namespace DeployPortal.Services.Deployment.Isolation;

/// <summary>
/// Manages isolated directories for PAC CLI auth profiles.
/// Ensures each deployment has its own isolated authentication context.
/// </summary>
public interface IIsolatedDirectoryManager
{
    /// <summary>
    /// Creates a unique isolated directory for a deployment.
    /// </summary>
    /// <param name="deploymentId">Deployment ID for naming</param>
    /// <returns>Full path to the created isolated directory</returns>
    string CreateIsolatedDirectory(int deploymentId);

    /// <summary>
    /// Deletes an isolated directory and all its contents.
    /// Safe to call even if directory doesn't exist.
    /// </summary>
    /// <param name="isolatedDirectory">Path to the isolated directory</param>
    void DeleteIsolatedDirectory(string isolatedDirectory);
}
