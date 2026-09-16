namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Service for deploying packages to Power Platform using PAC CLI.
/// </summary>
public interface IPacDeploymentService
{
    /// <summary>
    /// Deploys a Unified package to Power Platform.
    /// </summary>
    /// <param name="packagePath">Full path to TemplatePackage.dll</param>
    /// <param name="logFilePath">Path where PAC CLI should write deployment log</param>
    /// <param name="isolatedAuthDir">Isolated directory for PAC auth profile</param>
    /// <param name="onLog">Optional callback for logging</param>
    /// <param name="prebuiltZipPath">
    /// Optional zip of the Unified folder (from <see cref="UnifiedPackageZipBuilder"/>).
    /// When set, this file is used as <c>--package</c> and is not deleted by this method
    /// (caller owns lifetime). When null, a temporary zip is created and deleted here.
    /// </param>
    Task DeployAsync(
        string packagePath,
        string logFilePath,
        string isolatedAuthDir,
        Action<string>? onLog = null,
        string? prebuiltZipPath = null);
}
