namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Default implementation of IPacAuthService.
/// Handles PAC CLI authentication using Service Principal or interactive device code flow.
/// </summary>
public class PacAuthService : IPacAuthService
{
    private readonly IPacCliExecutor _pacExecutor;
    private readonly ISecretProtectionService _secretService;
    private readonly ISettingsService _settings;
    private readonly ILogger<PacAuthService> _logger;

    public PacAuthService(
        IPacCliExecutor pacExecutor,
        ISecretProtectionService secretService,
        ISettingsService settings,
        ILogger<PacAuthService> logger)
    {
        _pacExecutor = pacExecutor ?? throw new ArgumentNullException(nameof(pacExecutor));
        _secretService = secretService ?? throw new ArgumentNullException(nameof(secretService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task AuthenticateAsync(
        Models.Environment environment,
        string isolatedAuthDir,
        Action<string>? onLog = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(isolatedAuthDir);

        var envVars = new Dictionary<string, string>
        {
            ["PAC_AUTH_PROFILE_DIRECTORY"] = isolatedAuthDir
        };

        if (environment.HasServicePrincipal)
        {
            await AuthenticateWithServicePrincipalAsync(environment, envVars, onLog);
        }
        else
        {
            await AuthenticateInteractivelyAsync(environment, envVars, onLog);
        }
    }

    public async Task<string> WhoAmIAsync(string isolatedAuthDir)
    {
        ArgumentNullException.ThrowIfNull(isolatedAuthDir);

        var envVars = new Dictionary<string, string>
        {
            ["PAC_AUTH_PROFILE_DIRECTORY"] = isolatedAuthDir
        };

        var workingDir = GetWorkingDirectory();
        var result = await _pacExecutor.ExecuteAsync("auth who", workingDir, envVars);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"'pac auth who' failed with exit code {result.ExitCode}. " +
                $"Error: {result.StandardError}");
        }

        return result.StandardOutput;
    }

    private async Task AuthenticateWithServicePrincipalAsync(
        Models.Environment environment,
        Dictionary<string, string> envVars,
        Action<string>? onLog)
    {
        _logger.LogInformation(
            "Authenticating to {Url} using Service Principal {AppId}",
            environment.Url,
            environment.ApplicationId);
        
        onLog?.Invoke($"Authenticating to {environment.Url} (Service Principal)...");

        var clientSecret = _secretService.Decrypt(environment.ClientSecretEncrypted);
        var arguments = 
            $"auth create " +
            $"--applicationId {environment.ApplicationId} " +
            $"--clientSecret \"{clientSecret}\" " +
            $"--tenant {environment.TenantId} " +
            $"--environment {environment.Url}";

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
                $"PAC authentication (Service Principal) failed with exit code {result.ExitCode}. " +
                $"Error: {result.StandardError}");
        }

        _logger.LogInformation("Service Principal authentication successful");
    }

    private async Task AuthenticateInteractivelyAsync(
        Models.Environment environment,
        Dictionary<string, string> envVars,
        Action<string>? onLog)
    {
        _logger.LogInformation("Authenticating to {Url} using interactive device code flow", environment.Url);
        
        onLog?.Invoke($"Authenticating to {environment.Url} (interactive — open the link from the log and enter the code)...");

        var arguments = $"auth create --environment \"{environment.Url}\" --deviceCode";
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
                $"PAC authentication (interactive) failed with exit code {result.ExitCode}. " +
                $"Error: {result.StandardError}");
        }

        _logger.LogInformation("Interactive authentication successful");
    }

    private string GetWorkingDirectory()
    {
        var path = _settings.GetEffectiveModelUtilPath();
        return !string.IsNullOrEmpty(path) 
            ? Path.GetDirectoryName(path)! 
            : AppContext.BaseDirectory;
    }
}
