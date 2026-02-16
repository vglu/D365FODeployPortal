using System.Text.RegularExpressions;

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
        // Normalize URL: no trailing slash to avoid "//.default" in token scope (PAC appends /.default)
        var environmentUrl = (environment.Url ?? "").TrimEnd('/');
        // --name: isolated profile per environment so parallel deployments do not conflict (each run uses PAC_AUTH_PROFILE_DIRECTORY + named profile)
        var profileName = GetSafeProfileName(environment.Name);
        var arguments =
            $"auth create " +
            $"--name \"{profileName}\" " +
            $"--applicationId {environment.ApplicationId} " +
            $"--clientSecret \"{clientSecret}\" " +
            $"--tenant {environment.TenantId} " +
            $"--environment {environmentUrl} " +
            $"--accept-cleartext-caching";

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

        // PAC can exit 0 but still report connection failure in output (e.g. "The user is not a member of the organization")
        var combinedOutput = (result.StandardOutput + "\n" + result.StandardError);
        if (combinedOutput.Contains("Could not connect to the Dataverse organization", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("The user is not a member of the organization", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("invalid status code 'Forbidden'", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PAC authentication reported connection failure. The Service Principal may not have access to this environment. " +
                "Check that the app is a member of the target organization. PAC output: " + combinedOutput.Trim());
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

        var environmentUrl = (environment.Url ?? "").TrimEnd('/');
        var profileName = GetSafeProfileName(environment.Name);
        var arguments = $"auth create --name \"{profileName}\" --environment \"{environmentUrl}\" --deviceCode --accept-cleartext-caching";
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

        var combinedOutputInteractive = (result.StandardOutput + "\n" + result.StandardError);
        if (combinedOutputInteractive.Contains("Could not connect to the Dataverse organization", StringComparison.OrdinalIgnoreCase) ||
            combinedOutputInteractive.Contains("The user is not a member of the organization", StringComparison.OrdinalIgnoreCase) ||
            combinedOutputInteractive.Contains("invalid status code 'Forbidden'", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PAC authentication reported connection failure. You may not have access to this environment. PAC output: " + combinedOutputInteractive.Trim());
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

    /// <summary>Safe profile name for pac auth create --name (alphanumeric, dash, underscore; max 50 chars).</summary>
    private static string GetSafeProfileName(string? environmentName)
    {
        var raw = environmentName ?? "Env";
        var sanitized = Regex.Replace(raw, @"[^a-zA-Z0-9\-_]", "_");
        if (sanitized.Length > 50) sanitized = sanitized[..50];
        return "Deploy_" + (string.IsNullOrEmpty(sanitized) ? "Env" : sanitized);
    }
}
