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

        // Package Deployer resolves ImportConfig.xml via GetImportPackageDataFolderName
        // (PackageAssets) relative to the process working directory / package location.
        // Running PAC from ModelUtil/app dir causes "Config File Missing".
        var packageDir = Path.GetDirectoryName(packagePath)
            ?? throw new InvalidOperationException($"Cannot resolve directory for package: {packagePath}");

        var importConfigPath = Path.Combine(packageDir, "PackageAssets", "ImportConfig.xml");
        if (!File.Exists(importConfigPath))
        {
            throw new FileNotFoundException(
                "ImportConfig.xml not found next to TemplatePackage.dll. " +
                "Expected PackageAssets/ImportConfig.xml in the Unified package output. " +
                $"Looked for: {importConfigPath}",
                importConfigPath);
        }

        _logger.LogInformation("Deploying package: {Package} (cwd: {Cwd})", packagePath, packageDir);
        onLog?.Invoke($"Package: {packagePath}");
        onLog?.Invoke($"Working directory: {packageDir}");

        var envVars = new Dictionary<string, string>
        {
            ["PAC_AUTH_PROFILE_DIRECTORY"] = isolatedAuthDir
        };

        var arguments = $"package deploy --logConsole --package \"{packagePath}\" --logFile \"{logFilePath}\"";

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

        // PAC can exit 0 while still printing install / config failures on stdout/stderr.
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
}
