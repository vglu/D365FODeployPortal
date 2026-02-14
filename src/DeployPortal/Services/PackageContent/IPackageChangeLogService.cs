using DeployPortal.Models;

namespace DeployPortal.Services.PackageContent;

/// <summary>
/// Service for logging package modifications.
/// Single Responsibility: Audit trail for package changes.
/// </summary>
public interface IPackageChangeLogService
{
    /// <summary>
    /// Logs a change to a package (model or license added/removed).
    /// </summary>
    Task LogChangeAsync(
        int packageId,
        PackageChangeType changeType,
        string itemType,
        string itemName,
        string? details = null,
        string? changedBy = null);

    /// <summary>
    /// Gets change history for a package.
    /// </summary>
    /// <param name="packageId">Package ID</param>
    /// <param name="limit">Maximum number of records to return (default 100)</param>
    /// <returns>List of changes, newest first</returns>
    Task<List<PackageChangeLog>> GetChangeHistoryAsync(int packageId, int limit = 100);

    /// <summary>
    /// Gets recent changes across all packages.
    /// </summary>
    /// <param name="limit">Maximum number of records to return (default 50)</param>
    /// <returns>List of changes, newest first</returns>
    Task<List<PackageChangeLog>> GetRecentChangesAsync(int limit = 50);
}
