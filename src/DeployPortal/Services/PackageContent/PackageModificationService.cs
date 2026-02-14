using System.IO.Compression;
using DeployPortal.Data;
using DeployPortal.Models;
using DeployPortal.PackageOps;
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

    /// <summary>
    /// Modifies a ZIP file: optionally remove entries by path, optionally add new entries.
    /// Creates a temp file, then replaces the original (atomic on success).
    /// </summary>
    private static async Task<(bool Success, string? ErrorMessage)> ModifyZipAsync(
        string zipPath,
        HashSet<string>? removeEntryFullNames = null,
        List<(string EntryFullName, string SourceFilePath)>? addEntries = null)
    {
        removeEntryFullNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        addEntries ??= new List<(string, string)>();

        var tempPath = zipPath + ".tmp." + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using (var source = ZipFile.OpenRead(zipPath))
            await using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var dest = new ZipArchive(tempStream, ZipArchiveMode.Create))
            {
                foreach (var entry in source.Entries)
                {
                    if (removeEntryFullNames.Contains(entry.FullName))
                        continue;
                    if (entry.Length == 0 && entry.FullName.EndsWith("/", StringComparison.Ordinal))
                        continue; // directory entry, skip or create if needed
                    var destEntry = dest.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Optimal);
                    destEntry.LastWriteTime = entry.LastWriteTime;
                    await using (var srcStream = entry.Open())
                    await using (var dstStream = destEntry.Open())
                        await srcStream.CopyToAsync(dstStream);
                }

                foreach (var (entryFullName, sourceFilePath) in addEntries)
                {
                    var destEntry = dest.CreateEntry(entryFullName, System.IO.Compression.CompressionLevel.Optimal);
                    destEntry.LastWriteTime = DateTimeOffset.Now;
                    await using (var srcStream = File.OpenRead(sourceFilePath))
                    await using (var dstStream = destEntry.Open())
                        await srcStream.CopyToAsync(dstStream);
                }
            }

            File.Move(tempPath, zipPath, overwrite: true);
            return (true, null);
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
                try { File.Delete(tempPath); } catch { }
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Finds the path prefix used for models in LCS package (AOSService/Packages/files/ or AOSService/Packages/).
    /// </summary>
    private static string GetLcsModelsPathPrefix(ZipArchive zip)
    {
        var filesPrefix = "AOSService/Packages/files/";
        var packagesPrefix = "AOSService/Packages/";
        bool hasFiles = zip.Entries.Any(e => e.FullName.StartsWith(filesPrefix, StringComparison.OrdinalIgnoreCase));
        bool hasPackages = zip.Entries.Any(e => e.FullName.StartsWith(packagesPrefix, StringComparison.OrdinalIgnoreCase) && !e.FullName.StartsWith(filesPrefix, StringComparison.OrdinalIgnoreCase));
        if (hasFiles)
            return filesPrefix;
        if (hasPackages)
            return packagesPrefix;
        return filesPrefix; // default
    }

    private async Task<(bool Success, string? ErrorMessage)> AddModelToLcsPackageAsync(
        string packagePath, string modelFilePath)
    {
        var fileName = Path.GetFileName(modelFilePath);
        if (string.IsNullOrEmpty(fileName))
            return (false, "Invalid model file name");

        return await Task.Run(async () =>
        {
            try
            {
                string entryPath;
                using (var zip = ZipFile.OpenRead(packagePath))
                {
                    var prefix = GetLcsModelsPathPrefix(zip);
                    entryPath = prefix + fileName;
                }

                var addList = new List<(string EntryFullName, string SourceFilePath)> { (entryPath, modelFilePath) };

                // If user uploaded only .zip, generate minimal .nupkg and add it too (model = nupkg + zip in \files)
                if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var nupkgFileName = Path.GetFileNameWithoutExtension(fileName) + ".nupkg";
                    var nupkgPath = CreateMinimalNupkgForZipFileName(fileName);
                    if (nupkgPath != null)
                    {
                        try
                        {
                            using (var zip = ZipFile.OpenRead(packagePath))
                            {
                                var prefix = GetLcsModelsPathPrefix(zip);
                                addList.Add((prefix + nupkgFileName, nupkgPath));
                            }
                        }
                        finally
                        {
                            if (File.Exists(nupkgPath))
                                try { File.Delete(nupkgPath); } catch { }
                        }
                    }
                }

                return await ModifyZipAsync(packagePath, removeEntryFullNames: null, addEntries: addList);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }

    /// <summary>Creates a minimal .nupkg (zip with .nuspec only) so LCS has both nupkg and zip for the model.</summary>
    private static string? CreateMinimalNupkgForZipFileName(string zipFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(zipFileName);
        if (string.IsNullOrEmpty(baseName)) return null;
        // dynamicsax-atlas.388.10.0.41 -> id dynamicsax-atlas, version 388.10.0.41
        var parts = baseName.Split('.');
        string nuspecId = baseName;
        var version = "1.0.0.0";
        if (parts.Length >= 4)
        {
            var last4 = parts[^4..];
            if (last4.All(p => p.Length > 0 && p.All(char.IsDigit)))
            {
                version = string.Join(".", last4);
                nuspecId = string.Join(".", parts[..^4]);
            }
        }
        var nuspecEntryName = nuspecId + ".nuspec";
        var nuspecXml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"">
  <metadata>
    <id>{nuspecId}</id>
    <version>{version}</version>
  </metadata>
</package>";
        var tempPath = Path.Combine(Path.GetTempPath(), "DeployPortal_" + Guid.NewGuid().ToString("N")[..8] + ".nupkg");
        try
        {
            using (var zip = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry(nuspecEntryName, CompressionLevel.Fastest);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
                writer.Write(nuspecXml);
            }
            return tempPath;
        }
        catch
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
            return null;
        }
    }

    private async Task<(bool Success, string? ErrorMessage)> RemoveModelFromLcsPackageAsync(
        string packagePath, string modelName)
    {
        return await Task.Run(async () =>
        {
            try
            {
                var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var zip = ZipFile.OpenRead(packagePath))
                {
                    var prefix = GetLcsModelsPathPrefix(zip);
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.Length == 0)
                            continue;
                        var name = entry.FullName.Replace("\\", "/");
                        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            continue;
                        var filePart = name[prefix.Length..].TrimStart('/');
                        if (string.IsNullOrEmpty(filePart))
                            continue;
                        var entryFileName = filePart.Contains('/') ? filePart[..filePart.IndexOf('/')] : filePart;
                        var entryModelName = PackageAnalyzer.ExtractModuleNameFromNupkg(entryFileName);
                        if (entryModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase))
                            toRemove.Add(entry.FullName);
                    }
                }

                if (toRemove.Count == 0)
                    return (false, $"Model '{modelName}' not found in package");

                return await ModifyZipAsync(packagePath, removeEntryFullNames: toRemove, addEntries: null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }

    private static readonly string LcsLicensePrefix = "AOSService/Scripts/License/";

    private async Task<(bool Success, string? ErrorMessage)> AddLicenseToLcsPackageAsync(
        string packagePath, string licenseFilePath)
    {
        var fileName = Path.GetFileName(licenseFilePath);
        if (string.IsNullOrEmpty(fileName))
            return (false, "Invalid license file name");

        var entryPath = LcsLicensePrefix + fileName;
        var addList = new List<(string EntryFullName, string SourceFilePath)> { (entryPath, licenseFilePath) };
        return await ModifyZipAsync(packagePath, removeEntryFullNames: null, addEntries: addList);
    }

    private async Task<(bool Success, string? ErrorMessage)> RemoveLicenseFromLcsPackageAsync(
        string packagePath, string licenseFileName)
    {
        var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var zip = ZipFile.OpenRead(packagePath))
        {
            foreach (var entry in zip.Entries)
            {
                if (entry.Length == 0)
                    continue;
                var name = entry.FullName.Replace("\\", "/");
                if (!name.StartsWith(LcsLicensePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var filePart = name[LcsLicensePrefix.Length..].TrimStart('/');
                if (filePart.Equals(licenseFileName, StringComparison.OrdinalIgnoreCase))
                    toRemove.Add(entry.FullName);
            }
        }

        if (toRemove.Count == 0)
            return (false, $"License '{licenseFileName}' not found in package");

        return await ModifyZipAsync(packagePath, removeEntryFullNames: toRemove, addEntries: null);
    }

    // ===== Unified Package Operations =====

    private async Task<(bool Success, string? ErrorMessage)> AddModelToUnifiedPackageAsync(
        string packagePath, string modelFilePath)
    {
        var fileName = Path.GetFileName(modelFilePath);
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith("_managed.zip", StringComparison.OrdinalIgnoreCase))
            return (false, "Unified model must be a *_managed.zip file");

        var entryPath = fileName; // root of package
        var addList = new List<(string EntryFullName, string SourceFilePath)> { (entryPath, modelFilePath) };
        return await ModifyZipAsync(packagePath, removeEntryFullNames: null, addEntries: addList);
    }

    private async Task<(bool Success, string? ErrorMessage)> RemoveModelFromUnifiedPackageAsync(
        string packagePath, string modelName)
    {
        return await Task.Run(async () =>
        {
            var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = ZipFile.OpenRead(packagePath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.Length == 0)
                        continue;
                    var name = entry.FullName.Replace("\\", "/").TrimEnd('/');
                    if (!name.EndsWith("_managed.zip", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (name.Contains("DefaultDevSolution", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var fileName = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;
                    var entryModelName = PackageAnalyzer.ExtractModuleNameFromManagedZip(fileName);
                    if (entryModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase))
                        toRemove.Add(entry.FullName);
                }
            }

            if (toRemove.Count == 0)
                return (false, $"Model '{modelName}' not found in package");

            return await ModifyZipAsync(packagePath, removeEntryFullNames: toRemove, addEntries: null);
        });
    }

    /// <summary>
    /// For Unified packages, licenses live inside a *_managed.zip. We modify the first non-Default managed zip.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> AddLicenseToUnifiedPackageAsync(
        string packagePath, string licenseFilePath)
    {
        var fileName = Path.GetFileName(licenseFilePath);
        if (string.IsNullOrEmpty(fileName))
            return (false, "Invalid license file name");

        return await Task.Run(async () =>
        {
            try
            {
                byte[]? modifiedManagedZip = null;
                string? managedZipEntryName = null;

                using (var zip = ZipFile.OpenRead(packagePath))
                {
                    var mzEntry = zip.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith("_managed.zip", StringComparison.OrdinalIgnoreCase)
                        && !e.FullName.Contains("DefaultDevSolution", StringComparison.OrdinalIgnoreCase)
                        && e.Length > 0);

                    if (mzEntry == null)
                        return (false, "No *_managed.zip found in package to add license to");

                    managedZipEntryName = mzEntry.FullName;
                    var moduleName = PackageAnalyzer.ExtractModuleNameFromManagedZip(Path.GetFileName(mzEntry.Name));
                    var licenseGuid = Guid.NewGuid().ToString("N")[..8];
                    var innerEntryPath = $"{moduleName}/_License_{licenseGuid}/{fileName}";

                    using var ms = new MemoryStream();
                    using (var stream = mzEntry.Open())
                        await stream.CopyToAsync(ms);
                    ms.Position = 0;

                    using (var innerZip = new ZipArchive(ms, ZipArchiveMode.Update))
                    {
                        var newEntry = innerZip.CreateEntry(innerEntryPath, System.IO.Compression.CompressionLevel.Optimal);
                        await using (var src = File.OpenRead(licenseFilePath))
                        await using (var dst = newEntry.Open())
                            await src.CopyToAsync(dst);
                    }

                    modifiedManagedZip = ms.ToArray();
                }

                if (modifiedManagedZip == null || string.IsNullOrEmpty(managedZipEntryName))
                    return (false, "Could not modify inner package");

                var tempPath = packagePath + ".tmp." + Guid.NewGuid().ToString("N")[..8];
                try
                {
                    using (var source = ZipFile.OpenRead(packagePath))
                    await using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var dest = new ZipArchive(tempStream, ZipArchiveMode.Create))
                    {
                        foreach (var entry in source.Entries)
                        {
                            var en = entry.FullName;
                            if (en.Equals(managedZipEntryName, StringComparison.OrdinalIgnoreCase))
                            {
                                var destEntry = dest.CreateEntry(managedZipEntryName, System.IO.Compression.CompressionLevel.Optimal);
                                await using (var dstStream = destEntry.Open())
                                    await new MemoryStream(modifiedManagedZip).CopyToAsync(dstStream);
                                continue;
                            }
                            if (entry.Length == 0 && entry.FullName.EndsWith("/", StringComparison.Ordinal))
                                continue;
                            var destEntry2 = dest.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Optimal);
                            await using (var srcStream = entry.Open())
                            await using (var dstStream = destEntry2.Open())
                                await srcStream.CopyToAsync(dstStream);
                        }
                    }
                    File.Move(tempPath, packagePath, overwrite: true);
                    return (true, (string?)null);
                }
                catch
                {
                    if (File.Exists(tempPath))
                        try { File.Delete(tempPath); } catch { }
                    throw;
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }

    private async Task<(bool Success, string? ErrorMessage)> RemoveLicenseFromUnifiedPackageAsync(
        string packagePath, string licenseFileName)
    {
        return await Task.Run(async () =>
        {
            try
            {
                byte[]? modifiedManagedZip = null;
                string? managedZipEntryName = null;

                using (var zip = ZipFile.OpenRead(packagePath))
                {
                    foreach (var mzEntry in zip.Entries.Where(e =>
                        e.FullName.EndsWith("_managed.zip", StringComparison.OrdinalIgnoreCase)
                        && !e.FullName.Contains("DefaultDevSolution", StringComparison.OrdinalIgnoreCase)
                        && e.Length > 0))
                    {
                        using var ms = new MemoryStream();
                        using (var stream = mzEntry.Open())
                            await stream.CopyToAsync(ms);
                        ms.Position = 0;

                        var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        using (var innerZip = new ZipArchive(ms, ZipArchiveMode.Read))
                        {
                            foreach (var ie in innerZip.Entries)
                            {
                                if (ie.Length == 0) continue;
                                var fn = ie.FullName.Replace("\\", "/").Split('/', '\\').LastOrDefault();
                                if (string.Equals(fn, licenseFileName, StringComparison.OrdinalIgnoreCase))
                                    toRemove.Add(ie.FullName);
                            }
                        }

                        if (toRemove.Count == 0)
                            continue;

                        ms.Position = 0;
                        var tempMs = new MemoryStream();
                        using (var srcZip = new ZipArchive(ms, ZipArchiveMode.Read))
                        using (var destZip = new ZipArchive(tempMs, ZipArchiveMode.Create))
                        {
                            foreach (var entry in srcZip.Entries)
                            {
                                if (toRemove.Contains(entry.FullName)) continue;
                                if (entry.Length == 0 && entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                                var destEntry = destZip.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Optimal);
                                await using (var srcStream = entry.Open())
                                await using (var dstStream = destEntry.Open())
                                    await srcStream.CopyToAsync(dstStream);
                            }
                        }
                        modifiedManagedZip = tempMs.ToArray();
                        managedZipEntryName = mzEntry.FullName;
                        break;
                    }
                }

                if (modifiedManagedZip == null || string.IsNullOrEmpty(managedZipEntryName))
                    return (false, $"License '{licenseFileName}' not found in package");

                var tempPath = packagePath + ".tmp." + Guid.NewGuid().ToString("N")[..8];
                using (var source = ZipFile.OpenRead(packagePath))
                await using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var dest = new ZipArchive(tempStream, ZipArchiveMode.Create))
                {
                    foreach (var entry in source.Entries)
                    {
                        if (entry.FullName.Equals(managedZipEntryName, StringComparison.OrdinalIgnoreCase))
                        {
                            var destEntry = dest.CreateEntry(managedZipEntryName, System.IO.Compression.CompressionLevel.Optimal);
                            await using (var dstStream = destEntry.Open())
                                await new MemoryStream(modifiedManagedZip).CopyToAsync(dstStream);
                            continue;
                        }
                        if (entry.Length == 0 && entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                        var destEntry2 = dest.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Optimal);
                        await using (var srcStream = entry.Open())
                        await using (var dstStream = destEntry2.Open())
                            await srcStream.CopyToAsync(dstStream);
                    }
                }
                File.Move(tempPath, packagePath, overwrite: true);
                return (true, (string?)null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }
}
