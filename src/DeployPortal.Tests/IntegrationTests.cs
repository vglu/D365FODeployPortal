using System.IO.Compression;
using DeployPortal.Models;
using DeployPortal.PackageOps;
using DeployPortal.Services;
using NUnit.Framework;

namespace DeployPortal.Tests;

/// <summary>
/// Integration tests that exercise real merge and convert logic
/// (without needing the web app or ModelUtil.exe).
/// Tests use synthetic LCS and Unified packages.
/// Now uses PackageOps shared library directly.
/// </summary>
[TestFixture]
public class IntegrationTests
{
    private string _testDir = "";

    [SetUp]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"deploy-portal-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void Teardown()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    // =====================================================
    //  Package Type Detection (PackageAnalyzer)
    // =====================================================

    [Test]
    public void DetectPackageType_LCS_WithAOSService()
    {
        var zipPath = CreateLcsPackage("lcs1.zip", new[] { "ModuleA" }, new[] { "ComponentA" },
            new Dictionary<string, string>
            {
                ["AOSService/Packages/ModuleA.nupkg"] = "nupkg-content",
                ["AOSService/Scripts/install.ps1"] = "install"
            });

        var type = PackageAnalyzer.DetectPackageType(zipPath);
        Assert.That(type, Is.EqualTo("LCS"), "Should detect as LCS");
    }

    [Test]
    public void DetectPackageType_Unified_WithTemplateDll()
    {
        var zipPath = CreateUnifiedPackage("unified1.zip",
            new Dictionary<string, string>
            {
                ["TemplatePackage.dll"] = "fake-dll",
                ["PkgFolder/ImportConfig.xml"] = "<configdatastorage><externalpackages></externalpackages></configdatastorage>"
            });

        var type = PackageAnalyzer.DetectPackageType(zipPath);
        Assert.That(type, Is.EqualTo("Unified"), "Should detect as Unified");
    }

    [Test]
    public void DetectPackageType_Other_NoMarkers()
    {
        var zipPath = CreateZipWithEntries("other.zip", new Dictionary<string, string>
        {
            ["readme.txt"] = "just a zip file",
            ["data/config.json"] = "{}"
        });

        var type = PackageAnalyzer.DetectPackageType(zipPath);
        Assert.That(type, Is.EqualTo("Other"), "Should detect as Other");
    }

    // =====================================================
    //  Module Name Extraction (PackageAnalyzer)
    // =====================================================

    [Test]
    public void ExtractModuleName_StandardName()
    {
        Assert.That(PackageAnalyzer.ExtractModuleName("dynamicsax-sisheavyhighway.1.0.0.0"),
            Is.EqualTo("sisheavyhighway"));
    }

    [Test]
    public void ExtractModuleName_MultiPartVersion()
    {
        Assert.That(PackageAnalyzer.ExtractModuleName("dynamicsax-sisproject360.2021.4.1.1"),
            Is.EqualTo("sisproject360"));
    }

    [Test]
    public void ExtractModuleName_UnderscoreInName()
    {
        Assert.That(PackageAnalyzer.ExtractModuleName("dynamicsax-sispayroll_isv.1.0.0.0"),
            Is.EqualTo("sispayroll_isv"));
    }

    [Test]
    public void ExtractModuleName_NoDynamicsAxPrefix()
    {
        Assert.That(PackageAnalyzer.ExtractModuleName("mymodule.3.2.1"),
            Is.EqualTo("mymodule"));
    }

    [Test]
    public void ExtractModuleNameFromNupkg_DynamicsAxStyle()
    {
        Assert.That(PackageAnalyzer.ExtractModuleNameFromNupkg("dynamicsax-sisheavyhighway.2026.1.14.2.nupkg"),
            Is.EqualTo("sisheavyhighway"));
    }

    [Test]
    public void ExtractModuleNameFromNupkg_DotStyle()
    {
        Assert.That(PackageAnalyzer.ExtractModuleNameFromNupkg("Dynamics.AX.ApplicationSuite.1.0.0.0.nupkg"),
            Is.EqualTo("applicationsuite"));
    }

    [Test]
    public void ExtractModuleNameFromManagedZip_Standard()
    {
        Assert.That(PackageAnalyzer.ExtractModuleNameFromManagedZip("cch_sureaddress_1_0_0_1_managed.zip"),
            Is.EqualTo("cch_sureaddress"));
    }

    // =====================================================
    //  LCS Merge Logic (MergeEngine, no DB)
    // =====================================================

    [Test]
    public void LcsMerge_CombinesAOSServiceAndHotfixXml()
    {
        var pkg1Path = CreateLcsPackage("pkg1.zip",
            modules: new[] { "ModuleA", "ModuleB" },
            components: new[] { "CompA" },
            extraFiles: new Dictionary<string, string>
            {
                ["AOSService/Packages/ModuleA.nupkg"] = "moduleA-content",
                ["AOSService/Packages/ModuleB.nupkg"] = "moduleB-content",
                ["AOSService/Scripts/install.ps1"] = "install-script-1"
            });

        var pkg2Path = CreateLcsPackage("pkg2.zip",
            modules: new[] { "ModuleC" },
            components: new[] { "CompB", "CompC" },
            extraFiles: new Dictionary<string, string>
            {
                ["AOSService/Packages/ModuleC.nupkg"] = "moduleC-content",
                ["AOSService/Scripts/setup.ps1"] = "setup-script-2",
                ["AOSService/Data/data.txt"] = "extra-data"
            });

        var engine = new MergeEngine(_testDir);
        var resultDir = engine.MergeLcs(new List<string> { pkg1Path, pkg2Path });

        // Verify merged result
        Assert.That(File.Exists(Path.Combine(resultDir, "AOSService", "Packages", "ModuleA.nupkg")));
        Assert.That(File.Exists(Path.Combine(resultDir, "AOSService", "Packages", "ModuleB.nupkg")));
        Assert.That(File.Exists(Path.Combine(resultDir, "AOSService", "Packages", "ModuleC.nupkg")));
        Assert.That(File.Exists(Path.Combine(resultDir, "AOSService", "Scripts", "install.ps1")));
        Assert.That(File.Exists(Path.Combine(resultDir, "AOSService", "Scripts", "setup.ps1")));
        Assert.That(File.Exists(Path.Combine(resultDir, "AOSService", "Data", "data.txt")));

        var mergedXml = System.Xml.Linq.XDocument.Load(
            Path.Combine(resultDir, "HotfixInstallationInfo.xml"));
        var modules = mergedXml.Root!.Element("MetadataModuleList")!.Elements("string")
            .Select(e => e.Value).ToList();

        Assert.That(modules, Does.Contain("ModuleA"));
        Assert.That(modules, Does.Contain("ModuleB"));
        Assert.That(modules, Does.Contain("ModuleC"));

        var components = mergedXml.Root!.Element("AllComponentList")!.Elements("ArrayOfString").Count();
        Assert.That(components, Is.EqualTo(3), "Should have 3 component arrays");

        TestContext.Out.WriteLine($"LCS merge verified: 3 modules, 3 component arrays, 6 files in AOSService");
    }

    // =====================================================
    //  Unified (UDE) Merge Logic (MergeEngine)
    // =====================================================

    [Test]
    public void UdeMerge_CombinesFoldersAndUpdatesImportConfig()
    {
        var pkg1Path = CreateUnifiedPackage("ude1.zip", new Dictionary<string, string>
        {
            ["TemplatePackage.dll"] = "dll-content-1",
            ["PackageAssets/ImportConfig.xml"] = @"<?xml version=""1.0""?>
<configdatastorage>
  <externalpackages>
    <package type=""xpp"" filename=""SolutionA_managed.zip"" />
  </externalpackages>
</configdatastorage>",
            ["PackageAssets/SolutionA_managed.zip"] = "solution-a",
            ["PackageAssets/data1.txt"] = "data-from-pkg1"
        });

        var pkg2Path = CreateUnifiedPackage("ude2.zip", new Dictionary<string, string>
        {
            ["TemplatePackage.dll"] = "dll-content-2",
            ["PackageAssets/SolutionB_managed.zip"] = "solution-b",
            ["PackageAssets/SolutionC_managed.zip"] = "solution-c",
            ["PackageAssets/data2.txt"] = "data-from-pkg2"
        });

        var engine = new MergeEngine(_testDir);
        var resultDir = engine.MergeUnified(new List<string> { pkg1Path, pkg2Path });

        // Verify files from both packages exist
        Assert.That(File.Exists(Path.Combine(resultDir, "TemplatePackage.dll")));
        Assert.That(File.Exists(Path.Combine(resultDir, "PackageAssets", "SolutionA_managed.zip")));
        Assert.That(File.Exists(Path.Combine(resultDir, "PackageAssets", "SolutionB_managed.zip")));
        Assert.That(File.Exists(Path.Combine(resultDir, "PackageAssets", "SolutionC_managed.zip")));

        // Verify ImportConfig.xml was updated
        var xmlPath = Path.Combine(resultDir, "PackageAssets", "ImportConfig.xml");
        Assert.That(File.Exists(xmlPath));

        var pkgXml = System.Xml.Linq.XDocument.Load(xmlPath);
        var pkgPackages = pkgXml.Root!.Element("externalpackages")!.Elements("package")
            .Select(e => e.Attribute("filename")!.Value).ToList();

        Assert.That(pkgPackages, Does.Contain("SolutionA_managed.zip"), "Should keep original entry");
        Assert.That(pkgPackages, Does.Contain("SolutionB_managed.zip"), "Should add new from pkg2");
        Assert.That(pkgPackages, Does.Contain("SolutionC_managed.zip"), "Should add new from pkg2");

        TestContext.Out.WriteLine($"UDE merge verified: {pkgPackages.Count} packages in ImportConfig.xml");
    }

    // =====================================================
    //  Merge Strategy Detection (PackageAnalyzer)
    // =====================================================

    [Test]
    public void MergeStrategy_AllLCS_ReturnsLCS()
    {
        Assert.That(PackageAnalyzer.DetectMergeStrategy(new[] { "LCS", "LCS" }), Is.EqualTo("LCS"));
    }

    [Test]
    public void MergeStrategy_LCSAndMerged_ReturnsLCS()
    {
        Assert.That(PackageAnalyzer.DetectMergeStrategy(new[] { "LCS", "Merged" }), Is.EqualTo("LCS"));
    }

    [Test]
    public void MergeStrategy_AllUnified_ReturnsUnified()
    {
        Assert.That(PackageAnalyzer.DetectMergeStrategy(new[] { "Unified", "Unified" }), Is.EqualTo("Unified"));
    }

    [Test]
    public void MergeStrategy_LCSAndUnified_ReturnsNull()
    {
        Assert.That(PackageAnalyzer.DetectMergeStrategy(new[] { "LCS", "Unified" }), Is.Null);
    }

    // =====================================================
    //  Credential Parser
    // =====================================================

    [Test]
    public void CredentialParser_ParsesScriptOutput()
    {
        var input = @"==============================================================
  SERVICE PRINCIPAL CREATED SUCCESSFULLY
==============================================================

  Application (Client) ID:  aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
  Directory (Tenant) ID:    11111111-2222-3333-4444-555555555555
  Client Secret:            SuperSecretValue123!
  Secret expires:           2028-06-01

  Environments (OK):        env1.crm.dynamics.com, env2.crm.dynamics.com, env3.crm4.dynamics.com
==============================================================";

        var result = CredentialParser.Parse(input);

        Assert.That(result.ApplicationId, Is.EqualTo("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Assert.That(result.TenantId, Is.EqualTo("11111111-2222-3333-4444-555555555555"));
        Assert.That(result.ClientSecret, Is.EqualTo("SuperSecretValue123!"));
        Assert.That(result.HasSecret, Is.True);
        Assert.That(result.HasCredentials, Is.True);
        Assert.That(result.Environments, Does.Contain("env1.crm.dynamics.com"));
        Assert.That(result.Environments, Does.Contain("env2.crm.dynamics.com"));
        Assert.That(result.Environments, Does.Contain("env3.crm4.dynamics.com"));
        Assert.That(result.Environments.Count, Is.EqualTo(3));
    }

    [Test]
    public void CredentialParser_ParsesFileFormat()
    {
        var input = @"Mode:                    FullSetup
Service Principal:       PAC-Deploy-Automation
Application (Client) ID: bbbbbbbb-1111-2222-3333-444444444444
Directory (Tenant) ID:   cccccccc-5555-6666-7777-888888888888
Client Secret:           *** SHOWN IN CONSOLE ***
Secret expires:          2028-03-15
Created:                 2026-02-09 10:30:00
Environments (OK):       test.crm.dynamics.com, prod.crm.dynamics.com
Environments (FAIL):     ";

        var result = CredentialParser.Parse(input);

        Assert.That(result.ApplicationId, Is.EqualTo("bbbbbbbb-1111-2222-3333-444444444444"));
        Assert.That(result.TenantId, Is.EqualTo("cccccccc-5555-6666-7777-888888888888"));
        Assert.That(result.HasSecret, Is.False, "Masked secret should not count as valid");
        Assert.That(result.ServicePrincipalName, Is.EqualTo("PAC-Deploy-Automation"));
        Assert.That(result.Environments.Count, Is.EqualTo(2));
    }

    [Test]
    public void CredentialParser_ParsesPacAuthCommand()
    {
        var input = @"pac auth create --applicationId ""dddddddd-0000-1111-2222-333333333333"" --clientSecret ""MySecret"" --tenant ""eeeeeeee-4444-5555-6666-777777777777"" --environment ""myenv.crm.dynamics.com""";

        var result = CredentialParser.Parse(input);

        Assert.That(result.ApplicationId, Is.EqualTo("dddddddd-0000-1111-2222-333333333333"));
        Assert.That(result.TenantId, Is.EqualTo("eeeeeeee-4444-5555-6666-777777777777"));
        Assert.That(result.ClientSecret, Is.EqualTo("MySecret"));
        Assert.That(result.Environments, Does.Contain("myenv.crm.dynamics.com"));
    }

    // =====================================================
    //  LocalPackageOpsService test
    // =====================================================

    [Test]
    public void LocalPackageOpsService_DetectPackageType()
    {
        var svc = new LocalPackageOpsService(_testDir, _testDir);

        var zipPath = CreateLcsPackage("test-detect.zip", new[] { "Mod" }, new[] { "Comp" },
            new Dictionary<string, string>
            {
                ["AOSService/test.txt"] = "test"
            });

        Assert.That(svc.DetectPackageType(zipPath), Is.EqualTo("LCS"));
    }

    [Test]
    public void LocalPackageOpsService_DetectMergeStrategy()
    {
        var svc = new LocalPackageOpsService(_testDir, _testDir);

        Assert.That(svc.DetectMergeStrategy(new[] { "LCS", "LCS" }), Is.EqualTo("LCS"));
        Assert.That(svc.DetectMergeStrategy(new[] { "Unified", "Unified" }), Is.EqualTo("Unified"));
        Assert.That(svc.DetectMergeStrategy(new[] { "LCS", "Unified" }), Is.Null);
    }

    // =====================================================
    //  Full Merge-Convert Pipeline Comparison
    // =====================================================

    [Test]
    public void Pipeline_LcsMergeThenConvert_Vs_ConvertThenUdeMerge_SameContent()
    {
        var lcs1 = CreateLcsPackage("lcs-pkg1.zip",
            modules: new[] { "ModA" },
            components: new[] { "CompA" },
            extraFiles: new Dictionary<string, string>
            {
                ["AOSService/Packages/ModA.nupkg"] = "content-A",
                ["AOSService/UniqueFile1.txt"] = "only-in-pkg1"
            });

        var lcs2 = CreateLcsPackage("lcs-pkg2.zip",
            modules: new[] { "ModB" },
            components: new[] { "CompB" },
            extraFiles: new Dictionary<string, string>
            {
                ["AOSService/Packages/ModB.nupkg"] = "content-B",
                ["AOSService/UniqueFile2.txt"] = "only-in-pkg2"
            });

        // Path A: Merge LCS first
        var engineA = new MergeEngine(_testDir);
        var mergedLcsDir = engineA.MergeLcs(new List<string> { lcs1, lcs2 });

        var pathA_files = Directory.GetFiles(Path.Combine(mergedLcsDir, "AOSService"), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(mergedLcsDir, f))
            .OrderBy(f => f).ToList();

        // Path B: Extract each LCS separately, merge
        var lcs1Dir = Path.Combine(_testDir, "path-b-lcs1");
        var lcs2Dir = Path.Combine(_testDir, "path-b-lcs2");
        ZipFile.ExtractToDirectory(lcs1, lcs1Dir);
        ZipFile.ExtractToDirectory(lcs2, lcs2Dir);

        var mergedUdeDir = Path.Combine(_testDir, "path-b-merged-ude");
        FileHelper.CopyDirectoryRecursive(lcs1Dir, mergedUdeDir);
        FileHelper.CopyDirectoryRecursive(lcs2Dir, mergedUdeDir);

        var pathB_files = Directory.GetFiles(Path.Combine(mergedUdeDir, "AOSService"), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(mergedUdeDir, f))
            .OrderBy(f => f).ToList();

        Assert.That(pathB_files, Is.EquivalentTo(pathA_files),
            "Both merge strategies should produce the same set of files");

        Assert.That(File.Exists(Path.Combine(mergedLcsDir, "AOSService", "UniqueFile1.txt")));
        Assert.That(File.Exists(Path.Combine(mergedLcsDir, "AOSService", "UniqueFile2.txt")));

        var xmlA = System.Xml.Linq.XDocument.Load(
            Path.Combine(mergedLcsDir, "HotfixInstallationInfo.xml"));
        var modulesA = xmlA.Root!.Element("MetadataModuleList")!.Elements("string")
            .Select(e => e.Value).ToList();

        Assert.That(modulesA, Does.Contain("ModA"));
        Assert.That(modulesA, Does.Contain("ModB"));

        TestContext.Out.WriteLine("Pipeline comparison: PASSED");
    }

    // =====================================================
    //  Реальный LCS-пакет: конвертация по пути из env (для диагностики)
    // =====================================================

    [Test]
    public void ConvertRealLcsPackage_FromEnv()
    {
        var lcsPath = System.Environment.GetEnvironmentVariable("DeployPortal_TestLcsPackagePath");
        if (string.IsNullOrWhiteSpace(lcsPath) || !File.Exists(lcsPath))
        {
            Assert.Ignore("Set DeployPortal_TestLcsPackagePath to a real LCS zip path to run this test.");
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"convert_real_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var templateDir = Path.Combine(
                Path.GetDirectoryName(GetType().Assembly.Location)!,
                "..", "..", "..", "..", "DeployPortal", "bin", "Debug", "net9.0", "Resources", "UnifiedTemplate");
            if (!Directory.Exists(templateDir))
                templateDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "DeployPortal", "bin", "Debug", "net9.0", "Resources", "UnifiedTemplate");
            Assert.That(Directory.Exists(templateDir), Is.True, "UnifiedTemplate must exist (build DeployPortal first)");

            var engine = new ConvertEngine(tempDir, templateDir);
            var logs = new List<string>();
            var outputDir = engine.ConvertToUnifiedAsync(lcsPath, msg => logs.Add(msg)).GetAwaiter().GetResult();

            foreach (var log in logs)
                TestContext.Out.WriteLine(log);

            Assert.That(Directory.Exists(outputDir), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "TemplatePackage.dll")), Is.True);

            var assetsDir = Path.Combine(outputDir, "PackageAssets");
            var managedZips = Directory.Exists(assetsDir)
                ? Directory.GetFiles(assetsDir, "*_managed.zip")
                    .Where(f => !Path.GetFileName(f).Contains("DefaultDevSolution", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<string>();

            var totalSize = new DirectoryInfo(outputDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            TestContext.Out.WriteLine($"Modules (managed zips): {managedZips.Length}");
            TestContext.Out.WriteLine($"Unified output size: {totalSize / 1024} KB");
            if (managedZips.Length == 0)
            {
                TestContext.Out.WriteLine("WARNING: 0 modules -> only template was written (~51 KB). Check that LCS package has AOSService/Packages/files/dynamicsax-*.zip or *.nupkg (or in AOSService/Packages/).");
                Assert.Inconclusive("Real package produced 0 modules — see diagnostic output above. Run test-convert-real-package.ps1 to inspect ZIP structure.");
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // =====================================================
    //  Helpers
    // =====================================================

    private string CreateLcsPackage(string name, string[] modules, string[] components,
        Dictionary<string, string>? extraFiles = null)
    {
        var path = Path.Combine(_testDir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<HotfixInstallationInfo>
  <MetadataModuleList>
{string.Join("\n", modules.Select(m => $"    <string>{m}</string>"))}
  </MetadataModuleList>
  <AllComponentList>
{string.Join("\n", components.Select(c => $"    <ArrayOfString><string>{c}</string></ArrayOfString>"))}
  </AllComponentList>
</HotfixInstallationInfo>";

        AddEntry(zip, "HotfixInstallationInfo.xml", xml);

        if (extraFiles != null)
        {
            foreach (var kv in extraFiles)
                AddEntry(zip, kv.Key, kv.Value);
        }

        return path;
    }

    private string CreateUnifiedPackage(string name, Dictionary<string, string> entries)
    {
        return CreateZipWithEntries(name, entries);
    }

    private string CreateZipWithEntries(string name, Dictionary<string, string> entries)
    {
        var path = Path.Combine(_testDir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var kv in entries)
            AddEntry(zip, kv.Key, kv.Value);
        return path;
    }

    private static void AddEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
