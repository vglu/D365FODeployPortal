namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Service for deploying packages to Power Platform using PAC CLI.
/// </summary>
public interface IPacDeploymentService
{
    /// <summary>
    /// Deploys a Unified package (TemplatePackage.dll) to Power Platform environment.
    /// </summary>
    /// <param name="packagePath">Full path to TemplatePackage.dll</param>
    /// <param name="logFilePath">Path where PAC CLI should write deployment log</param>
    /// <param name="isolatedAuthDir">Isolated directory for PAC auth profile</param>
    /// <param name="onLog">Optional callback for logging</param>
    Task DeployAsync(
        string packagePath,
        string logFilePath,
        string isolatedAuthDir,
        Action<string>? onLog = null);
}
