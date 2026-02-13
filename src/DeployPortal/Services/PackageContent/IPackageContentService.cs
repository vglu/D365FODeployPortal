using DeployPortal.Models;

namespace DeployPortal.Services.PackageContent;

/// <summary>
/// Service for reading package contents (models and licenses).
/// Single Responsibility: Read-only operations on package files.
/// </summary>
public interface IPackageContentService
{
    /// <summary>
    /// Lists all models (D365FO modules) inside a package.
    /// </summary>
    /// <param name="packageId">Package ID from database</param>
    /// <returns>List of models with metadata</returns>
    Task<List<ModelInfo>> GetModelsAsync(int packageId);

    /// <summary>
    /// Lists all license files inside a package.
    /// </summary>
    /// <param name="packageId">Package ID from database</param>
    /// <returns>List of license files with metadata</returns>
    Task<List<LicenseInfo>> GetLicensesAsync(int packageId);

    /// <summary>
    /// Gets detailed information about a specific model.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="modelName">Model name</param>
    /// <returns>Model info with dependencies</returns>
    Task<ModelInfo?> GetModelDetailsAsync(int packageId, string modelName);

    /// <summary>
    /// Gets license file content for download/preview.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="licenseFileName">License file name</param>
    /// <returns>License info with content</returns>
    Task<LicenseInfo?> GetLicenseContentAsync(int packageId, string licenseFileName);
}
