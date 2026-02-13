using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DeployPortal.Services;

/// <summary>
/// Lists and downloads build artifacts from Azure DevOps (dev.azure.com or *.visualstudio.com).
/// Requires a PAT with Build (Read) scope.
/// </summary>
public class AzureDevOpsBuildService
{
    private readonly ILogger<AzureDevOpsBuildService> _logger;

    public AzureDevOpsBuildService(ILogger<AzureDevOpsBuildService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses a build results or artifacts URL to organization, project, and build ID.
    /// Supports: https://dev.azure.com/{org}/{project}/_build/results?buildId=123
    /// and: https://{org}.visualstudio.com/{project}/_build/results?buildId=123
    /// </summary>
    public static bool TryParseBuildUrl(string url, out string organization, out string project, out int buildId)
    {
        organization = project = string.Empty;
        buildId = 0;
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            var uri = new Uri(url.Trim());
            var query = ParseQueryString(uri.Query);
            var buildIdStr = query.GetValueOrDefault("buildId", "");
            if (string.IsNullOrEmpty(buildIdStr) || !int.TryParse(buildIdStr, out buildId)) return false;

            var path = uri.AbsolutePath.Trim('/');
            var segments = path.Split('/');
            if (uri.Host.EndsWith("visualstudio.com", StringComparison.OrdinalIgnoreCase))
            {
                organization = uri.Host.Split('.')[0];
                project = segments.Length > 0 ? Uri.UnescapeDataString(segments[0]) : string.Empty;
            }
            else if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                organization = segments.Length > 0 ? segments[0] : string.Empty;
                project = segments.Length > 1 ? Uri.UnescapeDataString(segments[1]) : string.Empty;
            }
            else
                return false;

            return !string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(project) && buildId > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Lists build definitions (pipelines) in the project. nameFilter: if set, filter by name (API may use prefix; for "contains" use GetDefinitionsContainingName).
    /// </summary>
    public async Task<List<BuildDefinitionInfo>> ListDefinitionsAsync(string organization, string project, string pat, string? nameFilter = null)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        var query = new List<string> { "api-version=7.1", "$top=200" };
        if (!string.IsNullOrWhiteSpace(nameFilter))
            query.Add($"name={Uri.EscapeDataString(nameFilter)}");
        var baseUrl = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/build/definitions?{string.Join("&", query)}";
        using var client = CreateClient(pat);
        var response = await client.GetAsync(baseUrl);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var list = doc.RootElement.TryGetProperty("value", out var v) ? v : default;
        var result = new List<BuildDefinitionInfo>();
        foreach (var item in list.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (id > 0 && !string.IsNullOrEmpty(name))
                result.Add(new BuildDefinitionInfo(id, name));
        }
        return result;
    }

    /// <summary>
    /// Lists completed builds. top: max count (default 10). definitionIds: filter by pipeline id(s). definitionNameFilter: "contains" — definitions whose name contains this text. minTime/maxTime: date filter (UTC).
    /// </summary>
    public async Task<List<BuildInfo>> ListBuildsAsync(string organization, string project, string pat,
        int top = 10, IEnumerable<int>? definitionIds = null, string? definitionNameFilter = null, DateTime? minTime = null, DateTime? maxTime = null)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        var ids = definitionIds?.ToList();
        if ((ids == null || ids.Count == 0) && !string.IsNullOrWhiteSpace(definitionNameFilter))
        {
            var defs = await ListDefinitionsAsync(organization, project, pat, nameFilter: null);
            var filter = definitionNameFilter.Trim();
            ids = defs.Where(d => d.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).Select(d => d.Id).ToList();
        }
        var query = new List<string>
        {
            "api-version=7.1",
            "$top=" + Math.Clamp(top, 1, 100),
            "resultFilter=completed",
            "statusFilter=completed",
            "queryOrder=finishTimeDescending"
        };
        if (ids != null && ids.Count > 0)
            query.Add("definitions=" + string.Join(",", ids));
        if (minTime.HasValue)
            query.Add("minTime=" + minTime.Value.ToUniversalTime().ToString("O"));
        if (maxTime.HasValue)
            query.Add("maxTime=" + maxTime.Value.ToUniversalTime().ToString("O"));

        var baseUrl = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/build/builds?{string.Join("&", query)}";
        using var client = CreateClient(pat);
        var response = await client.GetAsync(baseUrl);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var list = doc.RootElement.TryGetProperty("value", out var arr) ? arr : default;
        var result = new List<BuildInfo>();
        foreach (var item in list.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            var buildNumber = item.TryGetProperty("buildNumber", out var bn) ? bn.GetString() ?? "" : "";
            var defName = "";
            if (item.TryGetProperty("definition", out var def))
                defName = def.TryGetProperty("name", out var dn) ? dn.GetString() ?? "" : "";
            var resultStr = item.TryGetProperty("result", out var res) ? res.GetString() ?? "none" : "none";
            var finishTime = item.TryGetProperty("finishTime", out var ft) ? ft.GetString() ?? "" : "";
            var projName = "";
            if (item.TryGetProperty("project", out var proj))
                projName = proj.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "";
            if (id <= 0) continue;
            result.Add(new BuildInfo(id, buildNumber, defName, resultStr, finishTime, projName));
        }
        _logger.LogInformation("Listed {Count} builds for {Org}/{Project}", result.Count, organization, project);
        return result;
    }

    /// <summary>
    /// Gets work items associated with the build (e.g. linked commits/PRs). Returns refs with id and url. Use the first one for DevOpsTaskUrl.
    /// </summary>
    public async Task<List<WorkItemRef>> GetBuildWorkItemsRefsAsync(string organization, string project, int buildId, string pat, int top = 10)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/build/builds/{buildId}/workitems?api-version=7.1&$top={Math.Clamp(top, 1, 100)}";
        using var client = CreateClient(pat);
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var list = doc.RootElement.TryGetProperty("value", out var v) ? v : default;
        var result = new List<WorkItemRef>();
        foreach (var item in list.EnumerateArray())
        {
            var id = "";
            if (item.TryGetProperty("id", out var idEl))
                id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32().ToString() : idEl.GetString() ?? "";
            var workItemUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            if (!string.IsNullOrEmpty(workItemUrl))
                result.Add(new WorkItemRef(id, workItemUrl));
        }
        return result;
    }

    /// <summary>
    /// Returns the list of artifact names and their download URLs for a build.
    /// </summary>
    public async Task<List<BuildArtifactInfo>> ListArtifactsAsync(string organization, string project, int buildId, string pat)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        var baseUrl = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/build/builds/{buildId}/artifacts?api-version=7.1";
        using var client = CreateClient(pat);
        var response = await client.GetAsync(baseUrl);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var list = doc.RootElement.GetProperty("value");
        var result = new List<BuildArtifactInfo>();
        foreach (var item in list.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var resource = item.TryGetProperty("resource", out var r) ? r : (JsonElement?)null;
            var downloadUrl = resource?.ValueKind == JsonValueKind.Object && resource.Value.TryGetProperty("downloadUrl", out var d)
                ? d.GetString()
                : null;
            long? containerId = null;
            string? itemPath = null;
            var resourceType = resource?.ValueKind == JsonValueKind.Object == true && resource.Value.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString() : null;
            if (resource?.ValueKind == JsonValueKind.Object == true && resource.Value.TryGetProperty("data", out var dataEl))
            {
                var data = dataEl.GetString() ?? "";
                TryParseResourceData(data, name, out containerId, out itemPath);
            }
            if (!string.IsNullOrEmpty(name))
                result.Add(new BuildArtifactInfo(name, downloadUrl ?? "", containerId, itemPath, resourceType));
        }
        _logger.LogInformation("Listed {Count} artifacts for build {BuildId}", result.Count, buildId);
        return result;
    }

    /// <summary>
    /// Downloads an artifact as a stream (ZIP). Caller must dispose the stream.
    /// </summary>
    public async Task<Stream> DownloadArtifactAsync(string organization, string project, int buildId, string artifactName, string pat)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        var list = await ListArtifactsAsync(organization, project, buildId, pat);
        var artifact = list.FirstOrDefault(a => string.Equals(a.Name, artifactName, StringComparison.OrdinalIgnoreCase));
        if (artifact == null)
            throw new KeyNotFoundException($"Artifact '{artifactName}' not found.");
        if (string.IsNullOrEmpty(artifact.DownloadUrl))
            throw new InvalidOperationException($"No download URL for artifact '{artifactName}'.");

        byte[] bytes;
        try
        {
            // Try with PAT first (required for some Azure DevOps artifact URLs)
            using (var client = CreateClient(pat))
            {
                var response = await client.GetAsync(artifact.DownloadUrl);
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    // 400 often means URL is SAS (blob storage) and rejects auth header — retry without
                    response.Dispose();
                    using var noAuthClient = new HttpClient();
                    var retry = await noAuthClient.GetAsync(artifact.DownloadUrl);
                    retry.EnsureSuccessStatusCode();
                    bytes = await retry.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    bytes = await response.Content.ReadAsByteArrayAsync();
                }
            }
        }
        catch (HttpRequestException) when (artifact.DownloadUrl.Contains("blob.core.windows.net", StringComparison.OrdinalIgnoreCase))
        {
            // Blob URL: try without auth
            using var client = new HttpClient();
            var response = await client.GetAsync(artifact.DownloadUrl);
            response.EnsureSuccessStatusCode();
            bytes = await response.Content.ReadAsByteArrayAsync();
        }

        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
            throw new InvalidOperationException($"Artifact '{artifactName}' download did not return a valid ZIP file (got {bytes.Length} bytes). The URL may require different authentication or the artifact may be unavailable.");

        return new MemoryStream(bytes);
    }

    /// <summary>
    /// File Container API with api-version=6.0-preview (required by Azure DevOps). URL without project.
    /// Returns all items in container; filter by path.StartsWith(artifactName) when building tree.
    /// </summary>
    public async Task<List<ContainerItem>> GetContainerItemsAsync(string organization, long containerId, string pat)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/_apis/resources/Containers/{containerId}?api-version=6.0-preview";
        using var client = CreateClient(pat);
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var list) || list.ValueKind != JsonValueKind.Array)
            return new List<ContainerItem>();
        var result = new List<ContainerItem>();
        foreach (var item in list.EnumerateArray())
        {
            var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(path) && item.TryGetProperty("itemPath", out var ip))
                path = ip.GetString() ?? "";
            path = path?.Replace('\\', '/').TrimEnd('/') ?? "";
            if (string.IsNullOrEmpty(path)) continue;
            var itemType = item.TryGetProperty("itemType", out var t) ? t.GetString() ?? "" : (item.TryGetProperty("type", out var t2) ? t2.GetString() ?? "" : "");
            var isFolder = string.Equals(itemType, "folder", StringComparison.OrdinalIgnoreCase);
            result.Add(new ContainerItem(path, isFolder));
        }
        return result;
    }

    private static List<ArtifactNode> BuildTreeFromContainerItems(List<ContainerItem> items, string artifactName, int maxDepth)
    {
        var byPath = new Dictionary<string, ArtifactNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var path = item.Path.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(path)) continue;
            var segments = path.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (segments.Length == 0 || segments.Length > maxDepth) continue;
            var currentPath = "";
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                var parentPath = currentPath;
                currentPath = string.IsNullOrEmpty(currentPath) ? segment : currentPath + "/" + segment;
                var isLast = i == segments.Length - 1;
                var isFolder = isLast ? item.IsFolder : true;
                if (byPath.TryGetValue(currentPath, out _)) continue;
                var node = new ArtifactNode(segment, currentPath, isFolder, new List<ArtifactNode>(), i + 1);
                byPath[currentPath] = node;
                if (!string.IsNullOrEmpty(parentPath) && byPath.TryGetValue(parentPath, out var parent))
                    parent.Children.Add(node);
            }
        }
        var rootNodes = byPath.Values.Where(n => n.Depth == 1).OrderBy(n => n.Name).ToList();
        if (rootNodes.Count == 1 && rootNodes[0].IsFolder && rootNodes[0].Children.Count > 0)
            return rootNodes[0].Children.OrderBy(c => c.IsFolder ? 0 : 1).ThenBy(c => c.Name).ToList();
        return rootNodes;
    }

    /// <summary>
    /// Returns the directory tree of an artifact. Uses File Container API (6.0-preview) when type=Container; else ZIP download.
    /// </summary>
    public async Task<List<ArtifactNode>> GetArtifactContentsAsync(string organization, string project, int buildId, string artifactName, string pat, int maxDepth = 8)
    {
        var list = await ListArtifactsAsync(organization, project, buildId, pat);
        var artifact = list.FirstOrDefault(a => string.Equals(a.Name, artifactName, StringComparison.OrdinalIgnoreCase));
        if (artifact == null) return new List<ArtifactNode>();

        if (!artifact.ContainerId.HasValue)
            _logger.LogDebug("Artifact {Name} has no ContainerId (resource.data missing or not parsed).", artifactName);

        if (artifact.ContainerId.HasValue)
        {
            try
            {
                var items = await GetContainerItemsAsync(organization, artifact.ContainerId.Value, pat);
                _logger.LogInformation("File Container API returned {Total} items for container {ContainerId}. Filtering by artifact '{Name}'.", items.Count, artifact.ContainerId.Value, artifactName);
                var filtered = items.Where(i => i.Path.Equals(artifactName, StringComparison.OrdinalIgnoreCase)
                    || i.Path.StartsWith(artifactName + "/", StringComparison.OrdinalIgnoreCase)).ToList();
                if (filtered.Count > 0)
                {
                    _logger.LogInformation("Artifact {Name}: {Count} items after filter. Building tree.", artifactName, filtered.Count);
                    return BuildTreeFromContainerItems(filtered, artifactName, maxDepth);
                }
                if (items.Count > 0)
                    _logger.LogWarning("Artifact {Name}: 0 items after filter (expected path '{Name}' or '{Name}/...'). Sample paths from API: {Sample}.",
                        artifactName, artifactName, artifactName, string.Join("; ", items.Take(5).Select(i => i.Path)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "File Container API failed for artifact {Name}, falling back to ZIP", artifactName);
            }
        }

        try
        {
            await using var zipStream = await DownloadArtifactAsync(organization, project, buildId, artifactName, pat);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            var byPath = new Dictionary<string, ArtifactNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName.Replace('\\', '/').TrimEnd('/');
                if (string.IsNullOrEmpty(path)) continue;
                var isDir = entry.Length == 0;
                var segments = path.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray();
                if (segments.Length == 0 || segments.Length > maxDepth) continue;
                var currentPath = "";
                for (var i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    var parentPath = currentPath;
                    currentPath = string.IsNullOrEmpty(currentPath) ? segment : currentPath + "/" + segment;
                    var isLast = i == segments.Length - 1;
                    var isFolder = isLast ? isDir : true;
                    if (byPath.TryGetValue(currentPath, out _)) continue;
                    var node = new ArtifactNode(
                        Name: segment,
                        RelativePath: currentPath,
                        IsFolder: isFolder,
                        Children: new List<ArtifactNode>(),
                        Depth: i + 1
                    );
                    byPath[currentPath] = node;
                    if (string.IsNullOrEmpty(parentPath))
                        continue;
                    if (byPath.TryGetValue(parentPath, out var parent))
                        parent.Children.Add(node);
                }
            }
            var rootNodes = byPath.Values.Where(n => n.Depth == 1).OrderBy(n => n.Name).ToList();
            // If there is exactly one root node and it's a folder with children, show its children at top level (like Azure DevOps UI: Packages → files directly)
            if (rootNodes.Count == 1 && rootNodes[0].IsFolder && rootNodes[0].Children.Count > 0)
                return rootNodes[0].Children.OrderBy(c => c.IsFolder ? 0 : 1).ThenBy(c => c.Name).ToList();
            return rootNodes;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("did not return a valid ZIP", StringComparison.OrdinalIgnoreCase))
        {
            // e.g. build.SourceLabel — not a ZIP, skip without error
            return new List<ArtifactNode>();
        }
        catch (InvalidDataException)
        {
            // Corrupt or non-ZIP response (e.g. "End of Central Directory could not be found")
            return new List<ArtifactNode>();
        }
    }

    /// <summary>
    /// Downloads a single file from an artifact ZIP. relativePath is the path inside the zip (e.g. "Packages/file.zip"). Caller must dispose the stream.
    /// </summary>
    public async Task<Stream> DownloadArtifactEntryAsync(string organization, string project, int buildId, string artifactName, string relativePath, string pat)
    {
        await using var zipStream = await DownloadArtifactAsync(organization, project, buildId, artifactName, pat);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var path = relativePath.Replace('\\', '/');
        var entry = archive.GetEntry(path) ?? archive.GetEntry(path + "/");
        if (entry == null)
            entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').TrimEnd('/').Equals(path, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            throw new KeyNotFoundException($"Entry '{relativePath}' not found in artifact '{artifactName}'.");
        if (entry.Length == 0)
            throw new InvalidOperationException($"'{relativePath}' is a folder, not a file.");
        var ms = new MemoryStream();
        await using (var es = entry.Open())
            await es.CopyToAsync(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Downloads all entries under a folder path from the artifact ZIP and returns a new ZIP containing only that folder (Unified package by files → one zip). Caller must dispose the stream.
    /// </summary>
    public async Task<Stream> DownloadArtifactFolderAsZipAsync(string organization, string project, int buildId, string artifactName, string folderRelativePath, string pat)
    {
        await using var zipStream = await DownloadArtifactAsync(organization, project, buildId, artifactName, pat);
        using var sourceArchive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var folderPrefix = folderRelativePath.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(folderPrefix))
            folderPrefix = "";
        else
            folderPrefix = folderPrefix + "/";

        var ms = new MemoryStream();
        using (var outArchive = new ZipArchive(ms, ZipArchiveMode.Create))
        {
            foreach (var entry in sourceArchive.Entries)
            {
                var fullName = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(fullName)) continue;
                if (!fullName.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var relativeName = fullName[folderPrefix.Length..].TrimStart('/');
                if (string.IsNullOrEmpty(relativeName)) continue;
                if (entry.Length == 0)
                {
                    if (!relativeName.EndsWith("/"))
                        relativeName += "/";
                    outArchive.CreateEntry(relativeName);
                    continue;
                }
                var outEntry = outArchive.CreateEntry(relativeName, System.IO.Compression.CompressionLevel.Optimal);
                await using (var srcStream = entry.Open())
                using (var dstStream = outEntry.Open())
                    await srcStream.CopyToAsync(dstStream);
            }
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>Parses resource.data e.g. "#/17627872/Packages" into containerId and itemPath.</summary>
    private static void TryParseResourceData(string data, string artifactName, out long? containerId, out string? itemPath)
    {
        containerId = null;
        itemPath = null;
        if (string.IsNullOrWhiteSpace(data) || !data.StartsWith("#/", StringComparison.Ordinal)) return;
        var parts = data.Split('/').Where(s => !string.IsNullOrEmpty(s)).ToArray();
        if (parts.Length < 2) return; // "#" and id
        if (long.TryParse(parts[1], out var id))
        {
            containerId = id;
            itemPath = parts.Length > 2 ? string.Join("/", parts.Skip(2)) : artifactName;
        }
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var idx = pair.IndexOf('=');
            var key = idx >= 0 ? Uri.UnescapeDataString(pair[..idx]) : Uri.UnescapeDataString(pair);
            var value = idx >= 0 && idx < pair.Length - 1 ? Uri.UnescapeDataString(pair[(idx + 1)..]) : "";
            if (!string.IsNullOrEmpty(key)) result[key] = value;
        }
        return result;
    }

    private static HttpClient CreateClient(string pat)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat.Trim()));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
        return client;
    }
}

/// <summary>Artifact metadata. ContainerId from resource.data (e.g. "#/17627872/Packages"); ResourceType e.g. "Container".</summary>
public record BuildArtifactInfo(string Name, string DownloadUrl, long? ContainerId = null, string? ItemPath = null, string? ResourceType = null);

/// <summary>Item from File Container API (path, folder/file).</summary>
public record ContainerItem(string Path, bool IsFolder);

public record BuildDefinitionInfo(int Id, string Name);
public record BuildInfo(int Id, string BuildNumber, string DefinitionName, string Result, string FinishTime, string ProjectName);

/// <summary>Node in artifact ZIP contents tree. RelativePath is path inside zip; empty for root. Max depth 4.</summary>
public record ArtifactNode(string Name, string RelativePath, bool IsFolder, List<ArtifactNode> Children, int Depth);

/// <summary>Work item linked to a build (id and url for DevOpsTaskUrl).</summary>
public record WorkItemRef(string Id, string Url);
