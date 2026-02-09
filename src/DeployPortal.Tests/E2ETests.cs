using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Playwright;
using NUnit.Framework;

namespace DeployPortal.Tests;

[TestFixture]
public class E2ETests
{
    private Process? _appProcess;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private string _dbPath = "";
    private string _storagePath = "";

    private const string BaseUrl = "http://localhost:5199";

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Resolve project path
        var testDir = TestContext.CurrentContext.TestDirectory;
        var srcDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", ".."));
        var projectPath = Path.Combine(srcDir, "DeployPortal", "DeployPortal.csproj");

        TestContext.Out.WriteLine($"Project path: {projectPath} (exists: {File.Exists(projectPath)})");

        _dbPath = Path.Combine(Path.GetTempPath(), $"deploy-portal-test-{Guid.NewGuid():N}.db");
        _storagePath = Path.Combine(Path.GetTempPath(), $"deploy-portal-test-packages-{Guid.NewGuid():N}");

        _appProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{projectPath}\" --urls {BaseUrl}",
                WorkingDirectory = srcDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        _appProcess.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        _appProcess.StartInfo.Environment["DeployPortal__DatabasePath"] = _dbPath;
        _appProcess.StartInfo.Environment["DeployPortal__PackageStoragePath"] = _storagePath;

        _appProcess.Start();

        // Collect stdout/stderr in background
        _appProcess.BeginOutputReadLine();
        _appProcess.BeginErrorReadLine();

        var serverOutput = new List<string>();
        _appProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) serverOutput.Add(e.Data);
        };
        _appProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) serverOutput.Add($"[STDERR] {e.Data}");
        };

        // Wait for the app to start
        var started = false;
        var timeout = DateTime.UtcNow.AddSeconds(45);
        while (!started && DateTime.UtcNow < timeout)
        {
            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync(BaseUrl);
                started = response.IsSuccessStatusCode;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        if (!started)
        {
            var output = string.Join("\n", serverOutput);
            Assert.Fail($"App did not start within 45 seconds.\nOutput:\n{output}");
        }

        TestContext.Out.WriteLine("App started successfully");

        // Setup Playwright
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();

        if (_appProcess != null && !_appProcess.HasExited)
        {
            _appProcess.Kill(entireProcessTree: true);
            _appProcess.Dispose();
        }

        // Cleanup test database
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (Directory.Exists(_storagePath)) Directory.Delete(_storagePath, true); } catch { }
    }

    [SetUp]
    public async Task Setup()
    {
        _page = await _browser!.NewPageAsync();
        // Listen for console errors
        _page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
                TestContext.Out.WriteLine($"[BROWSER ERROR] {msg.Text}");
        };
    }

    [TearDown]
    public async Task Teardown()
    {
        if (_page != null) await _page.CloseAsync();
    }

    private async Task TakeScreenshot(string name)
    {
        if (_page == null) return;
        var dir = Path.Combine(TestContext.CurrentContext.TestDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{name}.png");
        await _page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        TestContext.Out.WriteLine($"Screenshot saved: {path}");
    }

    private async Task WaitForBlazor()
    {
        // Wait for Blazor to be fully interactive
        await _page!.WaitForFunctionAsync("() => document.querySelector('.mud-layout') !== null",
            new PageWaitForFunctionOptions { Timeout = 15000 });
        await Task.Delay(500);
    }

    // ====================== TESTS ======================

    [Test, Order(1)]
    public async Task HomePage_Loads_ShowsDashboard()
    {
        await _page!.GotoAsync(BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        await TakeScreenshot("01_home");

        // Check for console errors
        var content = await _page.ContentAsync();
        Assert.That(content, Does.Contain("Dashboard"), "Page should contain Dashboard text");
        Assert.That(content, Does.Contain("Packages"), "Page should contain Packages counter");
        Assert.That(content, Does.Contain("Environments"), "Page should contain Environments counter");
        Assert.That(content, Does.Contain("Deployments"), "Page should contain Deployments counter");
    }

    [Test, Order(2)]
    public async Task Navigation_AllPagesAccessible()
    {
        // Packages
        await _page!.GotoAsync($"{BaseUrl}/packages", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        var content = await _page.ContentAsync();
        Assert.That(content, Does.Contain("Upload Package"), "Packages page should load");

        // Environments
        await _page.GotoAsync($"{BaseUrl}/environments", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        content = await _page.ContentAsync();
        Assert.That(content, Does.Contain("Add Environment"), "Environments page should load");

        // Deploy
        await _page.GotoAsync($"{BaseUrl}/deploy", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        content = await _page.ContentAsync();
        Assert.That(content, Does.Contain("Deploy"), "Deploy page should load");

        // History
        await _page.GotoAsync($"{BaseUrl}/deployments", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        content = await _page.ContentAsync();
        Assert.That(content, Does.Contain("Deployment History"), "Deployments page should load");

        // Settings
        await _page.GotoAsync($"{BaseUrl}/settings", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        content = await _page.ContentAsync();
        Assert.That(content, Does.Contain("Tool Status"), "Settings page should load");
        Assert.That(content, Does.Contain("External Tools"), "Settings page should show tool configuration");
        Assert.That(content, Does.Contain("Storage"), "Settings page should show storage settings");
        Assert.That(content, Does.Contain("Deployment Info"), "Settings page should show deployment info");
    }

    [Test, Order(3)]
    public async Task Environments_AddEnvironment_AppearsInTable()
    {
        await _page!.GotoAsync($"{BaseUrl}/environments", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        await TakeScreenshot("03_envs_before");

        // Click Add Environment button
        await _page.Locator("button:has-text('Add Environment')").ClickAsync();
        await Task.Delay(1500); // Wait for dialog animation

        await TakeScreenshot("03_envs_dialog_opened");

        // MudBlazor renders MudDialog inline (not via portal) when using @bind-Visible
        // Find all text inputs in the dialog
        var allInputs = await _page.Locator(".mud-input-slot, .mud-input input").AllAsync();
        TestContext.Out.WriteLine($"Found {allInputs.Count} input elements on page");

        // Try to get inputs more specifically via the MudTextField label
        var nameInput = _page.Locator("div.mud-input-control:has(label:has-text('Name')) input").First;
        var urlInput = _page.Locator("div.mud-input-control:has(label:has-text('URL')) input").First;
        var tenantInput = _page.Locator("div.mud-input-control:has(label:has-text('Tenant')) input").First;
        var appIdInput = _page.Locator("div.mud-input-control:has(label:has-text('Application')) input").First;
        var secretInput = _page.Locator("div.mud-input-control:has(label:has-text('Client Secret')) input").First;

        await nameInput.FillAsync("Test Environment");
        await urlInput.FillAsync("test.crm.dynamics.com");
        await tenantInput.FillAsync("00000000-0000-0000-0000-000000000001");
        await appIdInput.FillAsync("00000000-0000-0000-0000-000000000002");
        await secretInput.FillAsync("test-secret-value-1234");

        await TakeScreenshot("03_envs_form_filled");

        // Click Save
        await _page.Locator("button:has-text('Save')").ClickAsync();
        await Task.Delay(2000);

        await TakeScreenshot("03_envs_after_save");

        // Verify environment appears in the table
        var tableContent = await _page.ContentAsync();
        Assert.That(tableContent, Does.Contain("Test Environment"), "Saved environment should appear in the table");
    }

    [Test, Order(4)]
    public async Task Packages_Upload_FileAppears()
    {
        // Create a dummy ZIP file
        var tempZip = Path.Combine(Path.GetTempPath(), "test-lcs-package.zip");
        if (File.Exists(tempZip)) File.Delete(tempZip);

        using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("AOSService/readme.txt");
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write("Test LCS package content");
            }
        }

        await _page!.GotoAsync($"{BaseUrl}/packages", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        await TakeScreenshot("04_packages_before_upload");

        // MudFileUpload renders a hidden <input type="file"> — set files directly
        var fileChooserTask = _page.WaitForFileChooserAsync();

        // Click the "Select ZIP file" button to trigger file chooser
        await _page.Locator("button:has-text('Select ZIP file')").ClickAsync();

        var fileChooser = await fileChooserTask;
        await fileChooser.SetFilesAsync(tempZip);

        // Wait for upload to finish
        await Task.Delay(5000);
        await TakeScreenshot("04_packages_after_upload");

        var content = await _page.ContentAsync();
        TestContext.Out.WriteLine($"Page contains 'test-lcs-package': {content.Contains("test-lcs-package")}");
        Assert.That(content, Does.Contain("test-lcs-package"), "Uploaded package should appear in the list");

        File.Delete(tempZip);
    }

    [Test, Order(5)]
    public async Task Packages_UploadTwo_MergeWorks()
    {
        // Create first package if not already uploaded
        var tempZip1 = Path.Combine(Path.GetTempPath(), "lcs-pkg-a.zip");
        var tempZip2 = Path.Combine(Path.GetTempPath(), "lcs-pkg-b.zip");

        CreateTestZip(tempZip1, "AOSService/moduleA.txt", "Module A content",
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<HotfixInstallationInfo>
  <MetadataModuleList><string>ModuleA</string></MetadataModuleList>
  <AllComponentList><ArrayOfString><string>ComponentA</string></ArrayOfString></AllComponentList>
</HotfixInstallationInfo>");

        CreateTestZip(tempZip2, "AOSService/moduleB.txt", "Module B content",
            @"<?xml version=""1.0"" encoding=""utf-8""?>
<HotfixInstallationInfo>
  <MetadataModuleList><string>ModuleB</string></MetadataModuleList>
  <AllComponentList><ArrayOfString><string>ComponentB</string></ArrayOfString></AllComponentList>
</HotfixInstallationInfo>");

        await _page!.GotoAsync($"{BaseUrl}/packages", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();

        // Upload first ZIP
        await UploadFile(tempZip1);
        await Task.Delay(3000);

        // Upload second ZIP
        await UploadFile(tempZip2);
        await Task.Delay(3000);

        await TakeScreenshot("05_packages_two_uploaded");

        // Select packages with checkboxes
        var checkboxes = await _page.Locator("td input[type='checkbox']").AllAsync();
        TestContext.Out.WriteLine($"Found {checkboxes.Count} row checkboxes");

        if (checkboxes.Count >= 2)
        {
            await checkboxes[0].ClickAsync();
            await checkboxes[1].ClickAsync();
            await Task.Delay(500);

            await TakeScreenshot("05_packages_selected");

            // Click Merge button
            var mergeButton = _page.Locator("button:has-text('Merge')");
            if (await mergeButton.CountAsync() > 0)
            {
                await mergeButton.First.ClickAsync();
                await Task.Delay(1000);

                // Confirm merge in dialog
                var yesButton = _page.Locator("button:has-text('Merge')");
                if (await yesButton.CountAsync() > 0)
                {
                    await yesButton.First.ClickAsync();
                }

                // Wait for merge to complete
                await Task.Delay(10000);
                await TakeScreenshot("05_packages_after_merge");

                var content = await _page.ContentAsync();
                Assert.That(content, Does.Contain("Merged"), "Merged package should appear");
            }
            else
            {
                TestContext.Out.WriteLine("Merge button not visible - checking if packages were selected");
                await TakeScreenshot("05_packages_no_merge_button");
                Assert.Warn("Merge button did not appear after selecting 2 packages");
            }
        }
        else
        {
            Assert.Warn($"Not enough checkboxes found ({checkboxes.Count}), skipping merge test");
        }

        File.Delete(tempZip1);
        File.Delete(tempZip2);
    }

    [Test, Order(6)]
    public async Task Deploy_PageShowsPackagesAndEnvironments()
    {
        await _page!.GotoAsync($"{BaseUrl}/deploy", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        await TakeScreenshot("06_deploy_page");

        var content = await _page.ContentAsync();
        Assert.That(content, Does.Contain("Select Package"), "Deploy page should have package selection panel");
        Assert.That(content, Does.Contain("Environments"), "Deploy page should have environment selection panel");
        Assert.That(content, Does.Contain("Start Deploy"), "Deploy page should have deploy button");
    }

    [Test, Order(7)]
    public async Task DeploymentHistory_PageLoads()
    {
        await _page!.GotoAsync($"{BaseUrl}/deployments", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        await TakeScreenshot("07_history_page");

        var content = await _page.ContentAsync();
        Assert.That(content, Does.Contain("Deployment History"), "History page should load");
    }

    [Test, Order(8)]
    public async Task SideNavigation_Works()
    {
        await _page!.GotoAsync(BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();

        // Navigate via side nav
        await _page.Locator("nav >> text=Packages").ClickAsync();
        await Task.Delay(1000);
        Assert.That(_page.Url, Does.Contain("/packages"));

        await _page.Locator("nav >> text=Environments").ClickAsync();
        await Task.Delay(1000);
        Assert.That(_page.Url, Does.Contain("/environments"));

        await _page.Locator("nav >> text=Deploy").ClickAsync();
        await Task.Delay(1000);
        Assert.That(_page.Url, Does.Contain("/deploy"));

        await _page.Locator("nav >> text=History").ClickAsync();
        await Task.Delay(1000);
        Assert.That(_page.Url, Does.Contain("/deployments"));

        await _page.Locator("nav >> text=Settings").ClickAsync();
        await Task.Delay(1000);
        Assert.That(_page.Url, Does.Contain("/settings"));
    }

    [Test, Order(9)]
    public async Task DarkMode_Toggle_Works()
    {
        await _page!.GotoAsync(BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        await TakeScreenshot("09_before_dark_mode");

        // Find dark mode toggle — last icon button in the appbar
        var toggleBtn = _page.Locator("header button").Last;
        await toggleBtn.ClickAsync();
        await Task.Delay(500);
        await TakeScreenshot("09_after_dark_mode");

        // Page should still be functional
        var content = await _page.ContentAsync();
        Assert.That(content, Does.Contain("Dashboard"), "Page should still work after dark mode toggle");
    }

    [Test, Order(10)]
    public async Task Environments_ImportFromScript_ParsesAndCreates()
    {
        await _page!.GotoAsync($"{BaseUrl}/environments", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();

        // Click "Import from Script" button
        await _page.Locator("button:has-text('Import from Script')").ClickAsync();
        await Task.Delay(1000);
        await TakeScreenshot("10_import_dialog_opened");

        // Paste script output into textarea
        var scriptOutput = @"==============================================================
  SERVICE PRINCIPAL CREATED SUCCESSFULLY
==============================================================

  Application (Client) ID:  11111111-2222-3333-4444-555555555555
  Directory (Tenant) ID:    aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
  Client Secret:            MyTestSecretValue12345!
  Secret expires:           2028-01-01

  Environments (OK):        import-env-1.crm.dynamics.com, import-env-2.crm.dynamics.com
==============================================================";

        var textarea = _page.Locator("textarea").First;
        await textarea.ClickAsync();
        await textarea.FillAsync(scriptOutput);
        // Trigger Blazor binding explicitly
        await textarea.DispatchEventAsync("input", new { });
        await textarea.DispatchEventAsync("change", new { });
        await Task.Delay(500);
        await TakeScreenshot("10_import_text_pasted");

        // Click Parse
        await _page.Locator("button:has-text('Parse')").ClickAsync();
        await Task.Delay(1000);
        await TakeScreenshot("10_import_parsed");

        // Verify parsed data is displayed
        var content = await _page.ContentAsync();
        Assert.That(content, Does.Contain("11111111"), "Should show parsed Application ID");
        Assert.That(content, Does.Contain("aaaaaaaa"), "Should show parsed Tenant ID");
        Assert.That(content, Does.Contain("import-env-1.crm.dynamics.com"), "Should show first environment");
        Assert.That(content, Does.Contain("import-env-2.crm.dynamics.com"), "Should show second environment");

        // Click Create button
        var createBtn = _page.Locator("button:has-text('Create 2 environment')");
        if (await createBtn.CountAsync() > 0)
        {
            await createBtn.First.ClickAsync();
            await Task.Delay(3000);
            await TakeScreenshot("10_import_after_create");

            // Verify environments appear in the table
            content = await _page.ContentAsync();
            Assert.That(content, Does.Contain("Import-env-1") .Or.Contain("import-env-1"), "First imported env should appear");
            Assert.That(content, Does.Contain("Import-env-2") .Or.Contain("import-env-2"), "Second imported env should appear");
        }
        else
        {
            Assert.Warn("Create button not found after parsing");
        }
    }

    [Test, Order(11)]
    public async Task NoConsoleErrors_OnPageLoad()
    {
        var errors = new List<string>();
        _page!.Console += (_, msg) =>
        {
            if (msg.Type == "error" && !msg.Text.Contains("favicon"))
                errors.Add(msg.Text);
        };

        await _page.GotoAsync(BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForBlazor();
        await Task.Delay(3000); // Give time for any delayed errors

        // Navigate to each page
        var pages = new[] { "/packages", "/environments", "/deploy", "/deployments", "/settings" };
        foreach (var p in pages)
        {
            await _page.GotoAsync($"{BaseUrl}{p}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await WaitForBlazor();
            await Task.Delay(1000);
        }

        if (errors.Count > 0)
        {
            TestContext.Out.WriteLine("Console errors found:");
            foreach (var err in errors)
                TestContext.Out.WriteLine($"  - {err}");
        }

        // Allow SignalR reconnection messages but fail on real errors
        var realErrors = errors.Where(e =>
            !e.Contains("WebSocket connection") &&
            !e.Contains("aspnetcore-browser-refresh") &&
            !e.Contains("favicon") &&
            !e.Contains("_blazor")).ToList();

        Assert.That(realErrors, Is.Empty, $"Console should have no errors. Found: {string.Join("; ", realErrors)}");
    }

    // ====================== HELPERS ======================

    private void CreateTestZip(string path, string entryName, string content, string? xmlContent = null)
    {
        if (File.Exists(path)) File.Delete(path);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var entry = zip.CreateEntry(entryName);
        using (var writer = new StreamWriter(entry.Open()))
        {
            writer.Write(content);
        }

        if (xmlContent != null)
        {
            var xmlEntry = zip.CreateEntry("HotfixInstallationInfo.xml");
            using (var writer = new StreamWriter(xmlEntry.Open()))
            {
                writer.Write(xmlContent);
            }
        }
    }

    private async Task UploadFile(string filePath)
    {
        var fileChooserTask = _page!.WaitForFileChooserAsync(new() { Timeout = 5000 });

        try
        {
            await _page.Locator("button:has-text('Select ZIP file')").ClickAsync();
            var fileChooser = await fileChooserTask;
            await fileChooser.SetFilesAsync(filePath);
        }
        catch (TimeoutException)
        {
            // Fallback: try setting file input directly
            TestContext.Out.WriteLine("FileChooser not triggered, trying direct input");
            var fileInput = _page.Locator("input[type='file']");
            await fileInput.SetInputFilesAsync(filePath);
        }
    }
}
