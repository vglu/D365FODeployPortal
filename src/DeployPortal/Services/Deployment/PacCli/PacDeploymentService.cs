using System.IO.Compression;
using DeployPortal.Services.Deployment.Validation;

namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Default implementation of IPacDeploymentService.
/// Handles PAC CLI package deployment.
/// </summary>
public class PacDeploymentService : IPacDeploymentService
{
    private readonly IPacCliExecutor _pacExecutor;
    private readonly ILogger<PacDeploymentService> _logger;

    public PacDeploymentService(
        IPacCliExecutor pacExecutor,
        ILogger<PacDeploymentService> logger)
    {
        _pacExecutor = pacExecutor ?? throw new ArgumentNullException(nameof(pacExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task DeployAsync(
        string packagePath,
        string logFilePath,
        string isolatedAuthDir,
        Action<string>? onLog = null)
    {
        ArgumentNullException.ThrowIfNull(packagePath);
        ArgumentNullException.ThrowIfNull(logFilePath);
        ArgumentNullException.ThrowIfNull(isolatedAuthDir);

        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException($"Package not found: {packagePath}", packagePath);
        }

        var packageDir = Path.GetDirectoryName(packagePath)
            ?? throw new InvalidOperationException($"Cannot resolve directory for package: {packagePath}");

        var assetsDir = Path.Combine(packageDir, "PackageAssets");
        var importConfigPath = Path.Combine(assetsDir, "ImportConfig.xml");
        if (!File.Exists(importConfigPath))
        {
            throw new FileNotFoundException(
                "ImportConfig.xml not found next to TemplatePackage.dll. " +
                "Expected PackageAssets/ImportConfig.xml in the Unified package output. " +
                $"Looked for: {importConfigPath}",
                importConfigPath);
        }

        var assetCount = Directory.Exists(assetsDir)
            ? Directory.GetFiles(assetsDir).Length
            : 0;
        onLog?.Invoke($"Package: {packagePath}");
        onLog?.Invoke($"PackageAssets: {assetCount} file(s), ImportConfig present.");

        // PAC may load a bare TemplatePackage.dll without its sibling PackageAssets folder
        // (Config File Missing). Deploying a zip keeps DLL + PackageAssets together; PAC
        // extracts them to a temp folder (e.g. %TEMP%\xxxx.CPY\PackageAssets\ImportConfig.xml).
        // Zip must be created OUTSIDE packageDir (CreateFromDirectory cannot write into itself).
        var deployZipPath = Path.Combine(
            Path.GetTempPath(),
            $"pac_deploy_{Guid.NewGuid():N}.zip");
        onLog?.Invoke($"Packaging Unified folder for PAC: {Path.GetFileName(deployZipPath)}");
        if (File.Exists(deployZipPath))
            File.Delete(deployZipPath);
        ZipFile.CreateFromDirectory(packageDir, deployZipPath, CompressionLevel.Fastest, includeBaseDirectory: false);

        try
        {
            _logger.LogInformation(
                "Deploying package zip: {Zip} (from {Dir})",
                deployZipPath,
                packageDir);

            var envVars = new Dictionary<string, string>
            {
                ["PAC_AUTH_PROFILE_DIRECTORY"] = isolatedAuthDir
            };

            var arguments =
                $"package deploy --logConsole --verbose --package \"{deployZipPath}\" --logFile \"{logFilePath}\"";

            var result = await _pacExecutor.ExecuteAsync(
                arguments,
                packageDir,
                envVars,
                onOutput: onLog,
                onError: line => onLog?.Invoke($"[ERROR] {line}"));

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"PAC package deployment failed with exit code {result.ExitCode}. " +
                    $"Error: {result.StandardError}");
            }

            var combinedOutput = $"{result.StandardOutput}\n{result.StandardError}";
            var failureEvidence = PackageDeployFailureDetector.FindFailureEvidence(combinedOutput);
            if (failureEvidence != null)
            {
                _logger.LogError(
                    "PAC exited 0 but install failure detected in output: {Evidence}",
                    failureEvidence);
                throw new InvalidOperationException(
                    $"PAC package deployment reported failure despite exit code 0. {failureEvidence}");
            }

            _logger.LogInformation("Package deployment completed successfully");
        }
        finally
        {
            try
            {
                if (File.Exists(deployZipPath))
                    File.Delete(deployZipPath);
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
