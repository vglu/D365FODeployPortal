using DeployPortal.Data;
using DeployPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace DeployPortal.Services.PackageContent;

/// <summary>
/// Implementation of IPackageChangeLogService.
/// Logs package modifications to database for audit trail.
/// </summary>
public class PackageChangeLogService : IPackageChangeLogService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<PackageChangeLogService> _logger;

    public PackageChangeLogService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<PackageChangeLogService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task LogChangeAsync(
        int packageId,
        PackageChangeType changeType,
        string itemType,
        string itemName,
        string? details = null,
        string? changedBy = null)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var logEntry = new PackageChangeLog
            {
                PackageId = packageId,
                ChangeType = changeType,
                ItemType = itemType,
                ItemName = itemName,
                Details = details,
                ChangedBy = changedBy ?? "Unknown",
                ChangedAt = DateTime.UtcNow
            };

            db.PackageChangeLogs.Add(logEntry);
            await db.SaveChangesAsync();

            _logger.LogInformation(
                "Logged package change: PackageId={PackageId}, Type={ChangeType}, Item={ItemType}/{ItemName}, User={ChangedBy}",
                packageId, changeType, itemType, itemName, changedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log package change for PackageId={PackageId}", packageId);
            // Don't throw - logging failures shouldn't break the main operation
        }
    }

    public async Task<List<PackageChangeLog>> GetChangeHistoryAsync(int packageId, int limit = 100)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.PackageChangeLogs
            .Where(c => c.PackageId == packageId)
            .OrderByDescending(c => c.ChangedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<PackageChangeLog>> GetRecentChangesAsync(int limit = 50)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.PackageChangeLogs
            .Include(c => c.Package)
            .OrderByDescending(c => c.ChangedAt)
            .Take(limit)
            .ToListAsync();
    }
}
