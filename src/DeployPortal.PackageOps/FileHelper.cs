namespace DeployPortal.PackageOps;

/// <summary>
/// Common file-system helpers used by conversion and merge engines.
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Recursively copies a directory, overwriting existing files.
    /// </summary>
    public static void CopyDirectoryRecursive(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
