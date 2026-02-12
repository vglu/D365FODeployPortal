using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace DeployPortal.PackageOps;

/// <summary>
/// Pure merge engine for LCS and Unified packages.
/// No DI, no database -- just file I/O.
/// </summary>
public class MergeEngine
{
    private readonly string _tempDir;

    public MergeEngine(string tempDir)
    {
        _tempDir = tempDir;
    }

    public string MergeLcs(List<string> packagePaths, Action<string>? onLog = null)
    {
        var workDir = Path.Combine(_tempDir, $"merge_lcs_{Guid.NewGuid():N}");
        var commonDir = Path.Combine(workDir, "CommonPackage");
        var tmpDir = Path.Combine(workDir, "TempPackage");
        Directory.CreateDirectory(commonDir);
        Directory.CreateDirectory(tmpDir);

        onLog?.Invoke("[LCS Merge] Extracting base package...");
        FileHelper.ExtractZipToDirectory(packagePaths[0], commonDir);
        var commonRoot = FileHelper.ResolveExtractRoot(commonDir);

        for (int i = 1; i < packagePaths.Count; i++)
        {
            onLog?.Invoke($"[LCS Merge] Merging package {i + 1}/{packagePaths.Count}...");
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            Directory.CreateDirectory(tmpDir);
            FileHelper.ExtractZipToDirectory(packagePaths[i], tmpDir);
            var tmpRoot = FileHelper.ResolveExtractRoot(tmpDir);

            // On Linux, zip entries with backslash (Windows) create dirs like "AOSService\Packages"; find by logical name
            var sourceAOS = FileHelper.FindChildDirectory(tmpRoot, "AOSService") ?? Path.Combine(tmpRoot, "AOSService");
            var targetAOS = FileHelper.FindChildDirectory(commonRoot, "AOSService") ?? Path.Combine(commonRoot, "AOSService");
            if (Directory.Exists(sourceAOS))
            {
                onLog?.Invoke("  Merging AOSService content");
                FileHelper.CopyDirectoryRecursive(sourceAOS, targetAOS);
            }

            var xmlPath1 = Path.Combine(commonRoot, "HotfixInstallationInfo.xml");
            var xmlPath2 = Path.Combine(tmpRoot, "HotfixInstallationInfo.xml");
            if (File.Exists(xmlPath1) && File.Exists(xmlPath2))
            {
                onLog?.Invoke("  Merging HotfixInstallationInfo.xml");
                MergeHotfixXml(xmlPath1, xmlPath2);
            }
        }
        return commonDir;
    }

    public string MergeUnified(List<string> packagePaths, Action<string>? onLog = null)
    {
        var workDir = Path.Combine(_tempDir, $"merge_ude_{Guid.NewGuid():N}");
        var targetDir = Path.Combine(workDir, "MergedPackage");
        Directory.CreateDirectory(targetDir);
        var allLicenses = new List<(string FileName, byte[] Content)>();

        for (int i = 0; i < packagePaths.Count; i++)
        {
            onLog?.Invoke($"[UDE Merge] Extracting package {i + 1}/{packagePaths.Count}...");
            var tempExtract = Path.Combine(workDir, $"pkg_{i}");
            Directory.CreateDirectory(tempExtract);
            FileHelper.ExtractZipToDirectory(packagePaths[i], tempExtract);

            var pkgAssetsDir = Path.Combine(tempExtract, "PackageAssets");
            if (Directory.Exists(pkgAssetsDir))
            {
                var pkgLicenses = CollectLicensesFromAssets(pkgAssetsDir);
                if (pkgLicenses.Count > 0)
                {
                    onLog?.Invoke($"  Found {pkgLicenses.Count} license file(s)");
                    foreach (var lic in pkgLicenses)
                    {
                        if (!allLicenses.Any(existing =>
                            existing.FileName.Equals(lic.FileName, StringComparison.OrdinalIgnoreCase)))
                        {
                            allLicenses.Add(lic);
                        }
                    }
                }
            }

            onLog?.Invoke("  Copying contents to merged output");
            FileHelper.CopyDirectoryRecursive(tempExtract, targetDir);
        }

        var assetsDir = Path.Combine(targetDir, "PackageAssets");
        if (allLicenses.Count > 0 && Directory.Exists(assetsDir))
        {
            onLog?.Invoke($"[UDE Merge] Consolidating {allLicenses.Count} license file(s)...");
            InjectConsolidatedLicenses(assetsDir, allLicenses, onLog);
            onLog?.Invoke("[UDE Merge] Licenses consolidated successfully");
        }

        onLog?.Invoke("[UDE Merge] Scanning for ImportConfig.xml files...");
        foreach (var xmlFile in Directory.GetFiles(targetDir, "ImportConfig.xml", SearchOption.AllDirectories))
        {
            var folder = Path.GetDirectoryName(xmlFile)!;
            var added = UpdateImportConfigXml(xmlFile, folder, onLog);
            if (added > 0)
                onLog?.Invoke($"  Updated ImportConfig.xml in {Path.GetFileName(folder)} (+{added} entries)");
        }
        return targetDir;
    }

    // ---- ImportConfig.xml helpers ----

    internal static int UpdateImportConfigXml(string xmlPath, string folder, Action<string>? onLog = null)
    {
        if (!File.Exists(xmlPath)) return 0;
        try
        {
            var doc = XDocument.Load(xmlPath);
            if (doc.Root == null) return 0;
            var cds = doc.Root.Element("configdatastorage") ?? doc.Root;
            var ep = cds.Element("externalpackages");
            if (ep == null) { ep = new XElement("externalpackages"); cds.Add(ep); }

            var existingSet = ep.Elements("package")
                .Select(e => e.Attribute("filename")?.Value)
                .Where(f => f != null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var managedZips = Directory.GetFiles(folder, "*_managed.zip")
                .Select(Path.GetFileName)
                .Where(f => f != null
                    && !f!.Equals("DefaultDevSolution_managed.zip", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int added = 0;
            foreach (var zipName in managedZips)
            {
                if (zipName != null && !existingSet.Contains(zipName))
                {
                    ep.Add(new XElement("package",
                        new XAttribute("type", "xpp"),
                        new XAttribute("filename", zipName)));
                    added++;
                }
            }
            if (added > 0) doc.Save(xmlPath);
            return added;
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"  Warning: {ex.Message}");
            return 0;
        }
    }

    // ---- License consolidation ----

    public static List<(string FileName, byte[] Content)> CollectLicensesFromAssets(string assetsDir)
    {
        var result = new List<(string FileName, byte[] Content)>();
        var managedZipPaths = Directory.GetFiles(assetsDir, "*_managed.zip")
            .Where(f => !Path.GetFileName(f)
                .Equals("DefaultDevSolution_managed.zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var zipPath in managedZipPaths)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var licEntries = zip.Entries
                    .Where(e => e.FullName.Contains("/_License_") || e.FullName.Contains("\\_License_"))
                    .ToList();

                foreach (var entry in licEntries)
                {
                    var licFileName = entry.FullName.Split('/', '\\').Last();
                    if (!result.Any(l => l.FileName.Equals(licFileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        using var ms = new MemoryStream();
                        using var stream = entry.Open();
                        stream.CopyTo(ms);
                        result.Add((licFileName, ms.ToArray()));
                    }
                }
            }
            catch (InvalidDataException)
            {
                // Not a valid ZIP (e.g., placeholder content in tests) — skip
            }
        }
        return result;
    }

    public static void InjectConsolidatedLicenses(
        string assetsDir, List<(string FileName, byte[] Content)> allLicenses, Action<string>? onLog = null)
    {
        var managedZips = Directory.GetFiles(assetsDir, "*_managed.zip")
            .Where(f => !Path.GetFileName(f)
                .Equals("DefaultDevSolution_managed.zip", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (managedZips.Count == 0) return;

        foreach (var zipPath in managedZips)
        {
            try { RemoveLicensesFromManagedZip(zipPath); }
            catch (InvalidDataException) { /* not a valid ZIP — skip */ }
        }

        // Find the first valid ZIP to inject licenses into
        string? firstZipPath = null;
        foreach (var zipPath in managedZips)
        {
            try
            {
                using var test = ZipFile.OpenRead(zipPath);
                firstZipPath = zipPath;
                break;
            }
            catch (InvalidDataException) { }
        }

        if (firstZipPath == null) return;

        var moduleName = PackageAnalyzer.ExtractModuleNameFromManagedZip(Path.GetFileName(firstZipPath));
        var licenseGuid = Guid.NewGuid().ToString();
        AddLicensesToManagedZip(firstZipPath, moduleName, licenseGuid, allLicenses);
        onLog?.Invoke($"  Placed {allLicenses.Count} license(s) into {Path.GetFileName(firstZipPath)}");
    }

    private static void RemoveLicensesFromManagedZip(string zipPath)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);

        var licEntries = zip.Entries
            .Where(e => e.FullName.Contains("/_License_") || e.FullName.Contains("\\_License_"))
            .ToList();
        foreach (var entry in licEntries) entry.Delete();

        var defEntry = zip.Entries.FirstOrDefault(e => e.FullName == "fnomoduledefinition.json");
        if (defEntry == null) return;

        string jsonText;
        using (var reader = new StreamReader(defEntry.Open())) jsonText = reader.ReadToEnd();

        try
        {
            var doc = JsonDocument.Parse(jsonText);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "License") writer.WriteNull("License");
                    else prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            defEntry.Delete();
            var newEntry = zip.CreateEntry("fnomoduledefinition.json");
            using var entryStream = newEntry.Open();
            entryStream.Write(ms.ToArray());
        }
        catch { /* JSON parse failure - leave as-is */ }
    }

    private static void AddLicensesToManagedZip(
        string zipPath, string moduleName, string licenseGuid,
        List<(string FileName, byte[] Content)> licenseFiles)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var licenseFolderPath = $"{moduleName}\\_License_{licenseGuid}";

        foreach (var (fileName, content) in licenseFiles)
        {
            var entryPath = $"{moduleName}/_License_{licenseGuid}/{fileName}";
            var entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(content);
        }

        var defEntry = zip.Entries.FirstOrDefault(e => e.FullName == "fnomoduledefinition.json");
        if (defEntry == null) return;

        string jsonText;
        using (var reader = new StreamReader(defEntry.Open())) jsonText = reader.ReadToEnd();

        try
        {
            var doc = JsonDocument.Parse(jsonText);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "License")
                    {
                        writer.WritePropertyName("License");
                        writer.WriteStartObject();
                        writer.WriteString("LicenseFolder", licenseFolderPath);
                        writer.WriteEndObject();
                    }
                    else prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            defEntry.Delete();
            var newEntry = zip.CreateEntry("fnomoduledefinition.json");
            using var entryStream = newEntry.Open();
            entryStream.Write(ms.ToArray());
        }
        catch { /* JSON parse failure - leave as-is */ }
    }

    // ---- HotfixInstallationInfo.xml merge ----

    internal static void MergeHotfixXml(string basePath, string addPath)
    {
        var doc1 = XDocument.Load(basePath);
        var doc2 = XDocument.Load(addPath);
        if (doc1.Root == null || doc2.Root == null) return;

        var moduleList1 = doc1.Root.Element("MetadataModuleList");
        var moduleList2 = doc2.Root.Element("MetadataModuleList");
        if (moduleList1 != null && moduleList2 != null)
            foreach (var mod in moduleList2.Elements("string"))
                moduleList1.Add(new XElement("string", mod.Value));

        var compList1 = doc1.Root.Element("AllComponentList");
        var compList2 = doc2.Root.Element("AllComponentList");
        if (compList1 != null && compList2 != null)
            foreach (var comp in compList2.Elements("ArrayOfString"))
                compList1.Add(comp);

        doc1.Save(basePath);
    }
}
