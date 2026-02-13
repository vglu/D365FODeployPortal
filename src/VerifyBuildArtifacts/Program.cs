using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Build URL: from args or default (artifacts link works too - we only need buildId, org, project)
var buildUrl = args.Length > 0 ? args[0] : "https://sisn.visualstudio.com/SIS%20D365FO%20Products/_build/results?buildId=107394";
var pat = Environment.GetEnvironmentVariable("AZURE_DEVOPS_PAT");
if (string.IsNullOrWhiteSpace(pat))
{
    Console.Error.WriteLine("Set AZURE_DEVOPS_PAT environment variable with your Personal Access Token.");
    Console.Error.WriteLine("Example (PowerShell): $env:AZURE_DEVOPS_PAT = \"your-pat\"");
    return 1;
}

if (!TryParseBuildUrl(buildUrl, out var org, out var project, out var buildId))
{
    Console.Error.WriteLine("Could not parse build URL. Use: https://dev.azure.com/{org}/{project}/_build/results?buildId=123");
    return 1;
}

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"=== Analyzing build #{buildId} ===\n");

using var client = CreateClient(pat);

// 1. List artifacts
var artifactsUrl = $"https://dev.azure.com/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}/_apis/build/builds/{buildId}/artifacts?api-version=7.1";
var artifactsResp = await client.GetAsync(artifactsUrl);
artifactsResp.EnsureSuccessStatusCode();
var artifactsJson = await artifactsResp.Content.ReadAsStringAsync();
using var artifactsDoc = JsonDocument.Parse(artifactsJson);
var value = artifactsDoc.RootElement.GetProperty("value");

foreach (var art in value.EnumerateArray())
{
    var name = art.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
    var resource = art.TryGetProperty("resource", out var r) ? r : (JsonElement?)null;
    if (resource == null) continue;

    var resType = resource.Value.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
    if (!string.Equals(resType, "Container", StringComparison.OrdinalIgnoreCase))
        continue;

    var data = resource.Value.TryGetProperty("data", out var dataEl) ? dataEl.GetString() ?? "" : "";
    var match = Regex.Match(data, @"#/(\d+)");
    if (!match.Success) continue;
    var containerId = match.Groups[1].Value;

    // 2. File Container API (6.0-preview, no project in URL - like Python)
    var containerUrl = $"https://dev.azure.com/{Uri.EscapeDataString(org)}/_apis/resources/Containers/{containerId}?api-version=6.0-preview";
    HttpResponseMessage filesResp;
    try
    {
        filesResp = await client.GetAsync(containerUrl);
        filesResp.EnsureSuccessStatusCode();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to get {name}: {ex.Message}");
        continue;
    }

    var filesJson = await filesResp.Content.ReadAsStringAsync();
    using var filesDoc = JsonDocument.Parse(filesJson);
    if (!filesDoc.RootElement.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array)
    {
        Console.WriteLine($"No 'value' array for {name}");
        continue;
    }

    Console.WriteLine($"📦 Artifact: {name}");
    foreach (var item in items.EnumerateArray())
    {
        var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(path) && item.TryGetProperty("itemPath", out var ip))
            path = ip.GetString() ?? "";
        path = path?.Replace('\\', '/').TrimEnd('/') ?? "";
        if (string.IsNullOrEmpty(path)) continue;

        var itemType = item.TryGetProperty("itemType", out var t) ? t.GetString() ?? "" : "";
        if (!string.Equals(itemType, "file", StringComparison.OrdinalIgnoreCase))
            continue;
        if (!path.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            continue;

        Console.WriteLine($"  📄 {path}");
    }
    Console.WriteLine(new string('-', 30));
}

return 0;

static bool TryParseBuildUrl(string url, out string organization, out string project, out int buildId)
{
    organization = project = string.Empty;
    buildId = 0;
    if (string.IsNullOrWhiteSpace(url)) return false;
    try
    {
        var uri = new Uri(url.Trim());
        var q = uri.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2, StringSplitOptions.None))
            .Where(s => s.Length > 0)
            .ToDictionary(s => Uri.UnescapeDataString(s[0]), s => s.Length > 1 ? Uri.UnescapeDataString(s[1]) : "", StringComparer.OrdinalIgnoreCase);
        if (!q.TryGetValue("buildId", out var buildIdStr) || !int.TryParse(buildIdStr, out buildId)) return false;

        var path = uri.AbsolutePath.Trim('/');
        var segments = path.Split('/');
        if (uri.Host.EndsWith("visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            organization = uri.Host.Split('.')[0];
            project = segments.Length > 0 ? Uri.UnescapeDataString(segments[0]) : "";
        }
        else if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            organization = segments.Length > 0 ? segments[0] : "";
            project = segments.Length > 1 ? Uri.UnescapeDataString(segments[1]) : "";
        }
        else
            return false;

        return !string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(project) && buildId > 0;
    }
    catch { return false; }
}

static HttpClient CreateClient(string pat)
{
    var client = new HttpClient();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat.Trim()));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
    return client;
}
