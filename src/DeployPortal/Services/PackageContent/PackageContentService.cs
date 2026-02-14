using System.IO.Compression;
using System.Xml.Linq;
using DeployPortal.Data;
using DeployPortal.Models;
using DeployPortal.PackageOps;
using Microsoft.EntityFrameworkCore;

namespace DeployPortal.Services.PackageContent;

/// <summary>
/// Implementation of IPackageContentService.
/// Reads D365FO models and licenses from package files.
/// </summary>
public class PackageContentService : IPackageContentService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<PackageContentService> _logger;

    public PackageContentService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<PackageContentService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<List<ModelInfo>> GetModelsAsync(int packageId)
    {
        var package = await GetPackageAsync(packageId);
        if (package == null || !File.Exists(package.StoredFilePath))
            return new List<ModelInfo>();

        try
        {
            var path = package.StoredFilePath;
            var list = package.PackageType switch
            {
                "LCS" or "Merged" => await GetModelsFromLcsPackageAsync(path),
                "Unified" => await GetModelsFromUnifiedPackageAsync(path),
                _ => new List<ModelInfo>()
            };

            // If type "Other" or list empty, try other format(s)
            if (list.Count == 0)
            {
                var tryUnified = package.PackageType is not "Unified";
                var tryLcs = package.PackageType is not "LCS" and not "Merged";
                if (tryUnified)
                    list = await GetModelsFromUnifiedPackageAsync(path);
                if (list.Count == 0 && tryLcs)
                    list = await GetModelsFromLcsPackageAsync(path);
            }

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read models from package {PackageId}", packageId);
            return new List<ModelInfo>();
        }
    }

    public async Task<List<LicenseInfo>> GetLicensesAsync(int packageId)
    {
        var package = await GetPackageAsync(packageId);
        if (package == null || !File.Exists(package.StoredFilePath))
            return new List<LicenseInfo>();

        try
        {
            // ExtractLicenseFileContents returns (FileName, Content) for both LCS and Unified (nested in *_managed.zip)
            var licenseContents = await Task.Run(() =>
                PackageAnalyzer.ExtractLicenseFileContents(package.StoredFilePath));
            return licenseContents.Select(l => new LicenseInfo
            {
                FileName = l.FileName,
                SizeBytes = l.Content.Length,
                ContentType = l.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    ? "application/xml"
                    : "text/plain"
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read licenses from package {PackageId}", packageId);
            return new List<LicenseInfo>();
        }
    }

    public async Task<ModelInfo?> GetModelDetailsAsync(int packageId, string modelName)
    {
        var models = await GetModelsAsync(packageId);
        return models.FirstOrDefault(m =>
            m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<LicenseInfo?> GetLicenseContentAsync(int packageId, string licenseFileName)
    {
        var package = await GetPackageAsync(packageId);
        if (package == null || !File.Exists(package.StoredFilePath))
            return null;

        try
        {
            var licenseContents = PackageAnalyzer.ExtractLicenseFileContents(package.StoredFilePath);
            var licenseData = licenseContents.FirstOrDefault(l =>
                l.FileName.Equals(licenseFileName, StringComparison.OrdinalIgnoreCase));

            if (licenseData == default)
                return null;

            return new LicenseInfo
            {
                FileName = licenseData.FileName,
                SizeBytes = licenseData.Content.Length,
                Content = licenseData.Content,
                ContentType = licenseFileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    ? "application/xml"
                    : "text/plain"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read license {FileName} from package {PackageId}",
                licenseFileName, packageId);
            return null;
        }
    }

    public async Task<byte[]?> GetModelFileContentAsync(int packageId, string modelFileName)
    {
        var package = await GetPackageAsync(packageId);
        if (package == null || !File.Exists(package.StoredFilePath) || string.IsNullOrEmpty(modelFileName))
            return null;

        return await Task.Run(() =>
        {
            try
            {
                using var zip = ZipFile.OpenRead(package.StoredFilePath);
                var entry = zip.Entries.FirstOrDefault(e =>
                    string.Equals(GetEntryFileName(e.FullName), modelFileName, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    return (byte[]?)null;
                using var ms = new MemoryStream();
                using (var stream = entry.Open())
                    stream.CopyTo(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read model file {FileName} from package {PackageId}",
                    modelFileName, packageId);
                return null;
            }
        });
    }

    private static string GetEntryFileName(string fullName)
    {
        var path = NormalizeZipPath(fullName);
        return path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
    }

    // ===== Private helpers =====

    private async Task<Package?> GetPackageAsync(int packageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Packages.FindAsync(packageId);
    }

    private static string NormalizeZipPath(string fullName) =>
        fullName?.Replace('\\', '/') ?? "";

    private async Task<List<ModelInfo>> GetModelsFromLcsPackageAsync(string zipPath)
    {
        return await Task.Run(() =>
        {
            var models = new List<ModelInfo>();
            using var zip = ZipFile.OpenRead(zipPath);

            // LCS packages have models as .nupkg/.zip in AOSService/Packages/files/ or AOSService/Packages/
            // Normalize path: ZIP can use either / or \ depending on how it was created
            var modelFiles = zip.Entries
                .Where(e =>
                {
                    var path = NormalizeZipPath(e.FullName);
                    var fileName = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
                    return path.Contains("AOSService/Packages/", StringComparison.OrdinalIgnoreCase)
                        && (fileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                            || fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        && (path.Contains("dynamicsax-", StringComparison.OrdinalIgnoreCase)
                            || fileName.Contains("dynamicsax-", StringComparison.OrdinalIgnoreCase));
                })
                .ToList();

            foreach (var entry in modelFiles)
            {
                try
                {
                    var modelName = PackageAnalyzer.ExtractModuleNameFromNupkg(entry.Name);
                    var modelInfo = new ModelInfo
                    {
                        Name = modelName,
                        FileName = entry.Name,
                        SizeBytes = entry.Length,
                        Version = ExtractVersionFromFileName(entry.Name)
                    };

                    // Try to extract dependencies from .nuspec if it's a .nupkg
                    if (entry.Name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    {
                        modelInfo.Dependencies = ExtractDependenciesFromNupkg(entry);
                    }

                    models.Add(modelInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse model from entry {EntryName}", entry.FullName);
                }
            }

            return models.OrderBy(m => m.Name).ToList();
        });
    }

    private async Task<List<ModelInfo>> GetModelsFromUnifiedPackageAsync(string zipPath)
    {
        return await Task.Run(() =>
        {
            var models = new List<ModelInfo>();
            using var zip = ZipFile.OpenRead(zipPath);

            // Unified packages have *_managed.zip (at root or in subfolders); exclude DefaultDevSolution
            var managedZips = zip.Entries
                .Where(e =>
                {
                    var path = NormalizeZipPath(e.FullName);
                    var fileName = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
                    return fileName.EndsWith("_managed.zip", StringComparison.OrdinalIgnoreCase)
                        && !path.Contains("DefaultDevSolution", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            foreach (var entry in managedZips)
            {
                try
                {
                    var modelName = PackageAnalyzer.ExtractModuleNameFromManagedZip(entry.Name);
                    var containsLicenses = false;
                    if (entry.Length > 0)
                    {
                        try
                        {
                            using var ms = new MemoryStream();
                            using (var stream = entry.Open())
                                stream.CopyTo(ms);
                            ms.Position = 0;
                            using var innerZip = new ZipArchive(ms, ZipArchiveMode.Read);
                            containsLicenses = innerZip.Entries.Any(e =>
                                e.FullName.Contains("/_License_", StringComparison.Ordinal)
                                || e.FullName.Contains("\\_License_", StringComparison.Ordinal));
                        }
                        catch { /* skip if inner zip unreadable */ }
                    }
                    var modelInfo = new ModelInfo
                    {
                        Name = modelName,
                        FileName = entry.Name,
                        SizeBytes = entry.Length,
                        Publisher = "Unknown",
                        ContainsLicenses = containsLicenses
                    };

                    models.Add(modelInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse model from entry {EntryName}", entry.FullName);
                }
            }

            return models.OrderBy(m => m.Name).ToList();
        });
    }

    private string? ExtractVersionFromFileName(string fileName)
    {
        // Extract version from filenames like "dynamicsax-modulename.1.0.0.0.nupkg"
        var parts = fileName.Replace(".nupkg", "").Replace(".zip", "").Split('.');
        if (parts.Length >= 4)
        {
            // Try to find version pattern (4 numeric segments)
            for (int i = 0; i < parts.Length - 3; i++)
            {
                if (int.TryParse(parts[i], out _) &&
                    int.TryParse(parts[i + 1], out _) &&
                    int.TryParse(parts[i + 2], out _) &&
                    int.TryParse(parts[i + 3], out _))
                {
                    return $"{parts[i]}.{parts[i + 1]}.{parts[i + 2]}.{parts[i + 3]}";
                }
            }
        }
        return null;
    }

    private List<string> ExtractDependenciesFromNupkg(ZipArchiveEntry nupkgEntry)
    {
        var dependencies = new List<string>();
        try
        {
            using var ms = new MemoryStream();
            using (var stream = nupkgEntry.Open())
                stream.CopyTo(ms);
            ms.Position = 0;

            using var nupkgZip = new ZipArchive(ms, ZipArchiveMode.Read);
            var nuspecEntry = nupkgZip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

            if (nuspecEntry != null)
            {
                using var nuspecStream = nuspecEntry.Open();
                var xdoc = XDocument.Load(nuspecStream);
                var ns = xdoc.Root?.Name.Namespace ?? XNamespace.None;

                var deps = xdoc.Descendants(ns + "dependency")
                    .Select(d => d.Attribute("id")?.Value)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => PackageAnalyzer.ExtractModuleName(id!))
                    .ToList();

                dependencies.AddRange(deps);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract dependencies from .nupkg {FileName}", nupkgEntry.Name);
        }

        return dependencies;
    }
}
