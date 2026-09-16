namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Service for authenticating to Power Platform using PAC CLI.
/// </summary>
public interface IPacAuthService
{
    /// <summary>
    /// Acquires the process-wide exclusive PAC auth/bind window.
    /// Hold until auth, who, pre-deploy probes, and profile re-select finish; then dispose so
    /// long <c>package deploy</c> can run in parallel with other deployments.
    /// </summary>
    Task<IDisposable> AcquireAuthBindGateAsync(Action<string>? onLog = null);

    /// <summary>
    /// Authenticates to Power Platform environment.
    /// Uses Service Principal if available, otherwise falls back to interactive device code flow.
    /// Always ends with <c>auth select</c> for the environment profile.
    /// </summary>
    Task AuthenticateAsync(
        Models.Environment environment,
        string isolatedAuthDir,
        Action<string>? onLog = null);

    /// <summary>
    /// Re-selects the named auth profile in the isolated directory (before package deploy).
    /// </summary>
    Task SelectProfileAsync(
        Models.Environment environment,
        string isolatedAuthDir,
        Action<string>? onLog = null);

    /// <summary>
    /// Executes 'pac auth who' to get current authentication context.
    /// </summary>
    Task<string> WhoAmIAsync(string isolatedAuthDir);
}
