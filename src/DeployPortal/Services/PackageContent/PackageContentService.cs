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
            return package.PackageType switch
            {
                "LCS" or "Merged" => await GetModelsFromLcsPackageAsync(package.StoredFilePath),
                "Unified" => await GetModelsFromUnifiedPackageAsync(package.StoredFilePath),
                _ => new List<ModelInfo>()
            };
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
            var licenseFiles = PackageAnalyzer.DetectLicenseFiles(package.StoredFilePath);
            return licenseFiles.Select(fn => new LicenseInfo
            {
                FileName = fn,
                SizeBytes = 0, // Will be filled when content is loaded
                ContentType = fn.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
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

    // ===== Private helpers =====

    private async Task<Package?> GetPackageAsync(int packageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Packages.FindAsync(packageId);
    }

    private async Task<List<ModelInfo>> GetModelsFromLcsPackageAsync(string zipPath)
    {
        return await Task.Run(() =>
        {
            var models = new List<ModelInfo>();
            using var zip = ZipFile.OpenRead(zipPath);

            // LCS packages have models as .nupkg files in AOSService/Packages/files/ or AOSService/Packages/
            var modelFiles = zip.Entries
                .Where(e => e.FullName.Contains("AOSService/Packages/", StringComparison.OrdinalIgnoreCase)
                    && (e.FullName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                        || e.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    && e.FullName.Contains("dynamicsax-", StringComparison.OrdinalIgnoreCase))
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

            // Unified packages have *_managed.zip files at root level
            var managedZips = zip.Entries
                .Where(e => e.FullName.EndsWith("_managed.zip", StringComparison.OrdinalIgnoreCase)
                    && !e.FullName.Contains("DefaultDevSolution", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var entry in managedZips)
            {
                try
                {
                    var modelName = PackageAnalyzer.ExtractModuleNameFromManagedZip(entry.Name);
                    var modelInfo = new ModelInfo
                    {
                        Name = modelName,
                        FileName = entry.Name,
                        SizeBytes = entry.Length,
                        Publisher = "Unknown" // Unified packages don't typically have publisher info accessible easily
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
