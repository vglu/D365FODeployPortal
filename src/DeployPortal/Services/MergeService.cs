using System.IO.Compression;
using DeployPortal.Data;
using DeployPortal.Models;
using DeployPortal.PackageOps;
using Microsoft.EntityFrameworkCore;

namespace DeployPortal.Services;

/// <summary>
/// Orchestrates package merging. Pure merge logic delegated to MergeEngine (PackageOps).
/// This service handles DB records, file storage, and logging.
/// </summary>
public class MergeService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ISettingsService _settings;
    private readonly ILogger<MergeService> _logger;

    public MergeService(IDbContextFactory<AppDbContext> dbFactory, ISettingsService settings, ILogger<MergeService> logger)
    {
        _dbFactory = dbFactory;
        _settings = settings;
        _logger = logger;
    }

    private string TempDir => _settings.TempWorkingDir;
    private string StoragePath => _settings.PackageStoragePath;

    /// <summary>
    /// Determines the merge strategy based on package types.
    /// </summary>
    public static string? DetectMergeStrategy(List<Package> packages)
    {
        var types = packages.Select(p => p.PackageType).Distinct();
        return PackageAnalyzer.DetectMergeStrategy(types);
    }

    /// <summary>
    /// Returns LCS model conflicts (same model name in more than one package). Empty when strategy is not LCS.
    /// </summary>
    public static List<LcsModelConflict> GetMergeConflicts(List<Package> packages)
    {
        if (packages.Count < 2) return new List<LcsModelConflict>();
        var strategy = DetectMergeStrategy(packages);
        if (strategy != "LCS") return new List<LcsModelConflict>();
        var paths = packages.OrderBy(p => p.UploadedAt).Select(p => p.StoredFilePath).ToList();
        return MergeEngine.DetectLcsModelConflicts(paths);
    }

    public async Task<Package> MergePackagesAsync(List<Package> packages, string outputName, Action<string>? onLog = null, List<LcsModelConflictResolution>? modelResolutions = null)
    {
        if (packages.Count < 2)
            throw new ArgumentException("At least 2 packages are required for merge.");

        var strategy = DetectMergeStrategy(packages);
        var packagePaths = packages.Select(p => p.StoredFilePath).ToList();
        if (onLog == null)
            onLog = msg => _logger.LogInformation("[Merge] {Msg}", msg);

        var engine = new MergeEngine(TempDir);
        string resultDir;

        if (strategy == "Unified")
        {
            resultDir = engine.MergeUnified(packagePaths, onLog);
        }
        else
        {
            resultDir = engine.MergeLcs(packagePaths, onLog, modelResolutions);
        }

        try
        {
            return await CreateMergedPackage(packages, outputName, resultDir,
                strategy == "Unified" ? "Unified" : "LCS", onLog);
        }
        finally
        {
            // Clean up the work dir (parent of resultDir)
            try
            {
                var workDir = Path.GetDirectoryName(resultDir);
                if (workDir != null && Directory.Exists(workDir))
                    Directory.Delete(workDir, true);
            }
            catch { }
        }
    }

    private async Task<Package> CreateMergedPackage(
        List<Package> packages, string outputName, string sourceDir, string mergeType,
        Action<string>? onLog = null)
    {
        Directory.CreateDirectory(StoragePath);
        var outputFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{outputName}.zip";
        var outputPath = Path.Combine(StoragePath, outputFileName);

        onLog?.Invoke("Creating merged package ZIP...");
        ZipFile.CreateFromDirectory(sourceDir, outputPath);

        var fileInfo = new FileInfo(outputPath);
        var sourceNames = System.Text.Json.JsonSerializer.Serialize(
            packages.Select(p => $"{p.Name} ({p.PackageType})").ToArray());

        var mergedPackage = new Package
        {
            Name = outputName,
            OriginalFileName = outputFileName,
            StoredFilePath = outputPath,
            FileSizeBytes = fileInfo.Length,
            PackageType = mergeType == "Unified" ? "Unified" : "Merged",
            UploadedAt = DateTime.UtcNow,
            ParentMergeFromId = packages[0].Id,
            MergeSourceNames = sourceNames
        };

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Packages.Add(mergedPackage);
        await db.SaveChangesAsync();

        onLog?.Invoke($"Merge completed: {outputName} ({fileInfo.Length / 1024 / 1024} MB)");
        _logger.LogInformation("Packages merged ({Strategy}): {Name}", mergeType, outputName);

        return mergedPackage;
    }
}
