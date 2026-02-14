using DeployPortal.Models;

namespace DeployPortal.Services.PackageContent;

/// <summary>
/// Service for modifying package contents (adding/removing models and licenses).
/// Single Responsibility: Write operations on package files.
/// </summary>
public interface IPackageModificationService
{
    /// <summary>
    /// Adds a model (D365FO module) to a package.
    /// </summary>
    /// <param name="packageId">Target package ID</param>
    /// <param name="modelFilePath">Path to model file (.nupkg or .zip)</param>
    /// <param name="changedBy">User who made the change</param>
    /// <returns>Success status</returns>
    Task<(bool Success, string? ErrorMessage)> AddModelAsync(int packageId, string modelFilePath, string? changedBy = null);

    /// <summary>
    /// Removes a model from a package.
    /// </summary>
    /// <param name="packageId">Target package ID</param>
    /// <param name="modelName">Model name to remove</param>
    /// <param name="changedBy">User who made the change</param>
    /// <returns>Success status</returns>
    Task<(bool Success, string? ErrorMessage)> RemoveModelAsync(int packageId, string modelName, string? changedBy = null);

    /// <summary>
    /// Adds a license file to a package.
    /// </summary>
    /// <param name="packageId">Target package ID</param>
    /// <param name="licenseFilePath">Path to license file (.txt or .xml)</param>
    /// <param name="changedBy">User who made the change</param>
    /// <returns>Success status</returns>
    Task<(bool Success, string? ErrorMessage)> AddLicenseAsync(int packageId, string licenseFilePath, string? changedBy = null);

    /// <summary>
    /// Removes a license file from a package.
    /// </summary>
    /// <param name="packageId">Target package ID</param>
    /// <param name="licenseFileName">License file name to remove</param>
    /// <param name="changedBy">User who made the change</param>
    /// <returns>Success status</returns>
    Task<(bool Success, string? ErrorMessage)> RemoveLicenseAsync(int packageId, string licenseFileName, string? changedBy = null);

    /// <summary>
    /// Validates if a model can be removed without breaking dependencies.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="modelName">Model to check</param>
    /// <returns>Validation result with list of dependent models if any</returns>
    Task<(bool CanRemove, List<string> DependentModels)> ValidateModelRemovalAsync(int packageId, string modelName);
}
