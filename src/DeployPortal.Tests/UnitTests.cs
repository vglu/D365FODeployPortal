using DeployPortal.Data;
using DeployPortal.Models;
using DeployPortal.Services;
using DeployPortal.Services.PackageContent;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace DeployPortal.Tests;

/// <summary>
/// Unit tests with mocks (IConvertService) and in-memory DB + test settings.
/// </summary>
[TestFixture]
public class UnitTests
{
    private string _testDir = "";
    private IDbContextFactory<AppDbContext> _dbFactory = null!;
    private Mock<IConvertService> _convertMock = null!;
    private Mock<IPackageChangeLogService> _changeLogMock = null!;
    private SettingsService _settings = null!;

    [SetUp]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"deploy-portal-unit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_testDir, "test.db")}")
            .Options;
        using (var db = new AppDbContext(options))
            db.Database.EnsureCreated();

        _dbFactory = new PooledDbContextFactory(options);

        _convertMock = new Mock<IConvertService>();
        _changeLogMock = new Mock<IPackageChangeLogService>();
        _changeLogMock.Setup(c => c.LogChangeAsync(It.IsAny<int>(), It.IsAny<PackageChangeType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeployPortal:TempWorkingDir"] = _testDir,
                ["DeployPortal:PackageStoragePath"] = Path.Combine(_testDir, "packages"),
                ["DeployPortal:ConverterEngine"] = "BuiltIn"
            })
            .Build();
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_testDir);
        var log = new Mock<ILogger<SettingsService>>();
        _settings = new SettingsService(config, env.Object, log.Object, _dbFactory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Test]
    public void SettingsService_MaxConcurrentDeployments_StoredInDatabase()
    {
        var all = _settings.GetAllSettings();
        Assert.That(all, Does.ContainKey("MaxConcurrentDeployments"));
        Assert.That(_settings.MaxConcurrentDeployments, Is.EqualTo(2), "Default when DB has no row");

        _settings.SaveSettings(new Dictionary<string, string>(all)
        {
            ["MaxConcurrentDeployments"] = "3"
        });
        Assert.That(_settings.MaxConcurrentDeployments, Is.EqualTo(3), "After save to DB");

        _settings.SaveSettings(new Dictionary<string, string>(all)
        {
            ["MaxConcurrentDeployments"] = "1"
        });
        Assert.That(_settings.MaxConcurrentDeployments, Is.EqualTo(1));
    }

    [Test]
    public void ConvertToUnifiedAsync_Throws_WhenPackageTypeIsUnified()
    {
        var logger = new Mock<ILogger<PackageService>>();
        var svc = new PackageService(
            _dbFactory,
            _settings,
            _convertMock.Object,
            _changeLogMock.Object,
            logger.Object);

        var pkg = new Package
        {
            Id = 1,
            Name = "Test",
            PackageType = "Unified",
            StoredFilePath = Path.Combine(_testDir, "x.zip")
        };
        File.WriteAllText(pkg.StoredFilePath, "dummy");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await svc.ConvertToUnifiedAsync(pkg));

        Assert.That(ex!.Message, Does.Contain("Unified"));
        _convertMock.Verify(c => c.ConvertToUnifiedAsync(It.IsAny<string>(), It.IsAny<Action<string>?>()), Times.Never);
    }

    [Test]
    public void ConvertToUnifiedAsync_Throws_WhenFileNotFound()
    {
        var logger = new Mock<ILogger<PackageService>>();
        var svc = new PackageService(
            _dbFactory,
            _settings,
            _convertMock.Object,
            _changeLogMock.Object,
            logger.Object);

        var pkg = new Package
        {
            Id = 1,
            Name = "Test",
            PackageType = "LCS",
            StoredFilePath = Path.Combine(_testDir, "nonexistent.zip")
        };

        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await svc.ConvertToUnifiedAsync(pkg));
        _convertMock.Verify(c => c.ConvertToUnifiedAsync(It.IsAny<string>(), It.IsAny<Action<string>?>()), Times.Never);
    }

    [Test]
    public async Task ConvertToUnifiedAsync_CallsConverter_AndSavesPackage()
    {
        var unifiedOutDir = Path.Combine(_testDir, "unified_out");
        Directory.CreateDirectory(unifiedOutDir);
        File.WriteAllText(Path.Combine(unifiedOutDir, "dummy.txt"), "content");

        _convertMock
            .Setup(c => c.ConvertToUnifiedAsync(It.IsAny<string>(), It.IsAny<Action<string>?>()))
            .ReturnsAsync(unifiedOutDir);

        var sourceZip = Path.Combine(_testDir, "source.zip");
        File.WriteAllText(sourceZip, "x");

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package
            {
                Id = 1,
                Name = "SourcePkg",
                OriginalFileName = "source.zip",
                StoredFilePath = sourceZip,
                PackageType = "LCS",
                UploadedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var logger = new Mock<ILogger<PackageService>>();
        var svc = new PackageService(
            _dbFactory,
            _settings,
            _convertMock.Object,
            _changeLogMock.Object,
            logger.Object);

        var sourcePkg = (await svc.GetByIdAsync(1))!;
        var result = await svc.ConvertToUnifiedAsync(sourcePkg);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("SourcePkg (Unified)"));
        Assert.That(result.PackageType, Is.EqualTo("Unified"));
        Assert.That(result.FileSizeBytes, Is.GreaterThan(0));
        Assert.That(result.LicenseFileNames, Is.Null, "Mock output has no license files");
        _convertMock.Verify(
            c => c.ConvertToUnifiedAsync(It.IsAny<string>(), It.IsAny<Action<string>?>()),
            Times.Once);

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var count = await db.Packages.CountAsync();
            Assert.That(count, Is.EqualTo(2));
        }
    }

    [Test]
    public void ConvertToLcsAsync_Throws_WhenPackageTypeIsLCS()
    {
        var logger = new Mock<ILogger<PackageService>>();
        var svc = new PackageService(
            _dbFactory,
            _settings,
            _convertMock.Object,
            _changeLogMock.Object,
            logger.Object);

        var pkg = new Package
        {
            Id = 1,
            Name = "Test",
            PackageType = "LCS",
            StoredFilePath = Path.Combine(_testDir, "x.zip")
        };
        File.WriteAllText(pkg.StoredFilePath, "dummy");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await svc.ConvertToLcsAsync(pkg));

        Assert.That(ex!.Message, Does.Contain("LCS"));
        _convertMock.Verify(c => c.ConvertToLcsAsync(It.IsAny<string>(), It.IsAny<Action<string>?>()), Times.Never);
    }

    [Test]
    public async Task ConvertToLcsAsync_CallsConverter_AndSavesPackage()
    {
        var lcsOutDir = Path.Combine(_testDir, "lcs_out");
        Directory.CreateDirectory(lcsOutDir);
        File.WriteAllText(Path.Combine(lcsOutDir, "HotfixInstallationInfo.xml"), "<root/>");

        _convertMock
            .Setup(c => c.ConvertToLcsAsync(It.IsAny<string>(), It.IsAny<Action<string>?>()))
            .ReturnsAsync(lcsOutDir);

        var sourceZip = Path.Combine(_testDir, "unified.zip");
        File.WriteAllText(sourceZip, "x");

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package
            {
                Id = 1,
                Name = "UnifiedPkg",
                OriginalFileName = "unified.zip",
                StoredFilePath = sourceZip,
                PackageType = "Unified",
                UploadedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var logger = new Mock<ILogger<PackageService>>();
        var svc = new PackageService(
            _dbFactory,
            _settings,
            _convertMock.Object,
            _changeLogMock.Object,
            logger.Object);

        var sourcePkg = (await svc.GetByIdAsync(1))!;
        var result = await svc.ConvertToLcsAsync(sourcePkg);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("UnifiedPkg (LCS)"));
        Assert.That(result.PackageType, Is.EqualTo("LCS"));
        Assert.That(result.LicenseFileNames, Is.Null, "Mock output has no license files");
        _convertMock.Verify(
            c => c.ConvertToLcsAsync(It.IsAny<string>(), It.IsAny<Action<string>?>()),
            Times.Once);
    }

    [Test]
    public async Task RefreshLicenseInfoAsync_UpdatesLicenseFileNames_WhenPackageHasLicenses()
    {
        var packagesDir = Path.Combine(_testDir, "packages");
        Directory.CreateDirectory(packagesDir);
        var zipPath = Path.Combine(packagesDir, "pkg.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var licenseEntry = zip.CreateEntry("AOSService/Scripts/License/MyLicense.txt");
            using (var w = licenseEntry.Open())
            using (var sw = new StreamWriter(w))
                sw.Write("license content");
        }

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package
            {
                Id = 1,
                Name = "PkgWithLicenses",
                OriginalFileName = "pkg.zip",
                StoredFilePath = zipPath,
                PackageType = "LCS",
                UploadedAt = DateTime.UtcNow,
                LicenseFileNames = null
            });
            await db.SaveChangesAsync();
        }

        var logger = new Mock<ILogger<PackageService>>();
        var svc = new PackageService(_dbFactory, _settings, _convertMock.Object, _changeLogMock.Object, logger.Object);
        await svc.RefreshLicenseInfoAsync(1);

        var pkg = await svc.GetByIdAsync(1);
        Assert.That(pkg, Is.Not.Null);
        Assert.That(pkg!.LicenseFileNames, Is.Not.Null.And.Contains("MyLicense.txt"));
    }

    [Test]
    public async Task UpdatePackageAsync_LogsNameAndTicketUrlChanges_ToChangeLog()
    {
        var packagesDir = Path.Combine(_testDir, "packages");
        Directory.CreateDirectory(packagesDir);
        var zipPath = Path.Combine(packagesDir, "pkg.zip");
        File.WriteAllText(zipPath, "dummy");

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package
            {
                Id = 1,
                Name = "OldName",
                OriginalFileName = "pkg.zip",
                StoredFilePath = zipPath,
                PackageType = "LCS",
                UploadedAt = DateTime.UtcNow,
                DevOpsTaskUrl = "https://old.url"
            });
            await db.SaveChangesAsync();
        }

        var logger = new Mock<ILogger<PackageService>>();
        var changeLogMock = new Mock<DeployPortal.Services.PackageContent.IPackageChangeLogService>();
        changeLogMock.Setup(c => c.LogChangeAsync(It.IsAny<int>(), It.IsAny<PackageChangeType>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);

        var svc = new PackageService(_dbFactory, _settings, _convertMock.Object, changeLogMock.Object, logger.Object);
        await svc.UpdatePackageAsync(1, "NewName", "https://new.url", "test-user");

        changeLogMock.Verify(c => c.LogChangeAsync(1, PackageChangeType.Updated, "Name", "NewName", "Previous: OldName", "test-user"), Times.Once);
        changeLogMock.Verify(c => c.LogChangeAsync(1, PackageChangeType.Updated, "TicketUrl", "https://new.url", "Previous: https://old.url", "test-user"), Times.Once);
    }
}

/// <summary>
/// Simple non-pooled factory for tests (EF in-memory doesn't need pooling).
/// </summary>
file class PooledDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public PooledDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

    public AppDbContext CreateDbContext() => new AppDbContext(_options);

    public async Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
        await Task.FromResult(CreateDbContext());
}
