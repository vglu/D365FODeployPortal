using System.Net.Http.Json;
using System.Text.Json;

namespace DeployPortal.PackageOps;

/// <summary>
/// Azure Functions implementation of IPackageOpsService.
/// Uploads packages to Azure Blob Storage, triggers Azure Functions for processing,
/// and downloads the result back to the local machine.
/// 
/// NOTE: This is a placeholder implementation. To fully enable Azure processing:
/// 1. Deploy DeployPortal.Functions project to Azure
/// 2. Configure Azure Blob Storage connection string
/// 3. Configure Azure Functions base URL in Settings → Processing Mode
/// </summary>
public class AzurePackageOpsService : IPackageOpsService
{
    private readonly string _functionsBaseUrl;
    private readonly string _blobConnectionString;
    private readonly string _functionKey;
    private readonly string _tempDir;
    private readonly HttpClient _httpClient;

    /// <param name="functionsBaseUrl">Base URL of the Azure Functions app (e.g., https://my-func.azurewebsites.net)</param>
    /// <param name="blobConnectionString">Azure Blob Storage connection string</param>
    /// <param name="functionKey">Azure Functions host key for authentication</param>
    /// <param name="tempDir">Local temp directory for staging files</param>
    /// <param name="httpClient">Optional HttpClient instance</param>
    public AzurePackageOpsService(
        string functionsBaseUrl,
        string blobConnectionString,
        string functionKey,
        string tempDir,
        HttpClient? httpClient = null)
    {
        _functionsBaseUrl = functionsBaseUrl.TrimEnd('/');
        _blobConnectionString = blobConnectionString;
        _functionKey = functionKey;
        _tempDir = tempDir;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string> ConvertToUnifiedAsync(string lcsPackagePath, Action<string>? onLog = null)
    {
        onLog?.Invoke("[Azure] Uploading package for conversion...");

        // Upload to blob → call Azure Function → download result
        var blobName = await UploadToBlobAsync(lcsPackagePath, onLog);

        onLog?.Invoke("[Azure] Triggering ConvertToUnified function...");
        var resultBlobName = await CallFunctionAsync("api/convert-to-unified", new
        {
            blobName,
            direction = "LcsToUnified"
        }, onLog);

        onLog?.Invoke("[Azure] Downloading converted package...");
        var outputDir = Path.Combine(_tempDir, $"azure_result_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        await DownloadFromBlobAsync(resultBlobName, outputDir, onLog);

        onLog?.Invoke("[Azure] Conversion complete.");
        return outputDir;
    }

    public async Task<string> ConvertToLcsAsync(string unifiedPackagePath, Action<string>? onLog = null)
    {
        onLog?.Invoke("[Azure] Uploading package for LCS conversion...");
        var blobName = await UploadToBlobAsync(unifiedPackagePath, onLog);

        onLog?.Invoke("[Azure] Triggering ConvertToLcs function...");
        var resultBlobName = await CallFunctionAsync("api/convert-to-lcs", new
        {
            blobName,
            direction = "UnifiedToLcs"
        }, onLog);

        onLog?.Invoke("[Azure] Downloading converted package...");
        var outputDir = Path.Combine(_tempDir, $"azure_result_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);
        await DownloadFromBlobAsync(resultBlobName, outputDir, onLog);

        onLog?.Invoke("[Azure] Conversion complete.");
        return outputDir;
    }

    public string MergeLcs(List<string> packagePaths, Action<string>? onLog = null)
    {
        // For merge, we currently run locally even in Azure mode
        // (uploading multiple large files to blob and orchestrating is complex).
        // This can be enhanced with Durable Functions orchestration in the future.
        onLog?.Invoke("[Azure/Fallback] Running LCS merge locally (multi-file upload not yet supported)...");
        var engine = new MergeEngine(_tempDir);
        return engine.MergeLcs(packagePaths, onLog);
    }

    public string MergeUnified(List<string> packagePaths, Action<string>? onLog = null)
    {
        onLog?.Invoke("[Azure/Fallback] Running UDE merge locally (multi-file upload not yet supported)...");
        var engine = new MergeEngine(_tempDir);
        return engine.MergeUnified(packagePaths, onLog);
    }

    // Detection always runs locally — no need to call Azure for metadata inspection
    public string DetectPackageType(string zipPath) => PackageAnalyzer.DetectPackageType(zipPath);
    public string? DetectMergeStrategy(IEnumerable<string> packageTypes) =>
        PackageAnalyzer.DetectMergeStrategy(packageTypes);

    // ═══════════════════════════════════════════════════════════
    //  Azure Blob + Function helpers (stubs for full implementation)
    // ═══════════════════════════════════════════════════════════

    private async Task<string> UploadToBlobAsync(string filePath, Action<string>? onLog = null)
    {
        // TODO: Implement using Azure.Storage.Blobs SDK
        // var blobServiceClient = new BlobServiceClient(_blobConnectionString);
        // var containerClient = blobServiceClient.GetBlobContainerClient("packages");
        // await containerClient.CreateIfNotExistsAsync();
        // var blobName = $"{Guid.NewGuid():N}/{Path.GetFileName(filePath)}";
        // var blobClient = containerClient.GetBlobClient(blobName);
        // await blobClient.UploadAsync(filePath, overwrite: true);
        // return blobName;

        await Task.CompletedTask;
        var blobName = $"{Guid.NewGuid():N}/{Path.GetFileName(filePath)}";
        onLog?.Invoke($"[Azure] Would upload {Path.GetFileName(filePath)} as {blobName}");
        throw new NotImplementedException(
            "Azure Blob Storage upload not yet configured. " +
            "Please add Azure.Storage.Blobs NuGet package and configure the connection string in Settings.");
    }

    private async Task<string> CallFunctionAsync(string endpoint, object payload, Action<string>? onLog = null)
    {
        var url = $"{_functionsBaseUrl}/{endpoint}";
        if (!string.IsNullOrEmpty(_functionKey))
            url += (url.Contains('?') ? "&" : "?") + $"code={_functionKey}";

        var response = await _httpClient.PostAsJsonAsync(url, payload);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var resultBlobName = result.GetProperty("resultBlobName").GetString()
            ?? throw new InvalidOperationException("Azure Function did not return resultBlobName");

        onLog?.Invoke($"[Azure] Function completed. Result: {resultBlobName}");
        return resultBlobName;
    }

    private async Task DownloadFromBlobAsync(string blobName, string outputDir, Action<string>? onLog = null)
    {
        // TODO: Implement using Azure.Storage.Blobs SDK
        // var blobServiceClient = new BlobServiceClient(_blobConnectionString);
        // var containerClient = blobServiceClient.GetBlobContainerClient("packages");
        // var blobClient = containerClient.GetBlobClient(blobName);
        // var downloadPath = Path.Combine(outputDir, Path.GetFileName(blobName));
        // await blobClient.DownloadToAsync(downloadPath);
        // ZipFile.ExtractToDirectory(downloadPath, outputDir);

        await Task.CompletedTask;
        onLog?.Invoke($"[Azure] Would download {blobName} to {outputDir}");
        throw new NotImplementedException(
            "Azure Blob Storage download not yet configured.");
    }
}
