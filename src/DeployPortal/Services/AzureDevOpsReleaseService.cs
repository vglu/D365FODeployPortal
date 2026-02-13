using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DeployPortal.Services;

/// <summary>
/// Lists Release Pipelines and creates releases in Azure DevOps (vsrm.dev.azure.com).
/// Requires PAT with Release (Read, write & manage) or equivalent.
/// </summary>
public class AzureDevOpsReleaseService
{
    private const string DefinitionsApiVersion = "7.1-preview.1";
    private const string ReleasesApiVersion = "7.1-preview.8";

    private readonly ILogger<AzureDevOpsReleaseService> _logger;

    public AzureDevOpsReleaseService(ILogger<AzureDevOpsReleaseService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Lists Release Definitions (pipelines) in the project.
    /// </summary>
    public async Task<List<ReleaseDefinitionInfo>> ListReleaseDefinitionsAsync(string organization, string project, string pat)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        var url = $"https://vsrm.dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/release/definitions?api-version={DefinitionsApiVersion}";
        using var client = CreateClient(pat);
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var list = doc.RootElement.TryGetProperty("value", out var v) ? v : default;
        var result = new List<ReleaseDefinitionInfo>();
        foreach (var item in list.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            if (id != 0) result.Add(new ReleaseDefinitionInfo(id, name));
        }
        _logger.LogInformation("Listed {Count} release definitions for {Org}/{Project}", result.Count, organization, project);
        return result;
    }

    /// <summary>
    /// Lists Artifacts feed names in the project (feeds.dev.azure.com). PAT needs Packaging (Read).
    /// </summary>
    public async Task<List<string>> GetFeedNamesAsync(string organization, string project, string pat)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        var url = $"https://feeds.dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/packaging/feeds?api-version=7.1";
        return await GetFeedNamesFromUrlAsync(url, pat);
    }

    /// <summary>
    /// Lists Artifacts feed names at organization level (feeds not tied to a project). PAT needs Packaging (Read).
    /// </summary>
    public async Task<List<string>> GetFeedNamesOrgLevelAsync(string organization, string pat)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        var url = $"https://feeds.dev.azure.com/{Uri.EscapeDataString(organization)}/_apis/packaging/feeds?api-version=7.1";
        return await GetFeedNamesFromUrlAsync(url, pat);
    }

    private static async Task<List<string>> GetFeedNamesFromUrlAsync(string url, string pat)
    {
        using var client = CreateClient(pat);
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var list = doc.RootElement.TryGetProperty("value", out var v) ? v : default;
        var result = new List<string>();
        foreach (var item in list.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (!string.IsNullOrEmpty(name)) result.Add(name);
        }
        return result;
    }

    /// <summary>
    /// Creates an Artifacts feed in the project. PAT needs Packaging (Read, write). Feed name: max 64 chars, no spaces, no leading . or _
    /// </summary>
    public async Task<bool> CreateFeedAsync(string organization, string project, string feedName, string pat)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        if (string.IsNullOrWhiteSpace(feedName)) return false;
        var url = $"https://feeds.dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/packaging/feeds?api-version=7.1";
        var body = JsonSerializer.Serialize(new { name = feedName.Trim() });
        using var client = CreateClient(pat);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(url, content);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Created Artifacts feed '{Feed}' in {Org}/{Project}", feedName, organization, project);
            return true;
        }
        var responseBody = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict &&
            (responseBody.Contains("already exists", StringComparison.OrdinalIgnoreCase) || responseBody.Contains("FeedNameAlreadyExistsException", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Feed '{Feed}' already exists, proceeding.", feedName);
            return true;
        }
        _logger.LogWarning("Create feed failed: {StatusCode} {Body}", response.StatusCode, responseBody);
        return false;
    }

    /// <summary>
    /// Gets release definition details including artifacts (alias, type).
    /// </summary>
    public async Task<ReleaseDefinitionDetail?> GetReleaseDefinitionAsync(string organization, string project, int definitionId, string pat)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");
        var url = $"https://vsrm.dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/release/definitions/{definitionId}?api-version={DefinitionsApiVersion}";
        using var client = CreateClient(pat);
        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var artifacts = new List<ReleaseArtifactInfo>();
        if (root.TryGetProperty("artifacts", out var arts))
        {
            foreach (var a in arts.EnumerateArray())
            {
                var alias = a.TryGetProperty("alias", out var al) ? al.GetString() ?? "" : "";
                var type = a.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(alias)) artifacts.Add(new ReleaseArtifactInfo(alias, type));
            }
        }
        return new ReleaseDefinitionDetail(definitionId, name, artifacts);
    }

    /// <summary>
    /// Creates a new release. artifacts: list of { alias, version } for artifact instance (e.g. Universal Package version).
    /// For any artifacts in the definition with defaultVersionType=selectDuringReleaseCreationType that are NOT in the provided list,
    /// this method will auto-resolve them: for Build artifacts, uses latest successful build; for others, fails with clear message.
    /// </summary>
    public async Task<CreateReleaseResult> CreateReleaseAsync(
        string organization,
        string project,
        int definitionId,
        string description,
        IReadOnlyList<(string Alias, string Version)> artifacts,
        string pat)
    {
        if (string.IsNullOrWhiteSpace(pat)) throw new UnauthorizedAccessException("PAT is required.");

        // Get definition to find all artifacts that require version at creation
        var definition = await GetReleaseDefinitionAsync(organization, project, definitionId, pat);
        if (definition == null)
            return CreateReleaseResult.Failed("Could not retrieve release definition.");

        var artifactVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provided in artifacts)
            artifactVersions[provided.Alias] = provided.Version;

        using var client = CreateClient(pat);
        var defUrl = $"https://vsrm.dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/release/definitions/{definitionId}?api-version={DefinitionsApiVersion}";
        var defResp = await client.GetAsync(defUrl);
        if (!defResp.IsSuccessStatusCode)
            return CreateReleaseResult.Failed($"Failed to get definition details: {defResp.StatusCode}");

        var defJson = await defResp.Content.ReadAsStringAsync();
        using var defDoc = JsonDocument.Parse(defJson);
        var defRoot = defDoc.RootElement;
        if (!defRoot.TryGetProperty("artifacts", out var arts))
            return CreateReleaseResult.Failed("Release definition has no artifacts.");

        const string selectDuringCreationType = "selectDuringReleaseCreationType";
        foreach (var art in arts.EnumerateArray())
        {
            var alias = art.TryGetProperty("alias", out var al) ? al.GetString() : null;
            if (string.IsNullOrEmpty(alias) || artifactVersions.ContainsKey(alias))
                continue;

            if (!art.TryGetProperty("definitionReference", out var defRef))
                continue;
            if (!defRef.TryGetProperty("defaultVersionType", out var dvt))
                continue;
            var dvtId = dvt.TryGetProperty("id", out var dvtIdEl) ? dvtIdEl.GetString() : null;
            var dvtName = dvt.TryGetProperty("name", out var dvtNameEl) ? dvtNameEl.GetString() : null;

            var needsVersionAtCreation = string.Equals(dvtId, selectDuringCreationType, StringComparison.OrdinalIgnoreCase)
                                          || (dvtName?.Contains("release creation", StringComparison.OrdinalIgnoreCase) == true)
                                          || (dvtName?.Contains("specify at", StringComparison.OrdinalIgnoreCase) == true);
            if (!needsVersionAtCreation)
                continue;

            // Try to auto-resolve version
            var artType = art.TryGetProperty("type", out var atEl) ? atEl.GetString() : null;
            if (string.Equals(artType, "Build", StringComparison.OrdinalIgnoreCase))
            {
                if (defRef.TryGetProperty("definition", out var defIdEl) && defIdEl.TryGetProperty("id", out var buildDefIdStr))
                {
                    var buildDefId = buildDefIdStr.GetString();
                    var buildsUrl = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/build/builds?definitions={buildDefId}&$top=1&resultFilter=succeeded&api-version=7.1";
                    try
                    {
                        var buildsResp = await client.GetAsync(buildsUrl);
                        if (buildsResp.IsSuccessStatusCode)
                        {
                            var buildsJson = await buildsResp.Content.ReadAsStringAsync();
                            using var buildsDoc = JsonDocument.Parse(buildsJson);
                            if (buildsDoc.RootElement.TryGetProperty("value", out var val) && val.GetArrayLength() > 0)
                            {
                                var buildId = val[0].TryGetProperty("id", out var bidEl) ? bidEl.GetInt32() : 0;
                                if (buildId > 0)
                                {
                                    artifactVersions[alias] = buildId.ToString();
                                    _logger.LogInformation("Auto-resolved artifact '{Alias}' (Build) to latest build {BuildId}", alias, buildId);
                                    continue;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get latest build for artifact '{Alias}'", alias);
                    }
                }
            }

            // Could not resolve
            return CreateReleaseResult.Failed($"Artifact '{alias}' requires version at release creation but none provided and auto-resolve failed. Provide version for this artifact.");
        }

        var url = $"https://vsrm.dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/release/releases?api-version={ReleasesApiVersion}";
        var payload = new
        {
            definitionId,
            description,
            isDraft = false,
            artifacts = artifactVersions.Select(kv => new
            {
                alias = kv.Key,
                instanceReference = new { id = kv.Value, name = kv.Value }
            }).ToArray()
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(body);
            var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            _logger.LogInformation("Created release {ReleaseId} for definition {DefId}", id, definitionId);
            var orgUrl = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}";
            return CreateReleaseResult.Success(id, $"{orgUrl}/_release?releaseId={id}");
        }
        var errorMessage = body;
        try
        {
            using var errDoc = JsonDocument.Parse(body);
            if (errDoc.RootElement.TryGetProperty("message", out var msgEl))
                errorMessage = msgEl.GetString() ?? body;
        }
        catch { /* use raw body */ }
        _logger.LogWarning("Create release failed: {StatusCode} {Body}", response.StatusCode, body);
        return CreateReleaseResult.Failed(errorMessage);
    }

    /// <summary>
    /// Uploads a folder as Universal Package to Azure Artifacts feed via Azure CLI, then returns the version used.
    /// If packagePath is a .zip file, it is extracted to a temp folder first.
    /// Uses PAT via AZURE_DEVOPS_EXT_PAT so no separate 'az login' is required.
    /// Requires Azure CLI (az) with 'az artifacts universal' available.
    /// </summary>
    public async Task<(bool Success, string? Version, string? Error)> UploadUniversalPackageAsync(
        string organization,
        string project,
        string feed,
        string packageName,
        string version,
        string packagePath,
        string pat,
        CancellationToken cancellationToken = default)
    {
        var orgUrl = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}";
        string pathToPublish = packagePath;
        string? tempDir = null;
        try
        {
            if (packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(packagePath))
            {
                tempDir = Path.Combine(Path.GetTempPath(), "DeployPortal_Upack_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(packagePath, tempDir), cancellationToken);
                pathToPublish = tempDir;
            }
            else if (!Directory.Exists(packagePath))
            {
                return (false, null, "Package path does not exist or is not a .zip file.");
            }

            var azPath = GetAzExecutablePath();
            if (string.IsNullOrEmpty(azPath))
                return (false, null, "Azure CLI (az) not found. Install from https://learn.microsoft.com/cli/azure/install-azure-cli and ensure it is on PATH (on Windows, restart the app after installing).");

            var nameForAz = (packageName ?? "").ToLowerInvariant(); // Universal package name must be lowercase
            var args = new List<string>
            {
                "artifacts", "universal", "publish",
                "--organization", orgUrl,
                "--project", project ?? "",
                "--scope", "project",
                "--feed", feed ?? "",
                "--name", nameForAz,
                "--version", version ?? "",
                "--path", pathToPublish ?? ""
            };
            var startInfo = new ProcessStartInfo
            {
                FileName = azPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args)
                startInfo.ArgumentList.Add(a);
            if (!string.IsNullOrWhiteSpace(pat))
                startInfo.Environment["AZURE_DEVOPS_EXT_PAT"] = pat.Trim();

            using var process = Process.Start(startInfo);
            if (process == null)
                return (false, null, "Could not start Azure CLI. Ensure it is installed and on PATH.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                return (false, null, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);

            _logger.LogInformation("Universal package {Name} v{Version} published to feed {Feed}", packageName, version, feed);
            return (true, version, null);
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Resolves full path to Azure CLI (az). On Windows the installer adds az.cmd; process start may not resolve "az" without shell.
    /// </summary>
    private static string? GetAzExecutablePath()
    {
        const string azName = "az";
        if (OperatingSystem.IsWindows())
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            var extensions = new[] { "az.cmd", "az.exe", azName };
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var trimmed = dir.Trim();
                foreach (var ext in extensions)
                {
                    var full = Path.Combine(trimmed, ext);
                    if (File.Exists(full)) return full;
                }
            }
            var defaultWinPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft SDKs", "Azure", "CLI2", "wbin", "az.cmd");
            if (File.Exists(defaultWinPath)) return defaultWinPath;
            return null;
        }
        var fromPath = FindInPath(azName);
        return fromPath;
    }

    private static string? FindInPath(string executable)
    {
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var full = Path.Combine(dir.Trim(), executable);
                if (File.Exists(full)) return full;
#if NET
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    if (File.Exists(full + ".sh")) return full + ".sh";
                }
#endif
            }
        }
        catch { /* ignore */ }
        return null;
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

public record ReleaseDefinitionInfo(int Id, string Name);

public record ReleaseDefinitionDetail(int Id, string Name, List<ReleaseArtifactInfo> Artifacts);

public record ReleaseArtifactInfo(string Alias, string Type);

public record CreateReleaseResult(bool IsSuccess, int? ReleaseId, string? ReleaseUrl, string? ErrorMessage)
{
    public static CreateReleaseResult Success(int releaseId, string releaseUrl) =>
        new(true, releaseId, releaseUrl, null);
    public static CreateReleaseResult Failed(string errorMessage) =>
        new(false, null, null, errorMessage);
}
