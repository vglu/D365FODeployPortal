using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace DeployPortal.PackageOps;

/// <summary>
/// Pure conversion engine: LCS ↔ Unified package conversion.
/// No DI, no database, no web dependencies — just file I/O.
/// Used by both local (in-process) and remote (Azure Functions) execution.
/// </summary>
public class ConvertEngine
{
    private readonly string _tempDir;
    private readonly string _templateDir;

    /// <summary>
    /// Creates a new ConvertEngine.
    /// </summary>
    /// <param name="tempDir">Temporary working directory for extraction/conversion.</param>
    /// <param name="templateDir">Path to the UnifiedTemplate resources directory.</param>
    public ConvertEngine(string tempDir, string templateDir)
    {
        _tempDir = tempDir;
        _templateDir = templateDir;
    }

    // ═══════════════════════════════════════════════════════════
    //  LCS → Unified
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Converts an LCS package ZIP to Unified format.
    /// Returns the path to the output directory containing TemplatePackage.dll.
    /// </summary>
    public async Task<string> ConvertToUnifiedAsync(string lcsPackagePath, Action<string>? onLog = null)
    {
        var outputDir = Path.Combine(
            Path.GetDirectoryName(lcsPackagePath)!,
            Path.GetFileNameWithoutExtension(lcsPackagePath) + "_unified");

        Directory.CreateDirectory(outputDir);
        var assetsDir = Path.Combine(outputDir, "PackageAssets");
        Directory.CreateDirectory(assetsDir);

        onLog?.Invoke("[Built-in Converter] Converting LCS → Unified...");
        onLog?.Invoke($"  Input:  {lcsPackagePath}");
        onLog?.Invoke($"  Output: {outputDir}");

        var tempDir = Path.Combine(_tempDir, $"convert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            onLog?.Invoke("[Built-in] Extracting LCS package...");
            ZipFile.ExtractToDirectory(lcsPackagePath, tempDir);

            // Read HotfixInstallationInfo.xml
            var hotfixXmlPath = Path.Combine(tempDir, "HotfixInstallationInfo.xml");
            var platformVersion = "7.0.0.0";

            if (File.Exists(hotfixXmlPath))
            {
                var doc = XDocument.Load(hotfixXmlPath);
                platformVersion = doc.Root?.Element("PlatformVersion")?.Value ?? platformVersion;
                var moduleCount = doc.Root?.Element("MetadataModuleList")?.Elements("string").Count() ?? 0;
                onLog?.Invoke($"[Built-in] Platform: {platformVersion}, Modules in manifest: {moduleCount}");
            }

            // Extract ISV license files
            var licenseFiles = ExtractLicenseFiles(tempDir);
            if (licenseFiles.Count > 0)
                onLog?.Invoke($"[Built-in] Found {licenseFiles.Count} license file(s) in AOSService/Scripts/License/");

            // Copy static template files
            onLog?.Invoke("[Built-in] Writing template scaffold...");
            CopyTemplateFiles(outputDir, assetsDir);

            // Process each module: look in AOSService/Packages/files/ (dynamicsax-*.zip and *.nupkg),
            // then fallback to AOSService/Packages/ (same patterns) for AIO/alternative layouts
            var filesDir = Path.Combine(tempDir, "AOSService", "Packages", "files");
            var packagesDir = Path.Combine(tempDir, "AOSService", "Packages");
            var managedZipNames = new List<string>();
            var correlationId = Guid.NewGuid().ToString();
            var timestamp = DateTime.UtcNow.ToString("M/d/yyyy h:mm:ss tt");
            var licenseGuid = Guid.NewGuid().ToString();

            var moduleArchives = new List<(string FilePath, string ModuleName)>();

            if (Directory.Exists(filesDir))
            {
                foreach (var z in Directory.GetFiles(filesDir, "dynamicsax-*.zip"))
                    moduleArchives.Add((z, PackageAnalyzer.ExtractModuleName(Path.GetFileNameWithoutExtension(z))));
                foreach (var n in Directory.GetFiles(filesDir, "*.nupkg"))
                    moduleArchives.Add((n, PackageAnalyzer.ExtractModuleNameFromNupkg(Path.GetFileName(n))));
            }

            if (moduleArchives.Count == 0 && Directory.Exists(packagesDir))
            {
                onLog?.Invoke("[Built-in] AOSService/Packages/files/ empty or missing, trying AOSService/Packages/...");
                foreach (var z in Directory.GetFiles(packagesDir, "dynamicsax-*.zip"))
                    moduleArchives.Add((z, PackageAnalyzer.ExtractModuleName(Path.GetFileNameWithoutExtension(z))));
                foreach (var n in Directory.GetFiles(packagesDir, "*.nupkg"))
                    moduleArchives.Add((n, PackageAnalyzer.ExtractModuleNameFromNupkg(Path.GetFileName(n))));
            }

            moduleArchives = moduleArchives
                .OrderBy(m => m.ModuleName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (moduleArchives.Count > 0)
            {
                onLog?.Invoke($"[Built-in] Found {moduleArchives.Count} module archive(s) to convert");

                for (int i = 0; i < moduleArchives.Count; i++)
                {
                    var (moduleZipPath, moduleName) = moduleArchives[i];

                    onLog?.Invoke($"[Built-in] [{i + 1}/{moduleArchives.Count}] {moduleName}");

                    var managedZipName = $"{moduleName}_1_0_0_1_managed.zip";
                    var managedZipPath = Path.Combine(assetsDir, managedZipName);

                    var moduleLicenseFiles = (i == 0 && licenseFiles.Count > 0) ? licenseFiles : null;
                    var moduleLicenseGuid = (i == 0 && licenseFiles.Count > 0) ? licenseGuid : null;

                    await CreateManagedZipAsync(
                        moduleZipPath, managedZipPath, moduleName,
                        platformVersion, correlationId, timestamp,
                        moduleLicenseFiles, moduleLicenseGuid);

                    managedZipNames.Add(managedZipName);
                }

                if (licenseFiles.Count > 0)
                    onLog?.Invoke($"[Built-in] Added {licenseFiles.Count} license(s) to {moduleArchives[0].ModuleName}");
            }
            else
            {
                onLog?.Invoke("[Built-in] Warning: No dynamicsax-*.zip or *.nupkg found in AOSService/Packages/files/ or AOSService/Packages/");
            }

            // Generate ImportConfig.xml
            onLog?.Invoke($"[Built-in] Generating ImportConfig.xml ({managedZipNames.Count} packages)...");
            GenerateImportConfig(assetsDir, managedZipNames);

            // Verify
            var templateDll = Path.Combine(outputDir, "TemplatePackage.dll");
            if (!File.Exists(templateDll))
                throw new FileNotFoundException(
                    "TemplatePackage.dll not found — ensure Resources/UnifiedTemplate/ exists", templateDll);

            onLog?.Invoke($"[Built-in] Conversion completed successfully: {managedZipNames.Count} modules");
            return outputDir;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Unified → LCS
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Converts a Unified package back to LCS format.
    /// Returns the path to the output directory.
    /// </summary>
    public async Task<string> ConvertToLcsAsync(string unifiedPackagePath, Action<string>? onLog = null)
    {
        var outputDir = Path.Combine(
            Path.GetDirectoryName(unifiedPackagePath)!,
            Path.GetFileNameWithoutExtension(unifiedPackagePath) + "_lcs");

        Directory.CreateDirectory(outputDir);

        onLog?.Invoke("[Unified→LCS] Converting Unified → LCS...");
        onLog?.Invoke($"  Input:  {unifiedPackagePath}");
        onLog?.Invoke($"  Output: {outputDir}");

        var tempDir = Path.Combine(_tempDir, $"rev_convert_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            onLog?.Invoke("[Unified→LCS] Extracting Unified package...");
            ZipFile.ExtractToDirectory(unifiedPackagePath, tempDir);

            var assetsDir = Path.Combine(tempDir, "PackageAssets");
            if (!Directory.Exists(assetsDir))
            {
                if (File.Exists(Path.Combine(tempDir, "ImportConfig.xml")))
                    assetsDir = tempDir;
                else
                    throw new DirectoryNotFoundException("PackageAssets directory not found in Unified package.");
            }

            var filesDir = Path.Combine(outputDir, "AOSService", "Packages", "files");
            Directory.CreateDirectory(filesDir);

            var managedZips = Directory.GetFiles(assetsDir, "*_managed.zip")
                .Where(f => !Path.GetFileName(f)
                    .Equals("DefaultDevSolution_managed.zip", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            onLog?.Invoke($"[Unified→LCS] Found {managedZips.Count} modules to convert back");

            var moduleEntries = new List<(string Name, string Version)>();
            var allLicenseFiles = new List<(string FileName, byte[] Content)>();
            string platformVersion = "7.0.0.0";

            for (int i = 0; i < managedZips.Count; i++)
            {
                var managedZipPath = managedZips[i];
                onLog?.Invoke($"[Unified→LCS] [{i + 1}/{managedZips.Count}] {Path.GetFileName(managedZipPath)}");

                using var managedZip = ZipFile.OpenRead(managedZipPath);

                var defEntry = managedZip.Entries.FirstOrDefault(e => e.FullName == "fnomoduledefinition.json");
                string moduleName = "unknown";
                string moduleVersion = "1.0.0.0";

                if (defEntry != null)
                {
                    using var reader = new StreamReader(defEntry.Open());
                    var jsonText = await reader.ReadToEndAsync();
                    var jsonDoc = JsonDocument.Parse(jsonText);
                    var root = jsonDoc.RootElement;

                    if (root.TryGetProperty("Module", out var moduleEl) &&
                        moduleEl.TryGetProperty("Name", out var nameEl))
                        moduleName = nameEl.GetString() ?? moduleName;

                    if (root.TryGetProperty("Versions", out var versionsEl) &&
                        versionsEl.TryGetProperty("Platform", out var platEl))
                        platformVersion = platEl.GetString() ?? platformVersion;

                    if (root.TryGetProperty("License", out var licEl) &&
                        licEl.ValueKind == JsonValueKind.Object)
                    {
                        var licEntries = managedZip.Entries
                            .Where(e => e.FullName.Contains("/_License_") || e.FullName.Contains("\\_License_"))
                            .ToList();

                        foreach (var le in licEntries)
                        {
                            var licFileName = le.FullName.Split('/', '\\').Last();
                            if (!allLicenseFiles.Any(l =>
                                l.FileName.Equals(licFileName, StringComparison.OrdinalIgnoreCase)))
                            {
                                using var ms = new MemoryStream();
                                using var stream = le.Open();
                                await stream.CopyToAsync(ms);
                                allLicenseFiles.Add((licFileName, ms.ToArray()));
                            }
                        }
                    }
                }

                moduleEntries.Add((moduleName, moduleVersion));

                var lcsModuleZipName = $"dynamicsax-{moduleName}.{moduleVersion}.zip";
                var lcsModuleZipPath = Path.Combine(filesDir, lcsModuleZipName);

                using (var outputZip = ZipFile.Open(lcsModuleZipPath, ZipArchiveMode.Create))
                {
                    var modulePrefix = $"{moduleName}/";
                    foreach (var entry in managedZip.Entries)
                    {
                        if (entry.FullName == "fnomoduledefinition.json") continue;
                        if (entry.FullName.Contains("/_License_") || entry.FullName.Contains("\\_License_")) continue;

                        string targetPath = entry.FullName.StartsWith(modulePrefix, StringComparison.OrdinalIgnoreCase)
                            ? entry.FullName[modulePrefix.Length..]
                            : entry.FullName;

                        if (string.IsNullOrEmpty(targetPath)) continue;

                        var newEntry = outputZip.CreateEntry(targetPath, CompressionLevel.Optimal);
                        if (entry.Length > 0)
                        {
                            using var sourceStream = entry.Open();
                            using var targetStream = newEntry.Open();
                            await sourceStream.CopyToAsync(targetStream);
                        }
                    }
                }
            }

            if (allLicenseFiles.Count > 0)
            {
                var licenseDir = Path.Combine(outputDir, "AOSService", "Scripts", "License");
                Directory.CreateDirectory(licenseDir);
                foreach (var (fileName, content) in allLicenseFiles)
                    await File.WriteAllBytesAsync(Path.Combine(licenseDir, fileName), content);
                onLog?.Invoke($"[Unified→LCS] Restored {allLicenseFiles.Count} license file(s)");
            }

            GenerateHotfixInstallationInfo(outputDir, moduleEntries, platformVersion);
            onLog?.Invoke("[Unified→LCS] Generated HotfixInstallationInfo.xml");
            onLog?.Invoke($"[Unified→LCS] Conversion completed: {moduleEntries.Count} modules");

            return outputDir;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════

    internal static List<(string FileName, string FullPath)> ExtractLicenseFiles(string extractedLcsDir)
    {
        var licenseDir = Path.Combine(extractedLcsDir, "AOSService", "Scripts", "License");
        var result = new List<(string, string)>();
        if (!Directory.Exists(licenseDir)) return result;

        foreach (var file in Directory.GetFiles(licenseDir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".txt" || ext == ".xml")
                result.Add((Path.GetFileName(file), file));
        }
        return result;
    }

    private static async Task CreateManagedZipAsync(
        string sourceZipPath, string outputZipPath, string moduleName,
        string platformVersion, string correlationId, string timestamp,
        List<(string FileName, string FullPath)>? licenseFiles = null,
        string? licenseGuid = null)
    {
        var tempModuleDir = Path.Combine(Path.GetTempPath(), $"module_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempModuleDir);

        try
        {
            ZipFile.ExtractToDirectory(sourceZipPath, tempModuleDir);
            using var outputZip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);

            string? licenseFolderPath = null;
            if (licenseFiles != null && licenseFiles.Count > 0 && licenseGuid != null)
                licenseFolderPath = $"{moduleName}\\_License_{licenseGuid}";

            // fnomoduledefinition.json
            var definition = new Dictionary<string, object?>
            {
                ["Versions"] = new { Platform = platformVersion, Application = "10.0.0.0", Compiler = platformVersion, PackageVersion = "1.0.0.0" },
                ["CorrelationID"] = correlationId,
                ["ClientID"] = Environment.MachineName,
                ["TimestampUtc"] = timestamp,
                ["BuildType"] = "Full",
                ["PackageType"] = "Release",
                ["OrganizationID"] = "00000000-0000-0000-0000-000000000000",
                ["DBSync"] = new { SyncKind = "Full", Arguments = "" },
                ["Module"] = new { Name = moduleName, properties = new[] { new { Item1 = "packagingSource", Item2 = "Pipeline" } } },
                ["AdditionalData"] = new { },
                ["License"] = licenseFolderPath != null ? new { LicenseFolder = licenseFolderPath } : (object?)null
            };

            var jsonEntry = outputZip.CreateEntry("fnomoduledefinition.json");
            await using (var stream = jsonEntry.Open())
                await JsonSerializer.SerializeAsync(stream, definition, new JsonSerializerOptions { WriteIndented = true });

            // Module files under prefix
            foreach (var file in Directory.GetFiles(tempModuleDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(tempModuleDir, file);
                var entryPath = $"{moduleName}/{relativePath}".Replace('\\', '/');
                var entry = outputZip.CreateEntry(entryPath, CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTime(file);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream);
            }

            // License files
            if (licenseFiles != null && licenseFiles.Count > 0 && licenseGuid != null)
            {
                foreach (var (fileName, fullPath) in licenseFiles)
                {
                    var entryPath = $"{moduleName}/_License_{licenseGuid}/{fileName}";
                    var entry = outputZip.CreateEntry(entryPath, CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(fullPath);
                    await using var entryStream = entry.Open();
                    await using var fileStream = File.OpenRead(fullPath);
                    await fileStream.CopyToAsync(entryStream);
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempModuleDir, true); } catch { }
        }
    }

    private static void GenerateImportConfig(string assetsDir, List<string> managedZipNames)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-16", null),
            new XElement("configdatastorage",
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
                new XAttribute("PerformDependancyChecks", "true"),
                new XAttribute("crmmigdataimportfile", ""),
                new XElement("solutions",
                    new XElement("configsolutionfile",
                        new XAttribute("solutionpackagefilename", "DefaultDevSolution_managed.zip"))),
                new XElement("externalpackages",
                    managedZipNames.Select(name =>
                        new XElement("package",
                            new XAttribute("type", "xpp"),
                            new XAttribute("filename", name))))));
        doc.Save(Path.Combine(assetsDir, "ImportConfig.xml"));
    }

    private void CopyTemplateFiles(string outputDir, string assetsDir)
    {
        if (!Directory.Exists(_templateDir))
            throw new DirectoryNotFoundException(
                $"Unified template directory not found: {_templateDir}. " +
                "Ensure Resources/UnifiedTemplate/ is present.");

        var dllSrc = Path.Combine(_templateDir, "TemplatePackage.dll");
        if (File.Exists(dllSrc))
            File.Copy(dllSrc, Path.Combine(outputDir, "TemplatePackage.dll"), true);

        foreach (var staticFile in new[] { "solution.xml", "customizations.xml", "manifest.ppkg.json", "DefaultDevSolution_managed.zip" })
        {
            var src = Path.Combine(_templateDir, staticFile);
            if (File.Exists(src)) File.Copy(src, Path.Combine(assetsDir, staticFile), true);
        }

        var ctSrc = Path.Combine(_templateDir, "Content_Types.xml");
        if (File.Exists(ctSrc))
            File.Copy(ctSrc, Path.Combine(assetsDir, "[Content_Types].xml"), true);

        var enUsDir = Path.Combine(_templateDir, "en-us");
        if (Directory.Exists(enUsDir))
            FileHelper.CopyDirectoryRecursive(enUsDir, Path.Combine(assetsDir, "en-us"));
    }

    internal static void GenerateHotfixInstallationInfo(
        string outputDir, List<(string Name, string Version)> modules, string platformVersion)
    {
        var xsi = XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");
        var xsd = XNamespace.Get("http://www.w3.org/2001/XMLSchema");

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("HotfixInstallationInfo",
                new XAttribute(XNamespace.Xmlns + "xsd", xsd),
                new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                new XElement("Name", Guid.NewGuid().ToString()),
                new XElement("Description", "Converted from Unified package by DeployPortal"),
                new XElement("InstallationDateTime", "0001-01-01T00:00:00"),
                new XElement("InstallationInfoFilePath"),
                new XElement("Version"),
                new XElement("Publisher", "Non-Microsoft"),
                new XElement("GeneratedFromAXMetadata", "false"),
                new XElement("Type", "ApplicationPackage"),
                new XElement("IncludedDeployablePackages"),
                new XElement("ServiceModelList"),
                new XElement("OtherComponentList"),
                new XElement("MetadataModuleRelease"),
                new XElement("MetadataModuleList",
                    modules.Select(m => new XElement("string", $"{m.Name}.{m.Version}"))),
                new XElement("MetadataModelList"),
                new XElement("PlatformReleaseDisplayName"),
                new XElement("PlatformVersion", platformVersion),
                new XElement("IsCompatibleWithSealedRelease", "true"),
                new XElement("IsCompatibleWithApp81PlusRelease", "true"),
                new XElement("AllComponentList",
                    modules.Select(m =>
                        new XElement("ArrayOfString",
                            new XElement("string", "AX Module"),
                            new XElement("string", $"{m.Name}.{m.Version}"))))));

        doc.Save(Path.Combine(outputDir, "HotfixInstallationInfo.xml"));
    }
}
