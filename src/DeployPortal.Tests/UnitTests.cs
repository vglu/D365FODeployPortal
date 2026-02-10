using DeployPortal.Data;
using DeployPortal.Models;
using DeployPortal.Services;
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
        _settings = new SettingsService(config, env.Object, log.Object);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Test]
    public void ConvertToUnifiedAsync_Throws_WhenPackageTypeIsUnified()
    {
        var logger = new Mock<ILogger<PackageService>>();
        var svc = new PackageService(
            _dbFactory,
            _settings,
            _convertMock.Object,
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
            logger.Object);

        var sourcePkg = (await svc.GetByIdAsync(1))!;
        var result = await svc.ConvertToUnifiedAsync(sourcePkg);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("SourcePkg (Unified)"));
        Assert.That(result.PackageType, Is.EqualTo("Unified"));
        Assert.That(result.FileSizeBytes, Is.GreaterThan(0));
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
            logger.Object);

        var sourcePkg = (await svc.GetByIdAsync(1))!;
        var result = await svc.ConvertToLcsAsync(sourcePkg);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("UnifiedPkg (LCS)"));
        Assert.That(result.PackageType, Is.EqualTo("LCS"));
        _convertMock.Verify(
            c => c.ConvertToLcsAsync(It.IsAny<string>(), It.IsAny<Action<string>?>()),
            Times.Once);
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
