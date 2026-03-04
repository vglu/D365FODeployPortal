using System.IO.Compression;
using DeployPortal.PackageOps;
using NUnit.Framework;

namespace DeployPortal.Tests.PackageOps;

[TestFixture]
public class PackageAnalyzerTests
{
    private string _testDir = null!;

    [SetUp]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"PackageAnalyzerTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Test]
    public void DetectPackageType_ReturnsUnified_WhenTemplatePackageDllExists()
    {
        var zipPath = Path.Combine(_testDir, "unified.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("Metadata/TemplatePackage.dll");
        }
        Assert.That(PackageAnalyzer.DetectPackageType(zipPath), Is.EqualTo("Unified"));
    }

    [Test]
    public void DetectPackageType_ReturnsLcs_WhenAOSServiceExists()
    {
        var zipPath = Path.Combine(_testDir, "lcs.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("AOSService/SomeFile.txt");
        }
        Assert.That(PackageAnalyzer.DetectPackageType(zipPath), Is.EqualTo("LCS"));
    }

    [Test]
    public void DetectPackageType_ReturnsLcs_WhenHotfixInstallationInfoExists()
    {
        var zipPath = Path.Combine(_testDir, "lcs2.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("HotfixInstallationInfo.xml");
        }
        Assert.That(PackageAnalyzer.DetectPackageType(zipPath), Is.EqualTo("LCS"));
    }

    [Test]
    public void DetectPackageType_ReturnsOther_WhenEmptyZip()
    {
        var zipPath = Path.Combine(_testDir, "empty.zip");
        using (ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }
        Assert.That(PackageAnalyzer.DetectPackageType(zipPath), Is.EqualTo("Other"));
    }

    [Test]
    public void ExtractModuleName_StripsDynamicsAxPrefixAndVersion()
    {
        Assert.That(PackageAnalyzer.ExtractModuleName("dynamicsax-contosoapp.1.0.0.0"), Is.EqualTo("contosoapp"));
        Assert.That(PackageAnalyzer.ExtractModuleName("dynamicsax-contosoproject.2021.4.1.1"), Is.EqualTo("contosoproject"));
    }

    [Test]
    public void ExtractModuleNameFromNupkg_ReturnsLastSegmentWithoutVersion()
    {
        Assert.That(PackageAnalyzer.ExtractModuleNameFromNupkg("Dynamics.AX.ApplicationSuite.1.0.0.0.nupkg"), Is.EqualTo("applicationsuite"));
        Assert.That(PackageAnalyzer.ExtractModuleNameFromNupkg("dynamicsax-contosoapp.1.0.0.0.nupkg"), Is.EqualTo("contosoapp"));
    }

    [Test]
    public void ExtractModuleNameFromManagedZip_StripsSuffixAndVersion()
    {
        Assert.That(PackageAnalyzer.ExtractModuleNameFromManagedZip("cch_sureaddress_1_0_0_1_managed.zip"), Is.EqualTo("cch_sureaddress"));
        Assert.That(PackageAnalyzer.ExtractModuleNameFromManagedZip("somemodule_managed.zip"), Is.EqualTo("somemodule"));
    }

    [Test]
    public void DetectMergeStrategy_ReturnsLcs_WhenAllLcsOrMerged()
    {
        Assert.That(PackageAnalyzer.DetectMergeStrategy(new[] { "LCS", "LCS" }), Is.EqualTo("LCS"));
        Assert.That(PackageAnalyzer.DetectMergeStrategy(new[] { "LCS", "Merged" }), Is.EqualTo("LCS"));
    }

    [Test]
    public void DetectMergeStrategy_ReturnsUnified_WhenAllUnified()
    {
        Assert.That(PackageAnalyzer.DetectMergeStrategy(new[] { "Unified", "Unified" }), Is.EqualTo("Unified"));
    }

    [Test]
    public void DetectMergeStrategy_ReturnsNull_WhenMixed()
    {
        Assert.That(PackageAnalyzer.DetectMergeStrategy(new[] { "LCS", "Unified" }), Is.Null);
    }

    [Test]
    public void DetectLicenseFiles_ReturnsLcsLicense_WhenInAOSServiceScriptsLicense()
    {
        var zipPath = Path.Combine(_testDir, "withlic.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("AOSService/Scripts/License/MyLicense.txt");
            using (var w = e.Open())
            using (var sw = new StreamWriter(w))
                sw.Write("license");
        }
        var list = PackageAnalyzer.DetectLicenseFiles(zipPath);
        Assert.That(list, Does.Contain("MyLicense.txt"));
    }

    [Test]
    public void DetectLicenseFiles_ReturnsEmpty_WhenNoLicenses()
    {
        var zipPath = Path.Combine(_testDir, "nolic.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("Other/File.txt");
        }
        Assert.That(PackageAnalyzer.DetectLicenseFiles(zipPath), Is.Empty);
    }
}
