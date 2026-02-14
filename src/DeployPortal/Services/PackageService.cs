using System.IO.Compression;
using DeployPortal.Data;
using DeployPortal.Models;
using DeployPortal.PackageOps;
using DeployPortal.Services.PackageContent;
using Microsoft.EntityFrameworkCore;

namespace DeployPortal.Services;

/// <summary>
/// Manages package upload, retrieval, deletion, and conversion orchestration.
/// Pure conversion/detection logic delegated to PackageOps.
/// </summary>
public class PackageService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ISettingsService _settings;
    private readonly IConvertService _convertService;
    private readonly IPackageChangeLogService _changeLogService;
    private readonly ILogger<PackageService> _logger;

    public PackageService(
        IDbContextFactory<AppDbContext> dbFactory,
        ISettingsService settings,
        IConvertService convertService,
        IPackageChangeLogService changeLogService,
        ILogger<PackageService> logger)
    {
        _dbFactory = dbFactory;
        _settings = settings;
        _convertService = convertService;
        _changeLogService = changeLogService;
        _logger = logger;
    }

    private string StoragePath => _settings.PackageStoragePath;

    /// <summary>Returns active (non-archived) packages.</summary>
    public async Task<List<Package>> GetAllAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Packages.Where(p => !p.IsArchived).OrderByDescending(p => p.UploadedAt).ToListAsync();
    }

    /// <summary>Returns archived packages.</summary>
    public async Task<List<Package>> GetArchivedAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Packages.Where(p => p.IsArchived).OrderByDescending(p => p.ArchivedAt ?? p.UploadedAt).ToListAsync();
    }

    public async Task<Package?> GetByIdAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Packages.FindAsync(id);
    }

    /// <summary>
    /// Uploads a package file. If packageType is null, auto-detects LCS vs Unified.
    /// </summary>
    public async Task<Package> UploadAsync(string fileName, Stream fileStream, string? packageType = null, string? devOpsTaskUrl = null)
    {
        Directory.CreateDirectory(StoragePath);

        var storedName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}_{fileName}";
        var storedPath = Path.Combine(StoragePath, storedName);

        await using (var fs = new FileStream(storedPath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fs);
        }

        var fileInfo = new FileInfo(storedPath);
        var detectedType = packageType ?? DetectPackageType(storedPath);

        var package = new Package
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            OriginalFileName = fileName,
            StoredFilePath = storedPath,
            FileSizeBytes = fileInfo.Length,
            PackageType = detectedType,
            UploadedAt = DateTime.UtcNow,
            DevOpsTaskUrl = string.IsNullOrWhiteSpace(devOpsTaskUrl) ? null : devOpsTaskUrl.Trim(),
            LicenseFileNames = GetLicenseFileNamesJson(storedPath)
        };

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        _logger.LogInformation("Package uploaded: {Name} ({Size} bytes, type: {Type})",
            package.Name, package.FileSizeBytes, package.PackageType);
        return package;
    }

    /// <summary>
    /// Updates the DevOps task URL on an existing package.
    /// </summary>
    public async Task UpdateDevOpsUrlAsync(int packageId, string? devOpsTaskUrl)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var package = await db.Packages.FindAsync(packageId);
        if (package == null) return;

        package.DevOpsTaskUrl = string.IsNullOrWhiteSpace(devOpsTaskUrl) ? null : devOpsTaskUrl.Trim();
        await db.SaveChangesAsync();

        _logger.LogInformation("Package {Name} DevOps URL updated to: {Url}", package.Name, package.DevOpsTaskUrl ?? "(cleared)");
    }

    /// <summary>
    /// Updates package name and/or DevOps task URL. Logs each change to package changelog.
    /// </summary>
    public async Task UpdatePackageAsync(int packageId, string? name, string? devOpsTaskUrl, string? changedBy = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var package = await db.Packages.FindAsync(packageId);
        if (package == null) return;

        var oldName = package.Name;
        var oldUrl = package.DevOpsTaskUrl;

        if (!string.IsNullOrWhiteSpace(name))
            package.Name = name.Trim();
        if (devOpsTaskUrl != null)
            package.DevOpsTaskUrl = string.IsNullOrWhiteSpace(devOpsTaskUrl) ? null : devOpsTaskUrl.Trim();
        await db.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(oldName, package.Name, StringComparison.Ordinal))
            await _changeLogService.LogChangeAsync(packageId, PackageChangeType.Updated, "Name", package.Name, $"Previous: {oldName}", changedBy);
        if (devOpsTaskUrl != null && oldUrl != package.DevOpsTaskUrl)
            await _changeLogService.LogChangeAsync(packageId, PackageChangeType.Updated, "TicketUrl", package.DevOpsTaskUrl ?? "(cleared)", $"Previous: {oldUrl ?? "(none)"}", changedBy);

        _logger.LogInformation("Package {Id} updated: Name={Name}, DevOpsUrl={Url}", packageId, package.Name, package.DevOpsTaskUrl ?? "(cleared)");
    }

    /// <summary>
    /// Archives a package (soft delete). It will appear only in the Archive tab.
    /// </summary>
    public async Task ArchiveAsync(int packageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var package = await db.Packages.FindAsync(packageId);
        if (package == null) return;

        package.IsArchived = true;
        package.ArchivedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation("Package archived: {Name} (Id={Id})", package.Name, packageId);
    }

    /// <summary>
    /// Restores an archived package to active list.
    /// </summary>
    public async Task RestoreAsync(int packageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var package = await db.Packages.FindAsync(packageId);
        if (package == null) return;

        package.IsArchived = false;
        package.ArchivedAt = null;
        await db.SaveChangesAsync();

        _logger.LogInformation("Package restored: {Name} (Id={Id})", package.Name, packageId);
    }

    /// <summary>
    /// Converts an LCS/Merged package to Unified format using the configured converter engine.
    /// </summary>
    public async Task<Package> ConvertToUnifiedAsync(Package sourcePackage, Action<string>? onLog = null)
    {
        if (sourcePackage.PackageType != "LCS" && sourcePackage.PackageType != "Merged")
            throw new InvalidOperationException($"Cannot convert {sourcePackage.PackageType} package to Unified.");

        if (!File.Exists(sourcePackage.StoredFilePath))
            throw new FileNotFoundException("Source package file not found.", sourcePackage.StoredFilePath);

        var tempDir = Path.Combine(_settings.TempWorkingDir, $"convert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempZip = Path.Combine(tempDir, Path.GetFileName(sourcePackage.StoredFilePath));
            File.Copy(sourcePackage.StoredFilePath, tempZip);

            var outputDir = await _convertService.ConvertToUnifiedAsync(tempZip, onLog);

            Directory.CreateDirectory(StoragePath);
            var outputName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{sourcePackage.Name}_Unified.zip";
            var outputPath = Path.Combine(StoragePath, outputName);

            ZipFile.CreateFromDirectory(outputDir, outputPath);

            var fileInfo = new FileInfo(outputPath);
            var pkg = new Package
            {
                Name = $"{sourcePackage.Name} (Unified)",
                OriginalFileName = outputName,
                StoredFilePath = outputPath,
                FileSizeBytes = fileInfo.Length,
                PackageType = "Unified",
                UploadedAt = DateTime.UtcNow,
                ParentMergeFromId = sourcePackage.Id,
                MergeSourceNames = System.Text.Json.JsonSerializer.Serialize(
                    new[] { $"{sourcePackage.Name} ({sourcePackage.PackageType})" }),
                LicenseFileNames = GetLicenseFileNamesJson(outputPath)
            };

            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Packages.Add(pkg);
            await db.SaveChangesAsync();

            // Ensure license icon shows: re-detect from file after zip is fully written
            var licJson = GetLicenseFileNamesJson(outputPath);
            if (licJson != null)
            {
                pkg.LicenseFileNames = licJson;
                await db.SaveChangesAsync();
            }

            onLog?.Invoke($"Conversion complete: {pkg.Name} ({FormatSizeStatic(fileInfo.Length)})");
            _logger.LogInformation("Package converted: {Source} → {Name}", sourcePackage.Name, pkg.Name);
            return pkg;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Converts a Unified package to LCS format.
    /// </summary>
    public async Task<Package> ConvertToLcsAsync(Package sourcePackage, Action<string>? onLog = null)
    {
        if (sourcePackage.PackageType != "Unified")
            throw new InvalidOperationException($"Cannot convert {sourcePackage.PackageType} to LCS.");

        if (!File.Exists(sourcePackage.StoredFilePath))
            throw new FileNotFoundException("Source package file not found.", sourcePackage.StoredFilePath);

        var tempDir = Path.Combine(_settings.TempWorkingDir, $"rev_convert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempZip = Path.Combine(tempDir, Path.GetFileName(sourcePackage.StoredFilePath));
            File.Copy(sourcePackage.StoredFilePath, tempZip);

            onLog?.Invoke("Converting Unified → LCS...");
            var outputDir = await _convertService.ConvertToLcsAsync(tempZip, onLog);

            Directory.CreateDirectory(StoragePath);
            var outputName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{sourcePackage.Name}_LCS.zip";
            var outputPath = Path.Combine(StoragePath, outputName);

            ZipFile.CreateFromDirectory(outputDir, outputPath);

            var fileInfo = new FileInfo(outputPath);
            var pkg = new Package
            {
                Name = $"{sourcePackage.Name} (LCS)",
                OriginalFileName = outputName,
                StoredFilePath = outputPath,
                FileSizeBytes = fileInfo.Length,
                PackageType = "LCS",
                UploadedAt = DateTime.UtcNow,
                ParentMergeFromId = sourcePackage.Id,
                MergeSourceNames = System.Text.Json.JsonSerializer.Serialize(
                    new[] { $"{sourcePackage.Name} ({sourcePackage.PackageType})" }),
                LicenseFileNames = GetLicenseFileNamesJson(outputPath)
            };

            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Packages.Add(pkg);
            await db.SaveChangesAsync();

            // Ensure license icon shows: re-detect from file after zip is fully written
            var licJson = GetLicenseFileNamesJson(outputPath);
            if (licJson != null)
            {
                pkg.LicenseFileNames = licJson;
                await db.SaveChangesAsync();
            }

            onLog?.Invoke($"Conversion complete: {pkg.Name} ({FormatSizeStatic(fileInfo.Length)})");
            _logger.LogInformation("Package converted: {Source} → {Name}", sourcePackage.Name, pkg.Name);
            return pkg;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Detects license file names in a package ZIP and returns JSON array or null.
    /// </summary>
    private static string? GetLicenseFileNamesJson(string zipPath)
    {
        var licFiles = PackageAnalyzer.DetectLicenseFiles(zipPath);
        return licFiles.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(licFiles) : null;
    }

    private static string FormatSizeStatic(long b)
    {
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:F1} KB";
        if (b < 1024L * 1024 * 1024) return $"{b / 1024.0 / 1024.0:F1} MB";
        return $"{b / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var package = await db.Packages.FindAsync(id);
        if (package == null) return;

        // Check if package is used in any deployments
        var deploymentsCount = await db.Deployments.CountAsync(d => d.PackageId == id);
        if (deploymentsCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete package '{package.Name}' because it is used in {deploymentsCount} deployment(s). " +
                $"Please delete all related deployments first.");
        }

        // Delete physical file
        if (File.Exists(package.StoredFilePath))
        {
            try
            {
                File.Delete(package.StoredFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete package file: {Path}", package.StoredFilePath);
                // Continue with DB deletion even if file deletion fails
            }
        }

        db.Packages.Remove(package);
        await db.SaveChangesAsync();

        _logger.LogInformation("Package deleted: {Name}", package.Name);
    }

    /// <summary>
    /// Checks if a package is used in any active (non-archived) deployments and returns usage information.
    /// </summary>
    public async Task<(int DeploymentsCount, List<string> EnvironmentNames)> GetPackageUsageAsync(int packageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        
        var deployments = await db.Deployments
            .Where(d => d.PackageId == packageId && !d.IsArchived) // Only active deployments
            .Include(d => d.Environment)
            .ToListAsync();

        var envNames = deployments
            .Select(d => d.Environment?.Name ?? "Unknown")
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        return (deployments.Count, envNames);
    }

    /// <summary>
    /// Extracts license file contents from a stored package.
    /// </summary>
    public async Task<List<(string FileName, byte[] Content)>> ExtractLicenseFilesAsync(int packageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var package = await db.Packages.FindAsync(packageId);
        if (package == null || !File.Exists(package.StoredFilePath))
            return new();

        return PackageAnalyzer.ExtractLicenseFileContents(package.StoredFilePath);
    }

    /// <summary>
    /// Injects license files into an existing package and updates the LicenseFileNames field.
    /// For LCS packages: places files into AOSService/Scripts/License/
    /// For Unified packages: injects into the first managed zip
    /// </summary>
    public async Task InjectLicenseFilesAsync(int packageId, List<(string FileName, byte[] Content)> licenseFiles)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var package = await db.Packages.FindAsync(packageId);
        if (package == null || !File.Exists(package.StoredFilePath))
            throw new FileNotFoundException("Package not found.");

        if (package.PackageType == "Unified")
        {
            // Extract package, inject licenses into managed ZIPs, re-pack
            var tempDir = Path.Combine(_settings.TempWorkingDir, $"lic_inject_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(package.StoredFilePath, tempDir);
                var assetsDir = Path.Combine(tempDir, "PackageAssets");
                if (Directory.Exists(assetsDir))
                {
                    // Collect existing + new, deduplicate
                    var existing = MergeEngine.CollectLicensesFromAssets(assetsDir);
                    var merged = new List<(string FileName, byte[] Content)>(existing);
                    foreach (var newLic in licenseFiles)
                    {
                        if (!merged.Any(l => l.FileName.Equals(newLic.FileName, StringComparison.OrdinalIgnoreCase)))
                            merged.Add(newLic);
                        else
                        {
                            // Replace existing with new version
                            merged.RemoveAll(l => l.FileName.Equals(newLic.FileName, StringComparison.OrdinalIgnoreCase));
                            merged.Add(newLic);
                        }
                    }
                    MergeEngine.InjectConsolidatedLicenses(assetsDir, merged);
                }

                File.Delete(package.StoredFilePath);
                System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, package.StoredFilePath);
            }
            finally { try { Directory.Delete(tempDir, true); } catch { } }
        }
        else
        {
            // LCS / Merged: put into AOSService/Scripts/License/
            using var zipStream = new FileStream(package.StoredFilePath, FileMode.Open, FileAccess.ReadWrite);
            using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Update);

            foreach (var lic in licenseFiles)
            {
                var entryPath = $"AOSService/Scripts/License/{lic.FileName}";
                // Remove existing entry if present
                var existing = archive.Entries.FirstOrDefault(e =>
                    e.FullName.Equals(entryPath, StringComparison.OrdinalIgnoreCase));
                existing?.Delete();

                var entry = archive.CreateEntry(entryPath);
                using var entryStream = entry.Open();
                await entryStream.WriteAsync(lic.Content);
            }
        }

        // Update file size & license list
        var fileInfo = new FileInfo(package.StoredFilePath);
        package.FileSizeBytes = fileInfo.Length;
        package.LicenseFileNames = GetLicenseFileNamesJson(package.StoredFilePath);
        await db.SaveChangesAsync();

        _logger.LogInformation("Injected {Count} license file(s) into package {Name}",
            licenseFiles.Count, package.Name);
    }

    /// <summary>
    /// Re-detects license files in an existing package and updates the db field.
    /// Returns the number of license files found. Throws if package or file not found.
    /// </summary>
    public async Task<int> RefreshLicenseInfoAsync(int packageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var package = await db.Packages.FindAsync(packageId);
        if (package == null)
            throw new InvalidOperationException("Package not found.");
        if (!File.Exists(package.StoredFilePath))
            throw new FileNotFoundException("Package file not found.", package.StoredFilePath);

        var licJson = GetLicenseFileNamesJson(package.StoredFilePath);
        package.LicenseFileNames = licJson;
        await db.SaveChangesAsync();

        if (string.IsNullOrEmpty(licJson)) return 0;
        var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(licJson);
        return list?.Count ?? 0;
    }

    /// <summary>
    /// Detects package type. Delegates to PackageAnalyzer.
    /// </summary>
    public static string DetectPackageType(string zipPath) =>
        PackageAnalyzer.DetectPackageType(zipPath);
}
