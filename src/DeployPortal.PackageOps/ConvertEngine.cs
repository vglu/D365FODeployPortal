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
    private readonly string? _lcsTemplatePath;

    /// <summary>
    /// Creates a new ConvertEngine.
    /// </summary>
    /// <param name="tempDir">Temporary working directory for extraction/conversion.</param>
    /// <param name="templateDir">Path to the UnifiedTemplate resources directory.</param>
    /// <param name="lcsTemplatePath">Optional path to LCS template (folder or .zip). When set, Unified→LCS uses it as skeleton so the result has the same structure as a full LCS package (exe, DLLs, Scripts, etc.).</param>
    public ConvertEngine(string tempDir, string templateDir, string? lcsTemplatePath = null)
    {
        _tempDir = tempDir;
        _templateDir = templateDir;
        _lcsTemplatePath = string.IsNullOrWhiteSpace(lcsTemplatePath) ? null : lcsTemplatePath.Trim();
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

        // Remove existing output so a retry or second deploy of the same package does not hit "file already exists"
        if (Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, true); } catch { /* ignore */ }
        }

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

            // Many LCS ZIPs have a single root folder (e.g. MyPackage_1.0/AOSService/...).
            // Resolve the effective root so we find HotfixInstallationInfo.xml and AOSService in both flat and nested layouts.
            var extractRoot = ResolveExtractRoot(tempDir);
            string? lcsRootFolderName = null;
            if (extractRoot != tempDir)
            {
                lcsRootFolderName = Path.GetFileName(extractRoot);
                onLog?.Invoke($"[Built-in] Using nested extract root: {lcsRootFolderName}");
            }

            // Read HotfixInstallationInfo.xml
            var hotfixXmlPath = Path.Combine(extractRoot, "HotfixInstallationInfo.xml");
            var platformVersion = "7.0.0.0";

            if (File.Exists(hotfixXmlPath))
            {
                var doc = XDocument.Load(hotfixXmlPath);
                platformVersion = doc.Root?.Element("PlatformVersion")?.Value ?? platformVersion;
                var moduleCount = doc.Root?.Element("MetadataModuleList")?.Elements("string").Count() ?? 0;
                onLog?.Invoke($"[Built-in] Platform: {platformVersion}, Modules in manifest: {moduleCount}");
            }

            // Extract ISV license files (AOSService resolved case-insensitively for Linux Docker)
            var licenseFiles = ExtractLicenseFiles(extractRoot);
            if (licenseFiles.Count > 0)
                onLog?.Invoke($"[Built-in] Found {licenseFiles.Count} license file(s) in AOSService/Scripts/License/");

            // Copy static template files
            onLog?.Invoke("[Built-in] Writing template scaffold...");
            CopyTemplateFiles(outputDir, assetsDir);

            // Process each module: look in AOSService/Packages/files/ (dynamicsax-*.zip and *.nupkg),
            // then fallback to AOSService/Packages/. Use case-insensitive lookup for AOSService, Packages, files (Linux).
            var aosServiceDir = FindSubdirectory(extractRoot, "AOSService");
            var packagesDir = aosServiceDir != null ? FindSubdirectory(aosServiceDir, "Packages") : null;
            var filesDir = packagesDir != null ? FindSubdirectory(packagesDir, "files") : null;
            var packagesDirAlt = packagesDir ?? (aosServiceDir != null ? Path.Combine(aosServiceDir, "Packages") : null);
            var filesDirAlt = filesDir ?? (packagesDirAlt != null ? Path.Combine(packagesDirAlt, "files") : null);
            var managedZipNames = new List<string>();
            var correlationId = Guid.NewGuid().ToString();
            var timestamp = DateTime.UtcNow.ToString("M/d/yyyy h:mm:ss tt");
            var licenseGuid = Guid.NewGuid().ToString();

            var moduleArchives = new List<(string FilePath, string ModuleName)>();

            if (filesDir != null && Directory.Exists(filesDir))
            {
                foreach (var z in Directory.GetFiles(filesDir, "dynamicsax-*.zip"))
                    moduleArchives.Add((z, PackageAnalyzer.ExtractModuleName(Path.GetFileNameWithoutExtension(z))));
                foreach (var n in Directory.GetFiles(filesDir, "*.nupkg"))
                    moduleArchives.Add((n, PackageAnalyzer.ExtractModuleNameFromNupkg(Path.GetFileName(n))));
            }

            if (moduleArchives.Count == 0 && packagesDirAlt != null && Directory.Exists(packagesDirAlt))
            {
                onLog?.Invoke("[Built-in] AOSService/Packages/files/ empty or missing, trying AOSService/Packages/...");
                foreach (var z in Directory.GetFiles(packagesDirAlt, "dynamicsax-*.zip"))
                    moduleArchives.Add((z, PackageAnalyzer.ExtractModuleName(Path.GetFileNameWithoutExtension(z))));
                foreach (var n in Directory.GetFiles(packagesDirAlt, "*.nupkg"))
                    moduleArchives.Add((n, PackageAnalyzer.ExtractModuleNameFromNupkg(Path.GetFileName(n))));
            }

            // Last resort: search recursively under AOSService for any dynamicsax-*.zip or *.nupkg (handles alternate layouts)
            if (moduleArchives.Count == 0 && aosServiceDir != null && Directory.Exists(aosServiceDir))
            {
                onLog?.Invoke("[Built-in] No modules in standard paths, searching under AOSService/...");
                foreach (var z in Directory.GetFiles(aosServiceDir, "dynamicsax-*.zip", SearchOption.AllDirectories))
                    moduleArchives.Add((z, PackageAnalyzer.ExtractModuleName(Path.GetFileNameWithoutExtension(z))));
                foreach (var n in Directory.GetFiles(aosServiceDir, "*.nupkg", SearchOption.AllDirectories))
                    moduleArchives.Add((n, PackageAnalyzer.ExtractModuleNameFromNupkg(Path.GetFileName(n))));
            }

            // Ultimate fallback: ZIP may use backslashes; .NET on Linux can create dirs like "Packages\files".
            // Directory.GetFiles with glob may not find files in such dirs on Linux; enumerate all and filter by name.
            // Use .zip only when present (deployable content); fall back to .nupkg only if no .zip found.
            if (moduleArchives.Count == 0)
            {
                onLog?.Invoke("[Built-in] Searching entire extract directory for module archives...");
                var zipFiles = Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories)
                    .Where(f => {
                        var seg = GetLastPathSegment(f);
                        return seg.StartsWith("dynamicsax-", StringComparison.OrdinalIgnoreCase) && seg.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                    }).ToArray();
                var nupkgFiles = Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories)
                    .Where(f => {
                        var seg = GetLastPathSegment(f);
                        return seg.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && seg.Contains("dynamicsax-", StringComparison.OrdinalIgnoreCase);
                    }).ToArray();
                var byModule = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (zipFiles.Length > 0)
                {
                    foreach (var z in zipFiles)
                    {
                        var fileName = GetLastPathSegment(z);
                        var name = PackageAnalyzer.ExtractModuleName(Path.GetFileNameWithoutExtension(fileName));
                        if (!string.IsNullOrEmpty(name)) byModule[name] = z;
                    }
                    onLog?.Invoke($"[Built-in] Found {byModule.Count} module(s) from .zip files");
                }
                if (byModule.Count == 0)
                {
                    foreach (var n in nupkgFiles)
                    {
                        var fileName = GetLastPathSegment(n);
                        var name = PackageAnalyzer.ExtractModuleNameFromNupkg(fileName);
                        if (!string.IsNullOrEmpty(name)) byModule[name] = n;
                    }
                    if (byModule.Count > 0)
                        onLog?.Invoke($"[Built-in] Using .nupkg (no .zip found): {byModule.Count} module(s)");
                }
                foreach (var kv in byModule)
                    moduleArchives.Add((kv.Value, kv.Key));
            }

            // Deduplicate by module name (e.g. same module as .zip and .nupkg); prefer first occurrence.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            moduleArchives = moduleArchives
                .Where(m => seen.Add(m.ModuleName))
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

            // Save LCS root folder name for round-trip (Unified→LCS will restore same structure)
            if (!string.IsNullOrWhiteSpace(lcsRootFolderName))
            {
                var rootMarkerPath = Path.Combine(outputDir, "DeployPortalLcsRoot.txt");
                await File.WriteAllTextAsync(rootMarkerPath, lcsRootFolderName.Trim());
            }

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

        string lcsOutputRoot;
        if (!string.IsNullOrEmpty(_lcsTemplatePath) && (File.Exists(_lcsTemplatePath) || Directory.Exists(_lcsTemplatePath)))
        {
            onLog?.Invoke($"[Unified→LCS] Using LCS template: {_lcsTemplatePath}");
            ApplyLcsTemplate(_lcsTemplatePath, outputDir, onLog);
            lcsOutputRoot = ResolveLcsOutputRoot(outputDir);
            onLog?.Invoke($"[Unified→LCS] LCS root: {Path.GetFileName(lcsOutputRoot)}");
        }
        else
        {
            lcsOutputRoot = outputDir;
        }

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

            // If we didn't use a template, restore root folder from marker (same as before)
            if (string.IsNullOrEmpty(_lcsTemplatePath) || (!File.Exists(_lcsTemplatePath) && !Directory.Exists(_lcsTemplatePath)))
            {
                var rootMarkerPath = Path.Combine(tempDir, "DeployPortalLcsRoot.txt");
                if (File.Exists(rootMarkerPath))
                {
                    var rootName = (await File.ReadAllTextAsync(rootMarkerPath)).Trim();
                    if (!string.IsNullOrEmpty(rootName))
                    {
                        lcsOutputRoot = Path.Combine(outputDir, rootName);
                        Directory.CreateDirectory(lcsOutputRoot);
                        onLog?.Invoke($"[Unified→LCS] Using LCS root folder: {rootName}");
                    }
                }
            }

            // When template has a single root folder named "AOSService", that folder IS the AOSService dir (no nested AOSService).
            // Otherwise lcsOutputRoot is the full LCS root (e.g. AX_...) and we use lcsOutputRoot/AOSService.
            var lcsRootName = Path.GetFileName(lcsOutputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var isAosServiceRoot = string.Equals(lcsRootName, "AOSService", StringComparison.OrdinalIgnoreCase);
            var aosServiceDir = isAosServiceRoot ? lcsOutputRoot : Path.Combine(lcsOutputRoot, "AOSService");
            var lcsRootForHotfix = isAosServiceRoot ? Path.GetDirectoryName(lcsOutputRoot)! : lcsOutputRoot;

            var packagesDir = Path.Combine(aosServiceDir, "Packages");
            var filesDir = Path.Combine(packagesDir, "files");
            Directory.CreateDirectory(filesDir);

            // When using template, clear existing .nupkg/.zip in files (and our .nupkg in Packages) so we only have our converted modules
            if (!string.IsNullOrEmpty(_lcsTemplatePath))
            {
                foreach (var f in Directory.GetFiles(filesDir, "*.nupkg").Concat(Directory.GetFiles(filesDir, "*.zip")))
                    try { File.Delete(f); } catch { }
                foreach (var f in Directory.GetFiles(packagesDir, "dynamicsax-*.nupkg"))
                    try { File.Delete(f); } catch { }
            }

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

                // Generate .nupkg in AOSService/Packages/ (LCS often has both .zip in files/ and .nupkg in Packages/)
                var nupkgId = $"dynamicsax-{moduleName}";
                var nupkgFileName = $"{nupkgId}.{moduleVersion}.nupkg";
                var nupkgPath = Path.Combine(packagesDir, nupkgFileName);
                await CreateNupkgFromManagedZipAsync(managedZipPath, moduleName, moduleVersion, nupkgPath);
            }

            if (allLicenseFiles.Count > 0)
            {
                var licenseDir = Path.Combine(aosServiceDir, "Scripts", "License");
                Directory.CreateDirectory(licenseDir);
                foreach (var (fileName, content) in allLicenseFiles)
                    await File.WriteAllBytesAsync(Path.Combine(licenseDir, fileName), content);
                onLog?.Invoke($"[Unified→LCS] Restored {allLicenseFiles.Count} license file(s)");
            }

            GenerateHotfixInstallationInfo(lcsRootForHotfix, moduleEntries, platformVersion);
            onLog?.Invoke("[Unified→LCS] Generated HotfixInstallationInfo.xml");
            onLog?.Invoke($"[Unified→LCS] Generated {moduleEntries.Count} .nupkg in AOSService/Packages/");
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

    /// <summary>
    /// Applies LCS template: extracts zip or copies directory into outputDir.
    /// Template can be a .zip (extracted as-is) or a folder. If folder has one child dir, that child is copied into outputDir; else folder contents are copied (folder is the LCS root).
    /// </summary>
    private static void ApplyLcsTemplate(string templatePath, string outputDir, Action<string>? onLog)
    {
        if (File.Exists(templatePath))
        {
            ZipFile.ExtractToDirectory(templatePath, outputDir);
            onLog?.Invoke("[Unified→LCS] Extracted LCS template ZIP");
            return;
        }
        if (!Directory.Exists(templatePath))
            return;

        var entries = Directory.GetFileSystemEntries(templatePath);
        var dirs = entries.Where(e => Directory.Exists(e)).ToList();
        var files = entries.Where(e => File.Exists(e)).ToList();

        // One child directory → treat as LCS root folder, copy it into outputDir
        if (dirs.Count == 1 && files.Count == 0)
        {
            FileHelper.CopyDirectoryRecursive(dirs[0], Path.Combine(outputDir, Path.GetFileName(dirs[0])));
            onLog?.Invoke($"[Unified→LCS] Copied LCS template root: {Path.GetFileName(dirs[0])}");
            return;
        }
        // Template path is the LCS root folder (has AOSService, HotfixInstallationInfo.xml) → copy into outputDir preserving root name
        var rootName = Path.GetFileName(templatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(rootName)) rootName = "LcsRoot";
        FileHelper.CopyDirectoryRecursive(templatePath, Path.Combine(outputDir, rootName));
        onLog?.Invoke($"[Unified→LCS] Copied LCS template root: {rootName}");
    }

    /// <summary>
    /// After applying template, outputDir contains either one child (the LCS root) or AOSService at top level. Returns the path to the LCS root.
    /// </summary>
    private static string ResolveLcsOutputRoot(string outputDir)
    {
        var entries = Directory.GetFileSystemEntries(outputDir);
        var dirs = entries.Where(e => Directory.Exists(e)).ToList();
        if (dirs.Count == 1)
            return dirs[0];
        return outputDir;
    }

    /// <summary>
    /// Creates a minimal .nupkg (NuGet package) with only the .nuspec manifest. In LCS, Packages/*.nupkg are
    /// small metadata packages (~33 KB); the actual deployable content is in Packages/files/*.zip only.
    /// We must not duplicate the module payload into .nupkg (that would double the size).
    /// </summary>
    private static async Task CreateNupkgFromManagedZipAsync(string managedZipPath, string moduleName, string moduleVersion, string nupkgPath)
    {
        var nuspecId = $"dynamicsax-{moduleName}";
        var nuspecXml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"">
  <metadata>
    <id>{nuspecId}</id>
    <version>{moduleVersion}</version>
    <description>Dynamics 365 deployable module {moduleName}</description>
    <authors>DeployPortal</authors>
  </metadata>
</package>";

        using var nupkgZip = ZipFile.Open(nupkgPath, ZipArchiveMode.Create);
        var nuspecEntry = nupkgZip.CreateEntry($"{nuspecId}.nuspec", CompressionLevel.Optimal);
        await using (var nuspecStream = nuspecEntry.Open())
        await using (var nuspecWriter = new StreamWriter(nuspecStream, System.Text.Encoding.UTF8))
            await nuspecWriter.WriteAsync(nuspecXml);
    }

    /// <summary>
    /// If the directory has exactly one child directory (and no files at root), return that child.
    /// This handles LCS ZIPs that have a single root folder (e.g. MyPackage_1.0/AOSService/...).
    /// </summary>
    private static string ResolveExtractRoot(string tempDir)
    {
        var entries = Directory.GetFileSystemEntries(tempDir);
        var dirs = entries.Where(e => Directory.Exists(e)).ToList();
        var files = entries.Where(e => File.Exists(e)).ToList();
        if (dirs.Count == 1 && files.Count == 0)
            return dirs[0];
        return tempDir;
    }

    /// <summary>
    /// Gets the last path segment (file name) splitting by both / and \.
    /// On Linux, Path.GetFileName does not treat backslash as separator; ZIPs from Windows can have paths with \.
    /// </summary>
    private static string GetLastPathSegment(string fullPath)
    {
        var lastSlash = fullPath.LastIndexOfAny(new[] { '/', '\\' });
        return lastSlash >= 0 ? fullPath[(lastSlash + 1)..] : fullPath;
    }

    /// <summary>
    /// Finds a direct subdirectory of parent with the given name (case-insensitive).
    /// On Linux, zip entries with backslash (Windows) create dirs like "AOSService\Packages"; match by logical name.
    /// </summary>
    private static string? FindSubdirectory(string parentDir, string name)
    {
        if (!Directory.Exists(parentDir)) return null;
        return FileHelper.FindChildDirectory(parentDir, name)
            ?? Directory.GetDirectories(parentDir)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
    }

    internal static List<(string FileName, string FullPath)> ExtractLicenseFiles(string extractedLcsDir)
    {
        var result = new List<(string, string)>();
        // Standard path: AOSService/Scripts/License (or AOSService\Scripts\License on Windows-style zips)
        var aosService = FindSubdirectory(extractedLcsDir, "AOSService");
        var licenseDir = aosService != null
            ? Path.Combine(aosService, "Scripts", "License")
            : Path.Combine(extractedLcsDir, "AOSService", "Scripts", "License");
        if (Directory.Exists(licenseDir))
        {
            foreach (var file in Directory.GetFiles(licenseDir))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".txt" || ext == ".xml")
                    result.Add((Path.GetFileName(file), file));
            }
        }

        // Fallback: ZIP may use backslashes; on Linux we get dirs like "AOSService\Scripts\License" (literal \ in name).
        // Find any file under a path that contains Scripts and License, .txt/.xml.
        if (result.Count == 0 && Directory.Exists(extractedLcsDir))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(extractedLcsDir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".txt" && ext != ".xml") continue;
                var pathNorm = file.Replace('\\', '/');
                if (!pathNorm.Contains("Scripts", StringComparison.OrdinalIgnoreCase) ||
                    !pathNorm.Contains("License", StringComparison.OrdinalIgnoreCase))
                    continue;
                var fileName = GetLastPathSegment(file);
                if (string.IsNullOrEmpty(fileName) || !seen.Add(fileName)) continue;
                result.Add((fileName, file));
            }
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
            if (File.Exists(outputZipPath))
                File.Delete(outputZipPath);
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
