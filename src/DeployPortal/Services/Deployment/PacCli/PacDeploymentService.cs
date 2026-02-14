namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Default implementation of IPacDeploymentService.
/// Handles PAC CLI package deployment.
/// </summary>
public class PacDeploymentService : IPacDeploymentService
{
    private readonly IPacCliExecutor _pacExecutor;
    private readonly ISettingsService _settings;
    private readonly ILogger<PacDeploymentService> _logger;

    public PacDeploymentService(
        IPacCliExecutor pacExecutor,
        ISettingsService settings,
        ILogger<PacDeploymentService> logger)
    {
        _pacExecutor = pacExecutor ?? throw new ArgumentNullException(nameof(pacExecutor));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

        _logger.LogInformation("Deploying package: {Package}", packagePath);
        onLog?.Invoke($"Package: {packagePath}");

        var envVars = new Dictionary<string, string>
        {
            ["PAC_AUTH_PROFILE_DIRECTORY"] = isolatedAuthDir
        };

        var arguments = $"package deploy --logConsole --package \"{packagePath}\" --logFile \"{logFilePath}\"";
        var workingDir = GetWorkingDirectory();

        var result = await _pacExecutor.ExecuteAsync(
            arguments,
            workingDir,
            envVars,
            onOutput: onLog,
            onError: line => onLog?.Invoke($"[ERROR] {line}"));

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"PAC package deployment failed with exit code {result.ExitCode}. " +
                $"Error: {result.StandardError}");
        }

        _logger.LogInformation("Package deployment completed successfully");
    }

    private string GetWorkingDirectory()
    {
        var path = _settings.GetEffectiveModelUtilPath();
        return !string.IsNullOrEmpty(path)
            ? Path.GetDirectoryName(path)!
            : AppContext.BaseDirectory;
    }
}
