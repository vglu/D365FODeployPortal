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

    /// <summary>
    /// Gets raw file content of a model (e.g. .nupkg or *_managed.zip) for download.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="modelFileName">Model file name as in package (e.g. dynamicsax-App.1.0.0.0.nupkg)</param>
    /// <returns>File content or null if not found</returns>
    Task<byte[]?> GetModelFileContentAsync(int packageId, string modelFileName);
}
