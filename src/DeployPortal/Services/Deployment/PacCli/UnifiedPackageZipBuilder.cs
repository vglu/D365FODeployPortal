using System.IO.Compression;

namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Builds a temporary zip of a Unified package folder for PAC (DLL + PackageAssets together).
/// </summary>
public static class UnifiedPackageZipBuilder
{
    /// <summary>
    /// Creates a zip of <paramref name="packageDir"/> under %TEMP%. Caller must delete the file.
    /// </summary>
    public static string CreateZip(string packageDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDir);
        if (!Directory.Exists(packageDir))
            throw new DirectoryNotFoundException($"Unified package directory not found: {packageDir}");

        var zipPath = Path.Combine(Path.GetTempPath(), $"pac_unified_{Guid.NewGuid():N}.zip");
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        ZipFile.CreateFromDirectory(packageDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        return zipPath;
    }
}
