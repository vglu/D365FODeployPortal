using System.IO.Compression;

namespace DeployPortal.PackageOps;

/// <summary>
/// Common file-system helpers used by conversion and merge engines.
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Extracts a zip to the destination directory, normalizing entry names (backslash to forward slash).
    /// On Linux, ZipFile.ExtractToDirectory does not create subdirs when entries use backslash; this fixes that.
    /// </summary>
    public static void ExtractZipToDirectory(string zipPath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var sep = Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
                continue;
            var normalizedFullName = entry.FullName.Replace('\\', sep);
            var destPath = Path.Combine(destinationDirectory, normalizedFullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destPath.TrimEnd(sep));
                continue;
            }
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    /// <summary>
    /// If extract dir has exactly one child directory and no files, returns that directory (nested LCS root).
    /// Otherwise returns the given dir.
    /// </summary>
    public static string ResolveExtractRoot(string extractDir)
    {
        var entries = Directory.GetFileSystemEntries(extractDir);
        var dirs = entries.Where(e => Directory.Exists(e)).ToList();
        var files = entries.Where(e => File.Exists(e)).ToList();
        if (dirs.Count == 1 && files.Count == 0)
            return dirs[0];
        return extractDir;
    }

    /// <summary>
    /// Finds a direct child directory of <paramref name="parentDir"/> whose name equals <paramref name="logicalName"/>
    /// or starts with <paramref name="logicalName"/> followed by path separator (handles zip entries with backslash on Linux).
    /// Returns null if not found.
    /// </summary>
    public static string? FindChildDirectory(string parentDir, string logicalName)
    {
        if (!Directory.Exists(parentDir)) return null;
        foreach (var path in Directory.GetDirectories(parentDir))
        {
            var name = Path.GetFileName(path);
            if (name.Equals(logicalName, StringComparison.OrdinalIgnoreCase))
                return path;
            if (name.StartsWith(logicalName + "\\", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(logicalName + "/", StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }

    /// <summary>
    /// Recursively copies a directory, overwriting existing files.
    /// </summary>
    public static void CopyDirectoryRecursive(string source, string target)
    {
        CopyDirectoryRecursive(source, target, _ => false);
    }

    /// <summary>
    /// Recursively copies a directory, overwriting existing files. Skips files for which skipFile returns true.
    /// </summary>
    public static void CopyDirectoryRecursive(string source, string target, Func<string, bool> skipFile)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
        {
            var fileName = Path.GetFileName(file);
            if (skipFile(fileName)) continue;
            File.Copy(file, Path.Combine(target, fileName), true);
        }

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(target, Path.GetFileName(dir)), skipFile);
    }
}
