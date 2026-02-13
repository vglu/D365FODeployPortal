namespace DeployPortal.Services.Deployment;

/// <summary>
/// Main service for deploying D365FO packages to Power Platform environments.
/// Orchestrates authentication, validation, and deployment.
/// </summary>
public interface IDeployService
{
    /// <summary>
    /// Authenticates to Power Platform and deploys the package.
    /// Uses isolated PAC auth profile directory to prevent cross-deployment interference.
    /// Performs two-level validation: pre-deploy (pac auth who) and post-deploy (log parsing).
    /// </summary>
    /// <param name="environment">Target environment (with Service Principal or interactive auth)</param>
    /// <param name="unifiedPackageDir">Directory containing the Unified package (TemplatePackage.dll)</param>
    /// <param name="logFilePath">Path to the deployment log file</param>
    /// <param name="isolatedAuthDir">Unique directory for PAC auth profile isolation (enables parallel deployments)</param>
    /// <param name="onLog">Optional callback for logging messages</param>
    Task DeployPackageAsync(
        Models.Environment environment,
        string unifiedPackageDir,
        string logFilePath,
        string isolatedAuthDir,
        Action<string>? onLog = null);
}
