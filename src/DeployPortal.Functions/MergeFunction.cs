using System.IO.Compression;
using System.Text.Json;
using Azure.Storage.Blobs;
using DeployPortal.PackageOps;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace DeployPortal.Functions;

/// <summary>
/// Azure Function for package merging.
/// Uses the same MergeEngine from the shared PackageOps library.
/// </summary>
public class MergeFunction
{
    private readonly ILogger<MergeFunction> _logger;

    public MergeFunction(ILogger<MergeFunction> logger)
    {
        _logger = logger;
    }

    [Function("MergePackages")]
    public async Task<HttpResponseData> Merge(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "merge")] HttpRequestData req)
    {
        _logger.LogInformation("Merge function triggered");

        var requestBody = await JsonSerializer.DeserializeAsync<MergeRequest>(req.Body);
        if (requestBody == null || requestBody.BlobNames == null || requestBody.BlobNames.Count < 2)
        {
            var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("At least 2 blobNames are required");
            return badResponse;
        }

        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")!;
        var containerName = Environment.GetEnvironmentVariable("PackageBlobContainer") ?? "packages";
        var blobServiceClient = new BlobServiceClient(connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

        var tempDir = Path.Combine(Path.GetTempPath(), $"func_merge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Download all input blobs
            var localPaths = new List<string>();
            for (int i = 0; i < requestBody.BlobNames.Count; i++)
            {
                var blob = containerClient.GetBlobClient(requestBody.BlobNames[i]);
                var localPath = Path.Combine(tempDir, $"pkg_{i}_{Path.GetFileName(requestBody.BlobNames[i])}");
                await blob.DownloadToAsync(localPath);
                localPaths.Add(localPath);
            }

            // Detect strategy
            var types = localPaths.Select(p => PackageAnalyzer.DetectPackageType(p));
            var strategy = PackageAnalyzer.DetectMergeStrategy(types);

            if (strategy == null)
            {
                var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Incompatible package types for merge");
                return badResponse;
            }

            // Merge
            var engine = new MergeEngine(tempDir);
            string resultDir;

            if (strategy == "Unified")
                resultDir = engine.MergeUnified(localPaths, msg => _logger.LogInformation(msg));
            else
                resultDir = engine.MergeLcs(localPaths, msg => _logger.LogInformation(msg));

            // Zip and upload result
            var resultZipPath = Path.Combine(tempDir, $"merged_{Guid.NewGuid():N}.zip");
            ZipFile.CreateFromDirectory(resultDir, resultZipPath);

            var resultBlobName = $"results/{Guid.NewGuid():N}/merged.zip";
            var resultBlob = containerClient.GetBlobClient(resultBlobName);
            await resultBlob.UploadAsync(resultZipPath, overwrite: true);

            _logger.LogInformation("Merge complete. Strategy: {Strategy}, Result: {BlobName}", strategy, resultBlobName);

            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { resultBlobName, strategy });
            return response;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private record MergeRequest(List<string> BlobNames, string? Strategy);
}
