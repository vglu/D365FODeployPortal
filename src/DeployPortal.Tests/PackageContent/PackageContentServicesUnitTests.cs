using System.IO.Compression;
using DeployPortal.Data;
using DeployPortal.Models;
using DeployPortal.Services.PackageContent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace DeployPortal.Tests.PackageContent;

[TestFixture]
public class PackageContentServicesUnitTests
{
    private string _testDir = null!;
    private IDbContextFactory<AppDbContext> _dbFactory = null!;

    [SetUp]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pkg-content-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        var dbPath = Path.Combine(_testDir, "test.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        using (var db = new AppDbContext(options))
            db.Database.EnsureCreated();
        _dbFactory = new PooledDbContextFactory(options);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    #region PackageChangeLogService

    [Test]
    public async Task PackageChangeLogService_LogChangeAsync_AddsEntryToDatabase()
    {
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package
            {
                Name = "TestPkg",
                OriginalFileName = "pkg.zip",
                StoredFilePath = "/tmp/pkg.zip",
                PackageType = "LCS"
            });
            await db.SaveChangesAsync();
        }

        var logger = new Mock<ILogger<PackageChangeLogService>>();
        var service = new PackageChangeLogService(_dbFactory, logger.Object);

        await service.LogChangeAsync(1, PackageChangeType.Added, "Model", "ApplicationSuite", "1.0.0", "test@user");

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var log = await db.PackageChangeLogs.FirstOrDefaultAsync(c => c.PackageId == 1);
            Assert.That(log, Is.Not.Null);
            Assert.That(log!.ChangeType, Is.EqualTo(PackageChangeType.Added));
            Assert.That(log.ItemType, Is.EqualTo("Model"));
            Assert.That(log.ItemName, Is.EqualTo("ApplicationSuite"));
            Assert.That(log.ChangedBy, Is.EqualTo("test@user"));
        }
    }

    [Test]
    public async Task PackageChangeLogService_GetChangeHistoryAsync_ReturnsOrderedByDateDesc()
    {
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package { Name = "P", OriginalFileName = "p.zip", StoredFilePath = "/p", PackageType = "LCS" });
            await db.SaveChangesAsync();
            db.PackageChangeLogs.AddRange(
                new PackageChangeLog { PackageId = 1, ChangeType = PackageChangeType.Added, ItemType = "Model", ItemName = "A", ChangedAt = DateTime.UtcNow.AddHours(-2) },
                new PackageChangeLog { PackageId = 1, ChangeType = PackageChangeType.Removed, ItemType = "License", ItemName = "l.txt", ChangedAt = DateTime.UtcNow.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        var logger = new Mock<ILogger<PackageChangeLogService>>();
        var service = new PackageChangeLogService(_dbFactory, logger.Object);
        var history = await service.GetChangeHistoryAsync(1, 10);

        Assert.That(history.Count, Is.EqualTo(2));
        Assert.That(history[0].ItemName, Is.EqualTo("l.txt"));
        Assert.That(history[1].ItemName, Is.EqualTo("A"));
    }

    #endregion

    #region PackageModificationService ValidateModelRemoval

    [Test]
    public async Task PackageModificationService_ValidateModelRemovalAsync_ReturnsCanRemoveTrue_WhenNoDependents()
    {
        var contentService = new Mock<IPackageContentService>();
        contentService.Setup(x => x.GetModelsAsync(1)).ReturnsAsync(new List<ModelInfo>
        {
            new() { Name = "App", Dependencies = new List<string>() },
            new() { Name = "Custom", Dependencies = new List<string> { "App" } }
        });
        var changeLogService = new Mock<IPackageChangeLogService>();
        var logger = new Mock<ILogger<PackageModificationService>>();

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package { Name = "P", OriginalFileName = "p.zip", StoredFilePath = "/p", PackageType = "LCS" });
            await db.SaveChangesAsync();
        }

        var modificationService = new PackageModificationService(_dbFactory, contentService.Object, changeLogService.Object, logger.Object);
        var (canRemove, dependents) = await modificationService.ValidateModelRemovalAsync(1, "Custom");

        Assert.That(canRemove, Is.True);
        Assert.That(dependents, Is.Empty);
    }

    [Test]
    public async Task PackageModificationService_ValidateModelRemovalAsync_ReturnsCanRemoveFalse_WhenDependentsExist()
    {
        var contentService = new Mock<IPackageContentService>();
        contentService.Setup(x => x.GetModelsAsync(1)).ReturnsAsync(new List<ModelInfo>
        {
            new() { Name = "Base", Dependencies = new List<string>() },
            new() { Name = "Extension", Dependencies = new List<string> { "Base" } }
        });
        var changeLogService = new Mock<IPackageChangeLogService>();
        var logger = new Mock<ILogger<PackageModificationService>>();

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package { Name = "P", OriginalFileName = "p.zip", StoredFilePath = "/p", PackageType = "LCS" });
            await db.SaveChangesAsync();
        }

        var modificationService = new PackageModificationService(_dbFactory, contentService.Object, changeLogService.Object, logger.Object);
        var (canRemove, dependents) = await modificationService.ValidateModelRemovalAsync(1, "Base");

        Assert.That(canRemove, Is.False);
        Assert.That(dependents, Has.One.Items.EqualTo("Extension"));
    }

    #endregion

    #region PackageContentService GetModelsAsync — LCS and Unified

    [Test]
    public async Task GetModelsAsync_LcsPackage_ReturnsModelsFromAOSServicePackages()
    {
        var lcsZipPath = Path.Combine(_testDir, "lcs.zip");
        using (var zip = ZipFile.Open(lcsZipPath, ZipArchiveMode.Create))
        {
            CreateEntry(zip, "AOSService/Packages/files/dynamicsax-ApplicationSuite.1.0.0.0.nupkg", "nupkg content");
            CreateEntry(zip, "AOSService/Packages/files/dynamicsax-CustomModel.2021.4.1.1.nupkg", "nupkg2");
        }

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package
            {
                Name = "LCS Pkg",
                OriginalFileName = "lcs.zip",
                StoredFilePath = lcsZipPath,
                PackageType = "LCS"
            });
            await db.SaveChangesAsync();
        }

        var logger = new Mock<ILogger<PackageContentService>>();
        var service = new PackageContentService(_dbFactory, logger.Object);
        var models = await service.GetModelsAsync(1);

        Assert.That(models, Has.Count.EqualTo(2));
        var names = models.Select(m => m.Name).OrderBy(x => x).ToList();
        Assert.That(names[0], Is.EqualTo("applicationsuite"));
        Assert.That(names[1], Is.EqualTo("custommodel"));
    }

    [Test]
    public async Task GetModelsAsync_UnifiedPackage_ReturnsModelsFromManagedZips()
    {
        var unifiedZipPath = Path.Combine(_testDir, "unified.zip");
        using (var zip = ZipFile.Open(unifiedZipPath, ZipArchiveMode.Create))
        {
            CreateEntry(zip, "ApplicationSuite_1_0_0_1_managed.zip", "managed content");
            CreateEntry(zip, "CustomExtension_1_0_0_1_managed.zip", "managed content 2");
            CreateEntry(zip, "DefaultDevSolution_1_0_0_0_managed.zip", "skip");
        }

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package
            {
                Name = "Unified Pkg",
                OriginalFileName = "unified.zip",
                StoredFilePath = unifiedZipPath,
                PackageType = "Unified"
            });
            await db.SaveChangesAsync();
        }

        var logger = new Mock<ILogger<PackageContentService>>();
        var service = new PackageContentService(_dbFactory, logger.Object);
        var models = await service.GetModelsAsync(1);

        Assert.That(models, Has.Count.EqualTo(2));
        var namesLower = models.Select(m => m.Name.ToLowerInvariant()).OrderBy(x => x).ToList();
        Assert.That(namesLower, Is.EquivalentTo(new[] { "applicationsuite", "customextension" }));
    }

    [Test]
    public async Task GetModelsAsync_MergedPackage_UsesLcsLogic()
    {
        var zipPath = Path.Combine(_testDir, "merged.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            CreateEntry(zip, "AOSService/Packages/dynamicsax-MergedModel.1.0.0.0.nupkg", "x");
        }

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Packages.Add(new Package
            {
                Name = "Merged Pkg",
                OriginalFileName = "merged.zip",
                StoredFilePath = zipPath,
                PackageType = "Merged"
            });
            await db.SaveChangesAsync();
        }

        var logger = new Mock<ILogger<PackageContentService>>();
        var service = new PackageContentService(_dbFactory, logger.Object);
        var models = await service.GetModelsAsync(1);

        Assert.That(models, Has.Count.EqualTo(1));
        Assert.That(models[0].Name, Is.EqualTo("mergedmodel"));
    }

    private static void CreateEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    #endregion

    private sealed class PooledDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public PooledDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new AppDbContext(_options);
        public async Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            await Task.FromResult(CreateDbContext());
    }
}
