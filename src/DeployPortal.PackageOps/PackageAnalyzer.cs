using System.IO.Compression;

namespace DeployPortal.PackageOps;

/// <summary>
/// Static helpers for package type detection and module name extraction.
/// No dependencies — pure logic.
/// </summary>
public static class PackageAnalyzer
{
    /// <summary>
    /// Detects whether a ZIP file is an LCS package, Unified package, or other.
    /// </summary>
    public static string DetectPackageType(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entryNames = zip.Entries.Select(e => e.FullName).ToList();

            if (entryNames.Any(n => n.EndsWith("TemplatePackage.dll", StringComparison.OrdinalIgnoreCase)))
                return "Unified";

            if (entryNames.Any(n => n.StartsWith("AOSService/", StringComparison.OrdinalIgnoreCase)))
                return "LCS";

            if (entryNames.Any(n => n.Equals("HotfixInstallationInfo.xml", StringComparison.OrdinalIgnoreCase)))
                return "LCS";

            if (entryNames.Any(n => n.StartsWith("Metadata/", StringComparison.OrdinalIgnoreCase)))
                return "LCS";

            return "Other";
        }
        catch
        {
            return "Other";
        }
    }

    /// <summary>
    /// Determines the merge strategy based on package type strings.
    /// Returns "LCS", "Unified", or null if incompatible.
    /// </summary>
    public static string? DetectMergeStrategy(IEnumerable<string> packageTypes)
    {
        var types = packageTypes.Distinct().ToList();

        if (types.All(t => t == "LCS" || t == "Merged"))
            return "LCS";

        if (types.All(t => t == "Unified"))
            return "Unified";

        if (types.All(t => t == "LCS" || t == "Merged" || t == "Other"))
            return "LCS";

        return null;
    }

    /// <summary>
    /// Extracts module name from a NuGet-style filename.
    /// "dynamicsax-sisheavyhighway.1.0.0.0" → "sisheavyhighway"
    /// "dynamicsax-sisproject360.2021.4.1.1" → "sisproject360"
    /// </summary>
    public static string ExtractModuleName(string fileName)
    {
        var name = fileName;

        if (name.StartsWith("dynamicsax-", StringComparison.OrdinalIgnoreCase))
            name = name["dynamicsax-".Length..];

        for (int i = 0; i < name.Length - 1; i++)
        {
            if (name[i] == '.' && char.IsDigit(name[i + 1]))
            {
                name = name[..i];
                break;
            }
        }

        return name.ToLowerInvariant();
    }

    /// <summary>
    /// Extracts module name from a .nupkg or similar filename (e.g. Dynamics.AX.ApplicationSuite.1.0.0.0.nupkg).
    /// Removes extension, strips trailing version segments (digits/dots), then takes last segment after dot or full name.
    /// </summary>
    public static string ExtractModuleNameFromNupkg(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(name)) return "unknown";

        // Strip trailing version segments (e.g. .2026.1.14.2 or .1.0.0.0)
        while (true)
        {
            var lastDot = name.LastIndexOf('.');
            if (lastDot < 0) break;
            var after = name[(lastDot + 1)..];
            var allDigits = after.Length > 0;
            for (int j = 0; j < after.Length && allDigits; j++)
                allDigits = char.IsDigit(after[j]);
            if (allDigits)
                name = name[..lastDot];
            else
                break;
        }

        var lastSegmentDot = name.LastIndexOf('.');
        if (lastSegmentDot >= 0 && lastSegmentDot < name.Length - 1)
            name = name[(lastSegmentDot + 1)..];

        if (name.StartsWith("dynamicsax-", StringComparison.OrdinalIgnoreCase))
            return ExtractModuleName(name);

        return string.IsNullOrEmpty(name) ? "unknown" : name.ToLowerInvariant();
    }

    /// <summary>
    /// Detects license file names inside a package (LCS or Unified).
    /// Returns a list of license file names (e.g. ["license1.txt", "license2.xml"]).
    /// </summary>
    public static List<string> DetectLicenseFiles(string zipPath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entryNames = zip.Entries.Select(e => e.FullName).ToList();

            // LCS: AOSService/Scripts/License/*.txt|*.xml
            foreach (var e in entryNames)
            {
                if (e.StartsWith("AOSService/Scripts/License/", StringComparison.OrdinalIgnoreCase)
                    || e.StartsWith("AOSService\\Scripts\\License\\", StringComparison.OrdinalIgnoreCase))
                {
                    var fn = e.Split('/', '\\').Last();
                    var ext = Path.GetExtension(fn).ToLowerInvariant();
                    if ((ext == ".txt" || ext == ".xml") && !string.IsNullOrEmpty(fn))
                        result.Add(fn);
                }
            }

            // Unified: look inside *_managed.zip for _License_ entries
            if (result.Count == 0)
            {
                var managedZips = zip.Entries
                    .Where(e => e.FullName.EndsWith("_managed.zip", StringComparison.OrdinalIgnoreCase)
                        && !e.FullName.Contains("DefaultDevSolution", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var mzEntry in managedZips)
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        using (var stream = mzEntry.Open())
                            stream.CopyTo(ms);
                        ms.Position = 0;

                        using var innerZip = new ZipArchive(ms, ZipArchiveMode.Read);
                        foreach (var ie in innerZip.Entries)
                        {
                            if (ie.FullName.Contains("/_License_") || ie.FullName.Contains("\\_License_"))
                            {
                                var fn = ie.FullName.Split('/', '\\').Last();
                                if (!string.IsNullOrEmpty(fn))
                                    result.Add(fn);
                            }
                        }
                    }
                    catch { /* bad inner zip, skip */ }
                }
            }
        }
        catch { /* invalid zip */ }

        return result.OrderBy(f => f).ToList();
    }

    /// <summary>
    /// Extracts license file contents from a package (LCS or Unified).
    /// Returns list of (FileName, Content) tuples.
    /// </summary>
    public static List<(string FileName, byte[] Content)> ExtractLicenseFileContents(string zipPath)
    {
        var result = new List<(string FileName, byte[] Content)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);

            // LCS: AOSService/Scripts/License/*
            foreach (var entry in zip.Entries)
            {
                if ((entry.FullName.StartsWith("AOSService/Scripts/License/", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.StartsWith("AOSService\\Scripts\\License\\", StringComparison.OrdinalIgnoreCase))
                    && entry.Length > 0)
                {
                    var fn = entry.FullName.Split('/', '\\').Last();
                    var ext = Path.GetExtension(fn).ToLowerInvariant();
                    if ((ext == ".txt" || ext == ".xml") && seen.Add(fn))
                    {
                        using var ms = new MemoryStream();
                        using var stream = entry.Open();
                        stream.CopyTo(ms);
                        result.Add((fn, ms.ToArray()));
                    }
                }
            }

            // Unified: look inside *_managed.zip
            if (result.Count == 0)
            {
                var managedZips = zip.Entries
                    .Where(e => e.FullName.EndsWith("_managed.zip", StringComparison.OrdinalIgnoreCase)
                        && !e.FullName.Contains("DefaultDevSolution", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var mzEntry in managedZips)
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        using (var stream = mzEntry.Open())
                            stream.CopyTo(ms);
                        ms.Position = 0;

                        using var innerZip = new ZipArchive(ms, ZipArchiveMode.Read);
                        foreach (var ie in innerZip.Entries)
                        {
                            if ((ie.FullName.Contains("/_License_") || ie.FullName.Contains("\\_License_"))
                                && ie.Length > 0)
                            {
                                var fn = ie.FullName.Split('/', '\\').Last();
                                if (!string.IsNullOrEmpty(fn) && seen.Add(fn))
                                {
                                    using var ims = new MemoryStream();
                                    using var istream = ie.Open();
                                    istream.CopyTo(ims);
                                    result.Add((fn, ims.ToArray()));
                                }
                            }
                        }
                    }
                    catch { /* bad inner zip */ }
                }
            }
        }
        catch { /* invalid zip */ }

        return result;
    }

    /// <summary>
    /// Extracts version from a model filename (e.g. dynamicsax-sisheavyhighway.2026.1.9.3.nupkg -> "2026.1.9.3").
    /// Returns empty string if no version segment found.
    /// </summary>
    public static string ExtractVersionFromModelFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(name)) return "";

        if (!name.StartsWith("dynamicsax-", StringComparison.OrdinalIgnoreCase))
            return "";

        var afterPrefix = name["dynamicsax-".Length..];
        var firstDot = afterPrefix.IndexOf('.');
        if (firstDot < 0) return "";

        var versionPart = afterPrefix[(firstDot + 1)..];
        return versionPart.All(c => c == '.' || char.IsDigit(c)) ? versionPart : "";
    }

    /// <summary>
    /// Extracts module name from managed zip filename.
    /// "cch_sureaddress_1_0_0_1_managed.zip" → "cch_sureaddress"
    /// </summary>
    public static string ExtractModuleNameFromManagedZip(string fileName)
    {
        var name = fileName;
        var suffix = "_managed.zip";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            name = name[..^suffix.Length];

        var versionPattern = "_1_0_0_1";
        if (name.EndsWith(versionPattern))
            name = name[..^versionPattern.Length];

        return name;
    }
}
