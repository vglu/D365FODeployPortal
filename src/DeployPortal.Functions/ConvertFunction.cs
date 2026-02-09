using System.IO.Compression;
using System.Text.Json;
using Azure.Storage.Blobs;
using DeployPortal.PackageOps;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace DeployPortal.Functions;

/// <summary>
/// Azure Function for package conversion (LCS ↔ Unified).
/// Uses the same ConvertEngine from the shared PackageOps library.
/// </summary>
public class ConvertFunction
{
    private readonly ILogger<ConvertFunction> _logger;

    public ConvertFunction(ILogger<ConvertFunction> logger)
    {
        _logger = logger;
    }

    [Function("ConvertToUnified")]
    public async Task<HttpResponseData> ConvertToUnified(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "convert-to-unified")] HttpRequestData req)
    {
        return await ProcessConversion(req, "LcsToUnified");
    }

    [Function("ConvertToLcs")]
    public async Task<HttpResponseData> ConvertToLcs(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "convert-to-lcs")] HttpRequestData req)
    {
        return await ProcessConversion(req, "UnifiedToLcs");
    }

    private async Task<HttpResponseData> ProcessConversion(HttpRequestData req, string direction)
    {
        _logger.LogInformation("Convert function triggered: {Direction}", direction);

        var requestBody = await JsonSerializer.DeserializeAsync<ConvertRequest>(req.Body);
        if (requestBody == null || string.IsNullOrEmpty(requestBody.BlobName))
        {
            var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badResponse.WriteStringAsync("Missing blobName in request body");
            return badResponse;
        }

        var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")!;
        var containerName = Environment.GetEnvironmentVariable("PackageBlobContainer") ?? "packages";
        var blobServiceClient = new BlobServiceClient(connectionString);
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

        // Download input blob to temp
        var tempDir = Path.Combine(Path.GetTempPath(), $"func_convert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var inputBlob = containerClient.GetBlobClient(requestBody.BlobName);
            var inputPath = Path.Combine(tempDir, Path.GetFileName(requestBody.BlobName));
            await inputBlob.DownloadToAsync(inputPath);

            // Convert using shared engine
            var templateDir = Path.Combine(AppContext.BaseDirectory, "Resources", "UnifiedTemplate");
            var engine = new ConvertEngine(tempDir, templateDir);

            string outputDir;
            if (direction == "LcsToUnified")
                outputDir = await engine.ConvertToUnifiedAsync(inputPath, msg => _logger.LogInformation(msg));
            else
                outputDir = await engine.ConvertToLcsAsync(inputPath, msg => _logger.LogInformation(msg));

            // Zip the result and upload
            var resultZipPath = Path.Combine(tempDir, $"result_{Guid.NewGuid():N}.zip");
            ZipFile.CreateFromDirectory(outputDir, resultZipPath);

            var resultBlobName = $"results/{Guid.NewGuid():N}/{Path.GetFileName(resultZipPath)}";
            var resultBlob = containerClient.GetBlobClient(resultBlobName);
            await resultBlob.UploadAsync(resultZipPath, overwrite: true);

            _logger.LogInformation("Conversion complete. Result: {BlobName}", resultBlobName);

            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { resultBlobName });
            return response;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private record ConvertRequest(string BlobName, string? Direction);
}
