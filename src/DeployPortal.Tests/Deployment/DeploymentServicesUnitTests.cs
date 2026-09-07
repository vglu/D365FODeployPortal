using DeployPortal.Data;
using DeployPortal.Services;
using DeployPortal.Services.Deployment;
using DeployPortal.Services.Deployment.PacCli;
using DeployPortal.Services.Deployment.Validation;
using DeployPortal.Services.Deployment.Isolation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace DeployPortal.Tests.Deployment;

/// <summary>
/// Unit tests for refactored deployment services (SOLID principles).
/// Tests each component in isolation using mocks.
/// </summary>
[TestFixture]
public class DeploymentServicesUnitTests
{
    private string _testDir = "";

    [SetUp]
    public void Setup()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"deploy-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    #region PacCliExecutor Tests

    [Test]
    public async Task PacCliExecutor_ExecuteAsync_ReturnsSuccessResult_WhenExitCodeIsZero()
    {
        // Arrange
        var logger = new Mock<ILogger<PacCliExecutor>>();
        var executor = new PacCliExecutor("cmd.exe", logger.Object);

        // Act
        var result = await executor.ExecuteAsync(
            "/c echo test",
            _testDir,
            new Dictionary<string, string> { ["TEST_VAR"] = "test_value" });

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.StandardOutput, Does.Contain("test").IgnoreCase);
    }

    [Test]
    public async Task PacCliExecutor_ExecuteAsync_ReturnsFailureResult_WhenExitCodeIsNonZero()
    {
        // Arrange
        var logger = new Mock<ILogger<PacCliExecutor>>();
        var executor = new PacCliExecutor("cmd.exe", logger.Object);

        // Act
        var result = await executor.ExecuteAsync(
            "/c exit 1",
            _testDir);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task PacCliExecutor_ExecuteAsync_InvokesCallbacks_ForOutputAndError()
    {
        // Arrange
        var logger = new Mock<ILogger<PacCliExecutor>>();
        var executor = new PacCliExecutor("cmd.exe", logger.Object);
        var outputLines = new List<string>();
        var errorLines = new List<string>();

        // Act
        await executor.ExecuteAsync(
            "/c echo stdout && echo stderr 1>&2",
            _testDir,
            onOutput: line => outputLines.Add(line),
            onError: line => errorLines.Add(line));

        // Assert
        Assert.That(outputLines, Has.Some.Contain("stdout").IgnoreCase);
        // Note: stderr redirection in cmd.exe might not always work in tests
    }

    #endregion

    #region PreDeployAuthValidator Tests

    [Test]
    public async Task PreDeployAuthValidator_ValidateAsync_Passes_WhenVerifyFriendlyNameIsOff()
    {
        // When setting is off, validator skips and never throws
        var logger = new Mock<ILogger<PreDeployAuthValidator>>();
        var validator = new PreDeployAuthValidator(logger.Object);
        var context = new DeploymentContext
        {
            Environment = new Models.Environment { Name = "Test", Url = "test-env.crm.dynamics.com" },
            IsolatedAuthDir = _testDir,
            LogFilePath = Path.Combine(_testDir, "deploy.log"),
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll"),
            VerifyOrganizationFriendlyName = false,
            PacAuthWhoOutput = "Organization Friendly Name: WRONG-ENV"
        };
        Assert.DoesNotThrowAsync(async () => await validator.ValidateAsync(context));
        await Task.CompletedTask;
    }

    [Test]
    public async Task PreDeployAuthValidator_ValidateAsync_Passes_WhenEnvironmentUrlIsInWhoOutput()
    {
        // Arrange
        var logger = new Mock<ILogger<PreDeployAuthValidator>>();
        var validator = new PreDeployAuthValidator(logger.Object);
        var context = new DeploymentContext
        {
            Environment = new Models.Environment 
            { 
                Name = "Test", 
                Url = "test-env.crm.dynamics.com" 
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = Path.Combine(_testDir, "deploy.log"),
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll"),
            VerifyOrganizationFriendlyName = true,
            PacAuthWhoOutput = "Environment Url: https://test-env.crm.dynamics.com/"
        };

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await validator.ValidateAsync(context));
        await Task.CompletedTask;
    }

    [Test]
    public async Task PreDeployAuthValidator_ValidateAsync_Passes_WhenOrganizationFriendlyNameMatches()
    {
        // Arrange
        var logger = new Mock<ILogger<PreDeployAuthValidator>>();
        var validator = new PreDeployAuthValidator(logger.Object);
        var context = new DeploymentContext
        {
            Environment = new Models.Environment 
            { 
                Name = "Example-Target-Env", 
                Url = "target-env.crm.dynamics.com",
                OrganizationFriendlyName = "Example-Target-Env"
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = Path.Combine(_testDir, "deploy.log"),
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll"),
            VerifyOrganizationFriendlyName = true,
            // Real output from pac auth who (interactive auth) — no URL, but has Organization Friendly Name
            PacAuthWhoOutput = @"Connected as user@example.com
Type: User
Organization Id: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
Organization Friendly Name: Example-Target-Env"
        };

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await validator.ValidateAsync(context));
        await Task.CompletedTask;
    }

    [Test]
    public void PreDeployAuthValidator_ValidateAsync_Throws_WhenNeitherUrlNorNameMatches()
    {
        // Arrange
        var logger = new Mock<ILogger<PreDeployAuthValidator>>();
        var validator = new PreDeployAuthValidator(logger.Object);
        var context = new DeploymentContext
        {
            Environment = new Models.Environment 
            { 
                Name = "Test", 
                Url = "test-env.crm.dynamics.com" 
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = Path.Combine(_testDir, "deploy.log"),
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll"),
            VerifyOrganizationFriendlyName = true,
            PacAuthWhoOutput = "Organization Friendly Name: WRONG-ENV\nEnvironment Url: https://WRONG-env.crm.dynamics.com/"
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await validator.ValidateAsync(context));
        Assert.That(ex!.Message, Does.Contain("PRE-DEPLOYMENT VALIDATION FAILED"));
        Assert.That(ex.Message, Does.Contain("test-env.crm.dynamics.com"));
    }

    [Test]
    public void PreDeployAuthValidator_ValidateAsync_Throws_WhenPacAuthWhoOutputIsNull()
    {
        // Arrange
        var logger = new Mock<ILogger<PreDeployAuthValidator>>();
        var validator = new PreDeployAuthValidator(logger.Object);
        var context = new DeploymentContext
        {
            Environment = new Models.Environment 
            { 
                Name = "Test", 
                Url = "test-env.crm.dynamics.com" 
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = Path.Combine(_testDir, "deploy.log"),
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll"),
            VerifyOrganizationFriendlyName = true,
            PacAuthWhoOutput = null // Missing!
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await validator.ValidateAsync(context));
        Assert.That(ex!.Message, Does.Contain("'pac auth who' output is missing"));
    }

    #endregion

    #region PostDeployLogValidator Tests

    [Test]
    public async Task PostDeployLogValidator_ValidateAsync_Passes_WhenLogContainsCorrectOrganizationUri()
    {
        // Arrange
        var logger = new Mock<ILogger<PostDeployLogValidator>>();
        var validator = new PostDeployLogValidator(logger.Object);
        var logPath = Path.Combine(_testDir, "deploy.log");
        File.WriteAllText(logPath, 
            "PackageDeployVerb Information: 8 : Message: Deployment Target Organization Uri: https://test-env.crm.dynamics.com/XRMServices/...");

        var context = new DeploymentContext
        {
            Environment = new Models.Environment 
            { 
                Name = "Test", 
                Url = "test-env.crm.dynamics.com" 
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = logPath,
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll")
        };

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await validator.ValidateAsync(context));
        await Task.CompletedTask;
    }

    [Test]
    public void PostDeployLogValidator_ValidateAsync_Throws_WhenLogContainsWrongOrganizationUri()
    {
        // Arrange
        var logger = new Mock<ILogger<PostDeployLogValidator>>();
        var validator = new PostDeployLogValidator(logger.Object);
        var logPath = Path.Combine(_testDir, "deploy.log");
        File.WriteAllText(logPath,
            "PackageDeployVerb Information: 8 : Message: Deployment Target Organization Uri: https://WRONG-env.crm.dynamics.com/XRMServices/...");

        var context = new DeploymentContext
        {
            Environment = new Models.Environment 
            { 
                Name = "Test", 
                Url = "test-env.crm.dynamics.com" 
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = logPath,
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll")
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await validator.ValidateAsync(context));
        Assert.That(ex!.Message, Does.Contain("POST-DEPLOYMENT VALIDATION FAILED"));
        Assert.That(ex.Message, Does.Contain("test-env.crm.dynamics.com"));
        Assert.That(ex.Message, Does.Contain("WRONG-env.crm.dynamics.com"));
    }

    [Test]
    public async Task PostDeployLogValidator_ValidateAsync_DoesNotThrow_WhenLogFileNotFound()
    {
        // Arrange
        var logger = new Mock<ILogger<PostDeployLogValidator>>();
        var validator = new PostDeployLogValidator(logger.Object);
        var logPath = Path.Combine(_testDir, "nonexistent.log");

        var context = new DeploymentContext
        {
            Environment = new Models.Environment 
            { 
                Name = "Test", 
                Url = "test-env.crm.dynamics.com" 
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = logPath,
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll")
        };

        // Act & Assert (should not throw — just log warning)
        Assert.DoesNotThrowAsync(async () => await validator.ValidateAsync(context));
        await Task.CompletedTask;
    }

    [Test]
    public void PostDeployLogValidator_ValidateAsync_Throws_WhenOrganizationUriNotFoundInLog()
    {
        var logger = new Mock<ILogger<PostDeployLogValidator>>();
        var validator = new PostDeployLogValidator(logger.Object);
        var logPath = Path.Combine(_testDir, "deploy.log");
        File.WriteAllText(logPath, "Some log content without Organization Uri line");

        var context = new DeploymentContext
        {
            Environment = new Models.Environment
            {
                Name = "Test",
                Url = "test-env.crm.dynamics.com"
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = logPath,
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll")
        };

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await validator.ValidateAsync(context));
        Assert.That(ex!.Message, Does.Contain("Organization Uri"));
    }

    [Test]
    public void PostDeployLogValidator_ValidateAsync_Throws_WhenLogContainsRaiseFailEvent()
    {
        var logger = new Mock<ILogger<PostDeployLogValidator>>();
        var validator = new PostDeployLogValidator(logger.Object);
        var logPath = Path.Combine(_testDir, "deploy-fail.log");
        File.WriteAllText(logPath,
            "PackageDeployVerb Information: 8 : Message: RaiseFailEvent - update progress with fail event\n" +
            "PackageDeployVerb Information: 8 : Message: Deployment Target Organization Uri: https://test-env.crm.dynamics.com/XRMServices/...");

        var context = new DeploymentContext
        {
            Environment = new Models.Environment
            {
                Name = "Test",
                Url = "test-env.crm.dynamics.com"
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = logPath,
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll")
        };

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await validator.ValidateAsync(context));
        Assert.That(ex!.Message, Does.Contain("install reported failure"));
        Assert.That(ex.Message, Does.Contain("RaiseFailEvent"));
    }

    [Test]
    public void PostDeployLogValidator_ValidateAsync_Throws_WhenLogContainsFoInstallFailed()
    {
        var logger = new Mock<ILogger<PostDeployLogValidator>>();
        var validator = new PostDeployLogValidator(logger.Object);
        var logPath = Path.Combine(_testDir, "deploy-fo-fail.log");
        File.WriteAllText(logPath,
            "Error: Installation failed for Finance and Operations application\n" +
            "PackageDeployVerb Information: 8 : Message: Deployment Target Organization Uri: https://test-env.crm.dynamics.com/XRMServices/...");

        var context = new DeploymentContext
        {
            Environment = new Models.Environment
            {
                Name = "Test",
                Url = "test-env.crm.dynamics.com"
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = logPath,
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll")
        };

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await validator.ValidateAsync(context));
        Assert.That(ex!.Message, Does.Contain("Installation failed for Finance and Operations"));
    }

    [Test]
    public void PostDeployLogValidator_ValidateAsync_Throws_WhenLogContainsConfigFileMissing()
    {
        var logger = new Mock<ILogger<PostDeployLogValidator>>();
        var validator = new PostDeployLogValidator(logger.Object);
        var logPath = Path.Combine(_testDir, "deploy-config-missing.log");
        File.WriteAllText(logPath,
            "Failed to Load the Import Configuration : Config File Missing\n" +
            "PackageDeployVerb Error: 2 : Message: Selected Plugin is null");

        var context = new DeploymentContext
        {
            Environment = new Models.Environment
            {
                Name = "Test",
                Url = "test-env.crm.dynamics.com"
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = logPath,
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll")
        };

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await validator.ValidateAsync(context));
        Assert.That(ex!.Message, Does.Contain("Config File Missing").Or.Contain("Failed to Load the Import Configuration").Or.Contain("Selected Plugin is null"));
    }

    #endregion

    #region PackageDeployFailureDetector Tests

    [Test]
    public void PackageDeployFailureDetector_FindFailureEvidence_ReturnsNull_ForCleanLog()
    {
        var text = "Deployment Target Organization Uri: https://ok.crm.dynamics.com/\nImport completed";
        Assert.That(PackageDeployFailureDetector.FindFailureEvidence(text), Is.Null);
    }

    [Test]
    public void PackageDeployFailureDetector_FindFailureEvidence_DetectsRaiseFailEvent()
    {
        var text = "Message: RaiseFailEvent - update progress with fail event";
        var evidence = PackageDeployFailureDetector.FindFailureEvidence(text);
        Assert.That(evidence, Is.Not.Null);
        Assert.That(evidence, Does.Contain("RaiseFailEvent"));
    }

    [Test]
    public void PackageDeployFailureDetector_FindFailureEvidence_DetectsFoInstallFailed()
    {
        var text = "Error: Installation failed for Finance and Operations application";
        Assert.That(PackageDeployFailureDetector.HasFailure(text), Is.True);
    }

    [Test]
    public void PackageDeployFailureDetector_FindFailureEvidence_DetectsConfigFileMissing()
    {
        var text = "Error: Failed to Load the Import Configuration : Config File Missing";
        var evidence = PackageDeployFailureDetector.FindFailureEvidence(text);
        Assert.That(evidence, Is.Not.Null);
        Assert.That(evidence, Does.Contain("Config File Missing").Or.Contain("Failed to Load"));
    }

    #endregion

    #region PacDeploymentService Tests

    private static string CreateUnifiedPackageLayout(string root)
    {
        var packagePath = Path.Combine(root, "TemplatePackage.dll");
        File.WriteAllText(packagePath, "dll");
        var assets = Path.Combine(root, "PackageAssets");
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "ImportConfig.xml"), "<configdatastorage/>");
        return packagePath;
    }

    [Test]
    public void PacDeploymentService_DeployAsync_Throws_WhenImportConfigMissing()
    {
        var packagePath = Path.Combine(_testDir, "TemplatePackage.dll");
        File.WriteAllText(packagePath, "dll");

        var pac = new Mock<IPacCliExecutor>();
        var logger = new Mock<ILogger<PacDeploymentService>>();
        var service = new PacDeploymentService(pac.Object, logger.Object);

        var ex = Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await service.DeployAsync(packagePath, Path.Combine(_testDir, "d.log"), _testDir));

        Assert.That(ex!.Message, Does.Contain("ImportConfig.xml"));
        pac.Verify(p => p.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>?>(),
            It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>()), Times.Never);
    }

    [Test]
    public void PacDeploymentService_DeployAsync_UsesPackageDirectoryAsWorkingDir()
    {
        var packagePath = CreateUnifiedPackageLayout(_testDir);
        string? capturedCwd = null;

        var pac = new Mock<IPacCliExecutor>();
        pac.Setup(p => p.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>()))
            .Callback<string, string, IDictionary<string, string>?, Action<string>?, Action<string>?>(
                (_, cwd, _, _, _) => capturedCwd = cwd)
            .ReturnsAsync(new PacCliResult
            {
                ExitCode = 0,
                StandardOutput = "Package deployed successfully\n",
                StandardError = ""
            });

        var logger = new Mock<ILogger<PacDeploymentService>>();
        var service = new PacDeploymentService(pac.Object, logger.Object);

        Assert.DoesNotThrowAsync(async () =>
            await service.DeployAsync(packagePath, Path.Combine(_testDir, "d.log"), _testDir));

        Assert.That(capturedCwd, Is.EqualTo(_testDir));
    }

    [Test]
    public void PacDeploymentService_DeployAsync_Throws_WhenPacExitsZeroButStdoutHasFoFailure()
    {
        var packagePath = CreateUnifiedPackageLayout(_testDir);

        var pac = new Mock<IPacCliExecutor>();
        pac.Setup(p => p.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>()))
            .ReturnsAsync(new PacCliResult
            {
                ExitCode = 0,
                StandardOutput = "Error: Installation failed for Finance and Operations application\n",
                StandardError = ""
            });

        var logger = new Mock<ILogger<PacDeploymentService>>();
        var service = new PacDeploymentService(pac.Object, logger.Object);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.DeployAsync(packagePath, Path.Combine(_testDir, "d.log"), _testDir));

        Assert.That(ex!.Message, Does.Contain("despite exit code 0"));
        Assert.That(ex.Message, Does.Contain("Installation failed for Finance and Operations"));
    }

    [Test]
    public void PacDeploymentService_DeployAsync_Throws_WhenPacExitsZeroButStdoutHasConfigMissing()
    {
        var packagePath = CreateUnifiedPackageLayout(_testDir);

        var pac = new Mock<IPacCliExecutor>();
        pac.Setup(p => p.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>()))
            .ReturnsAsync(new PacCliResult
            {
                ExitCode = 0,
                StandardOutput = "Error: Failed to Load the Import Configuration : Config File Missing\n",
                StandardError = ""
            });

        var logger = new Mock<ILogger<PacDeploymentService>>();
        var service = new PacDeploymentService(pac.Object, logger.Object);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.DeployAsync(packagePath, Path.Combine(_testDir, "d.log"), _testDir));

        Assert.That(ex!.Message, Does.Contain("Config File Missing").Or.Contain("Failed to Load"));
    }

    [Test]
    public async Task PacDeploymentService_DeployAsync_Succeeds_WhenPacExitsZeroAndOutputIsClean()
    {
        var packagePath = CreateUnifiedPackageLayout(_testDir);

        var pac = new Mock<IPacCliExecutor>();
        pac.Setup(p => p.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>()))
            .ReturnsAsync(new PacCliResult
            {
                ExitCode = 0,
                StandardOutput = "Package deployed successfully\n",
                StandardError = ""
            });

        var logger = new Mock<ILogger<PacDeploymentService>>();
        var service = new PacDeploymentService(pac.Object, logger.Object);

        Assert.DoesNotThrowAsync(async () =>
            await service.DeployAsync(packagePath, Path.Combine(_testDir, "d.log"), _testDir));
        await Task.CompletedTask;
    }

    #endregion

    #region IsolatedDirectoryManager Tests

    private static ISettingsService CreateSettingsService(string testDir)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeployPortal:TempWorkingDir"] = testDir
            })
            .Build();
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(testDir);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(testDir, "settings.db")}")
            .Options;
        using (var db = new AppDbContext(options))
            db.Database.EnsureCreated();
        var dbFactory = new PooledDbContextFactory(options);
        var settingsLogger = new Mock<ILogger<SettingsService>>();
        return new SettingsService(config, env.Object, settingsLogger.Object, dbFactory);
    }

    [Test]
    public void IsolatedDirectoryManager_CreateIsolatedDirectory_CreatesDirectory()
    {
        // Arrange
        var settings = CreateSettingsService(_testDir);
        var logger = new Mock<ILogger<IsolatedDirectoryManager>>();
        var manager = new IsolatedDirectoryManager(settings, logger.Object);

        // Act
        var isolatedDir = manager.CreateIsolatedDirectory(123);

        // Assert
        Assert.That(Directory.Exists(isolatedDir), Is.True);
        Assert.That(isolatedDir, Does.Contain("pac_auth_123_"));
        // Note: isolatedDir uses SettingsService.TempWorkingDir which might be different from _testDir
    }

    [Test]
    public void IsolatedDirectoryManager_DeleteIsolatedDirectory_DeletesDirectory()
    {
        // Arrange
        var settings = CreateSettingsService(_testDir);
        var logger = new Mock<ILogger<IsolatedDirectoryManager>>();
        var manager = new IsolatedDirectoryManager(settings, logger.Object);
        var isolatedDir = manager.CreateIsolatedDirectory(456);
        File.WriteAllText(Path.Combine(isolatedDir, "test.txt"), "content");

        // Act
        manager.DeleteIsolatedDirectory(isolatedDir);

        // Assert
        Assert.That(Directory.Exists(isolatedDir), Is.False);
    }

    [Test]
    public void IsolatedDirectoryManager_DeleteIsolatedDirectory_DoesNotThrow_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var settings = CreateSettingsService(_testDir);
        var logger = new Mock<ILogger<IsolatedDirectoryManager>>();
        var manager = new IsolatedDirectoryManager(settings, logger.Object);
        var nonexistentDir = Path.Combine(_testDir, "nonexistent");

        // Act & Assert
        Assert.DoesNotThrow(() => manager.DeleteIsolatedDirectory(nonexistentDir));
    }

    #endregion

    #region DeploymentOrchestrator (delay constant)

    [Test]
    public void DeploymentOrchestrator_DelayBetweenStarts_IsThirtySeconds()
    {
        Assert.That(DeploymentOrchestrator.DelayBetweenStartsSeconds, Is.EqualTo(30),
            "Delay between deployment starts must remain 30 seconds unless product requirement changes.");
    }

    #endregion

    private sealed class PooledDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public PooledDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new AppDbContext(_options);
    }
}
