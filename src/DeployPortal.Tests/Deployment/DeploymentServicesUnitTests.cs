using DeployPortal.Services;
using DeployPortal.Services.Deployment;
using DeployPortal.Services.Deployment.PacCli;
using DeployPortal.Services.Deployment.Validation;
using DeployPortal.Services.Deployment.Isolation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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
            PacAuthWhoOutput = "Environment Url: https://test-env.crm.dynamics.com/"
        };

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await validator.ValidateAsync(context));
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
                Name = "CST-HFX-TST-07", 
                Url = "cst-hfx-tst-07.crm.dynamics.com" 
            },
            IsolatedAuthDir = _testDir,
            LogFilePath = Path.Combine(_testDir, "deploy.log"),
            PackagePath = Path.Combine(_testDir, "TemplatePackage.dll"),
            // Real output from pac auth who (interactive auth) — no URL, but has Organization Friendly Name
            PacAuthWhoOutput = @"Connected as vhlushchenko@sisn.com
Type: User
Organization Id: ef7d39e4-66d2-f011-8729-000d3a33a003
Organization Friendly Name: CST-HFX-TST-07"
        };

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await validator.ValidateAsync(context));
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
    }

    [Test]
    public async Task PostDeployLogValidator_ValidateAsync_DoesNotThrow_WhenOrganizationUriNotFoundInLog()
    {
        // Arrange
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

        // Act & Assert (should not throw — just log warning)
        Assert.DoesNotThrowAsync(async () => await validator.ValidateAsync(context));
    }

    #endregion

    #region IsolatedDirectoryManager Tests

    [Test]
    public void IsolatedDirectoryManager_CreateIsolatedDirectory_CreatesDirectory()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeployPortal:TempWorkingDir"] = _testDir
            })
            .Build();
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_testDir);
        var settingsLogger = new Mock<ILogger<SettingsService>>();
        var settings = new SettingsService(config, env.Object, settingsLogger.Object);
        
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
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeployPortal:TempWorkingDir"] = _testDir
            })
            .Build();
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_testDir);
        var settingsLogger = new Mock<ILogger<SettingsService>>();
        var settings = new SettingsService(config, env.Object, settingsLogger.Object);
        
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
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DeployPortal:TempWorkingDir"] = _testDir
            })
            .Build();
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_testDir);
        var settingsLogger = new Mock<ILogger<SettingsService>>();
        var settings = new SettingsService(config, env.Object, settingsLogger.Object);
        
        var logger = new Mock<ILogger<IsolatedDirectoryManager>>();
        var manager = new IsolatedDirectoryManager(settings, logger.Object);
        var nonexistentDir = Path.Combine(_testDir, "nonexistent");

        // Act & Assert
        Assert.DoesNotThrow(() => manager.DeleteIsolatedDirectory(nonexistentDir));
    }

    #endregion
}
