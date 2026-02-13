namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Service for authenticating to Power Platform using PAC CLI.
/// </summary>
public interface IPacAuthService
{
    /// <summary>
    /// Authenticates to Power Platform environment.
    /// Uses Service Principal if available, otherwise falls back to interactive device code flow.
    /// </summary>
    /// <param name="environment">Target environment with auth credentials</param>
    /// <param name="isolatedAuthDir">Isolated directory for PAC auth profile</param>
    /// <param name="onLog">Optional callback for logging</param>
    Task AuthenticateAsync(
        Models.Environment environment,
        string isolatedAuthDir,
        Action<string>? onLog = null);

    /// <summary>
    /// Executes 'pac auth who' to get current authentication context.
    /// </summary>
    /// <param name="isolatedAuthDir">Isolated directory for PAC auth profile</param>
    /// <returns>Output from 'pac auth who' command</returns>
    Task<string> WhoAmIAsync(string isolatedAuthDir);
}
