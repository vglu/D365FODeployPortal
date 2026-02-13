using System.IO.Compression;
using DeployPortal.Data;
using DeployPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DeployPortal.Services.PackageContent;

/// <summary>
/// Implementation of IPackageModificationService.
/// Handles adding/removing models and licenses from D365FO packages.
/// </summary>
public class PackageModificationService : IPackageModificationService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IPackageContentService _contentService;
    private readonly IPackageChangeLogService _changeLogService;
    private readonly ILogger<PackageModificationService> _logger;

    public PackageModificationService(
        IDbContextFactory<AppDbContext> dbFactory,
        IPackageContentService contentService,
        IPackageChangeLogService changeLogService,
        ILogger<PackageModificationService> logger)
    {
        _dbFactory = dbFactory;
        _contentService = contentService;
        _changeLogService = changeLogService;
        _logger = logger;
    }

    public async Task<(bool Success, string? ErrorMessage)> AddModelAsync(
        int packageId, string modelFilePath, string? changedBy = null)
    {
        try
        {
            var package = await GetPackageAsync(packageId);
            if (package == null)
                return (false, "Package not found");

            if (!File.Exists(package.StoredFilePath))
                return (false, "Package file not found");

            if (!File.Exists(modelFilePath))
                return (false, "Model file not found");

            // Check for duplicates
            var existingModels = await _contentService.GetModelsAsync(packageId);
            var modelFileName = Path.GetFileName(modelFilePath);
            var isDuplicate = existingModels.Any(m =>
                m.FileName.Equals(modelFileName, StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
                return (false, $"Model '{modelFileName}' already exists in package");

            // Add model based on package type
            var result = package.PackageType switch
            {
                "LCS" or "Merged" => await AddModelToLcsPackageAsync(package.StoredFilePath, modelFilePath),
                "Unified" => await AddModelToUnifiedPackageAsync(package.StoredFilePath, modelFilePath),
                _ => (false, $"Unsupported package type: {package.PackageType}")
            };

            if (result.Item1)
            {
                await _changeLogService.LogChangeAsync(
                    packageId,
                    PackageChangeType.Added,
                    "Model",
                    modelFileName,
                    $"Size: {new FileInfo(modelFilePath).Length} bytes",
                    changedBy);

                _logger.LogInformation(
                    "Added model {ModelName} to package {PackageId} by {User}",
                    modelFileName, packageId, changedBy);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add model to package {PackageId}", packageId);
            return (false, $"Error adding model: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> RemoveModelAsync(
        int packageId, string modelName, string? changedBy = null)
    {
        try
        {
            var package = await GetPackageAsync(packageId);
            if (package == null)
                return (false, "Package not found");

            if (!File.Exists(package.StoredFilePath))
                return (false, "Package file not found");

            // Validate dependencies
            var (canRemove, dependentModels) = await ValidateModelRemovalAsync(packageId, modelName);
            if (!canRemove)
            {
                var deps = string.Join(", ", dependentModels);
                return (false, $"Cannot remove model '{modelName}': it is required by {deps}");
            }

            // Remove model based on package type
            var result = package.PackageType switch
            {
                "LCS" or "Merged" => await RemoveModelFromLcsPackageAsync(package.StoredFilePath, modelName),
                "Unified" => await RemoveModelFromUnifiedPackageAsync(package.StoredFilePath, modelName),
                _ => (false, $"Unsupported package type: {package.PackageType}")
            };

            if (result.Item1)
            {
                await _changeLogService.LogChangeAsync(
                    packageId,
                    PackageChangeType.Removed,
                    "Model",
                    modelName,
                    null,
                    changedBy);

                _logger.LogInformation(
                    "Removed model {ModelName} from package {PackageId} by {User}",
                    modelName, packageId, changedBy);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove model from package {PackageId}", packageId);
            return (false, $"Error removing model: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> AddLicenseAsync(
        int packageId, string licenseFilePath, string? changedBy = null)
    {
        try
        {
            var package = await GetPackageAsync(packageId);
            if (package == null)
                return (false, "Package not found");

            if (!File.Exists(package.StoredFilePath))
                return (false, "Package file not found");

            if (!File.Exists(licenseFilePath))
                return (false, "License file not found");

            var licenseFileName = Path.GetFileName(licenseFilePath);
            var ext = Path.GetExtension(licenseFileName).ToLowerInvariant();

            if (ext != ".txt" && ext != ".xml")
                return (false, "License file must be .txt or .xml");

            // Check for duplicates
            var existingLicenses = await _contentService.GetLicensesAsync(packageId);
            var isDuplicate = existingLicenses.Any(l =>
                l.FileName.Equals(licenseFileName, StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
                return (false, $"License '{licenseFileName}' already exists in package");

            // Add license based on package type
            var result = package.PackageType switch
            {
                "LCS" or "Merged" => await AddLicenseToLcsPackageAsync(package.StoredFilePath, licenseFilePath),
                "Unified" => await AddLicenseToUnifiedPackageAsync(package.StoredFilePath, licenseFilePath),
                _ => (false, $"Unsupported package type: {package.PackageType}")
            };

            if (result.Item1)
            {
                await _changeLogService.LogChangeAsync(
                    packageId,
                    PackageChangeType.Added,
                    "License",
                    licenseFileName,
                    $"Size: {new FileInfo(licenseFilePath).Length} bytes",
                    changedBy);

                _logger.LogInformation(
                    "Added license {LicenseFileName} to package {PackageId} by {User}",
                    licenseFileName, packageId, changedBy);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add license to package {PackageId}", packageId);
            return (false, $"Error adding license: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> RemoveLicenseAsync(
        int packageId, string licenseFileName, string? changedBy = null)
    {
        try
        {
            var package = await GetPackageAsync(packageId);
            if (package == null)
                return (false, "Package not found");

            if (!File.Exists(package.StoredFilePath))
                return (false, "Package file not found");

            // Remove license based on package type
            var result = package.PackageType switch
            {
                "LCS" or "Merged" => await RemoveLicenseFromLcsPackageAsync(package.StoredFilePath, licenseFileName),
                "Unified" => await RemoveLicenseFromUnifiedPackageAsync(package.StoredFilePath, licenseFileName),
                _ => (false, $"Unsupported package type: {package.PackageType}")
            };

            if (result.Item1)
            {
                await _changeLogService.LogChangeAsync(
                    packageId,
                    PackageChangeType.Removed,
                    "License",
                    licenseFileName,
                    null,
                    changedBy);

                _logger.LogInformation(
                    "Removed license {LicenseFileName} from package {PackageId} by {User}",
                    licenseFileName, packageId, changedBy);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove license from package {PackageId}", packageId);
            return (false, $"Error removing license: {ex.Message}");
        }
    }

    public async Task<(bool CanRemove, List<string> DependentModels)> ValidateModelRemovalAsync(
        int packageId, string modelName)
    {
        try
        {
            var models = await _contentService.GetModelsAsync(packageId);
            var dependentModels = models
                .Where(m => m.Dependencies.Contains(modelName, StringComparer.OrdinalIgnoreCase))
                .Select(m => m.Name)
                .ToList();

            return (dependentModels.Count == 0, dependentModels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate model removal for package {PackageId}", packageId);
            return (false, new List<string> { "Validation failed" });
        }
    }

    // ===== Private helpers =====

    private async Task<Package?> GetPackageAsync(int packageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Packages.FindAsync(packageId);
    }

    // ===== LCS Package Operations =====

    private async Task<(bool Success, string? ErrorMessage)> AddModelToLcsPackageAsync(
        string packagePath, string modelFilePath)
    {
        // TODO: Implement LCS-specific model addition
        // Models go to AOSService/Packages/files/
        return await Task.FromResult((false, "LCS model addition not yet implemented"));
    }

    private async Task<(bool Success, string? ErrorMessage)> RemoveModelFromLcsPackageAsync(
        string packagePath, string modelName)
    {
        // TODO: Implement LCS-specific model removal
        return await Task.FromResult((false, "LCS model removal not yet implemented"));
    }

    private async Task<(bool Success, string? ErrorMessage)> AddLicenseToLcsPackageAsync(
        string packagePath, string licenseFilePath)
    {
        // TODO: Implement LCS-specific license addition
        // Licenses go to AOSService/Scripts/License/
        return await Task.FromResult((false, "LCS license addition not yet implemented"));
    }

    private async Task<(bool Success, string? ErrorMessage)> RemoveLicenseFromLcsPackageAsync(
        string packagePath, string licenseFileName)
    {
        // TODO: Implement LCS-specific license removal
        return await Task.FromResult((false, "LCS license removal not yet implemented"));
    }

    // ===== Unified Package Operations =====

    private async Task<(bool Success, string? ErrorMessage)> AddModelToUnifiedPackageAsync(
        string packagePath, string modelFilePath)
    {
        // TODO: Implement Unified-specific model addition
        // Models are *_managed.zip files at root level
        return await Task.FromResult((false, "Unified model addition not yet implemented"));
    }

    private async Task<(bool Success, string? ErrorMessage)> RemoveModelFromUnifiedPackageAsync(
        string packagePath, string modelName)
    {
        // TODO: Implement Unified-specific model removal
        return await Task.FromResult((false, "Unified model removal not yet implemented"));
    }

    private async Task<(bool Success, string? ErrorMessage)> AddLicenseToUnifiedPackageAsync(
        string packagePath, string licenseFilePath)
    {
        // TODO: Implement Unified-specific license addition
        // Licenses go inside *_managed.zip files
        return await Task.FromResult((false, "Unified license addition not yet implemented"));
    }

    private async Task<(bool Success, string? ErrorMessage)> RemoveLicenseFromUnifiedPackageAsync(
        string packagePath, string licenseFileName)
    {
        // TODO: Implement Unified-specific license removal
        return await Task.FromResult((false, "Unified license removal not yet implemented"));
    }
}
