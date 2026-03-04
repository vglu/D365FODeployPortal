using System.Text.Json;
using DeployPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace DeployPortal.Services;

/// <summary>
/// Manages application settings that can be changed at runtime via the UI.
/// Settings are persisted to a JSON file in LocalApplicationData so they survive rebuilds and clean.
/// Some settings (e.g. MaxConcurrentDeployments) are stored in the database.
/// On read, user settings override appsettings.json values.
/// </summary>
public class SettingsService : ISettingsService
{
    public const string KeyMaxConcurrentDeployments = "MaxConcurrentDeployments";
    public const int DefaultMaxConcurrentDeployments = 2;
    public const int MinMaxConcurrentDeployments = 1;
    public const int MaxMaxConcurrentDeployments = 20;

    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SettingsService> _logger;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly string _userSettingsPath;
    private static readonly object _lock = new();

    public SettingsService(
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<SettingsService> logger,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _config = config;
        _env = env;
        _logger = logger;
        _dbFactory = dbFactory;
        var configuredPath = _config["DeployPortal:UserSettingsPath"]?.Trim();
        if (!string.IsNullOrEmpty(configuredPath))
        {
            _userSettingsPath = Path.GetFullPath(configuredPath);
        }
        else
        {
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(appDataDir, "DeployPortal");
            _userSettingsPath = Path.Combine(appDir, "usersettings.json");
        }
        MigrateFromLegacyPathIfNeeded();
    }

    /// <summary>One-time migration: copy usersettings.json from bin folder to AppData if it exists there.</summary>
    private void MigrateFromLegacyPathIfNeeded()
    {
        if (File.Exists(_userSettingsPath)) return;
        var legacyPath = Path.Combine(AppContext.BaseDirectory, "usersettings.json");
        if (!File.Exists(legacyPath)) return;
        try
        {
            var dir = Path.GetDirectoryName(_userSettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Copy(legacyPath, _userSettingsPath);
            _logger.LogInformation("Migrated user settings from {Legacy} to {New}", legacyPath, _userSettingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not migrate user settings from {Legacy}", legacyPath);
        }
    }

    // ========== Settings Keys ==========
    /// <summary>
    /// Converter engine to use: "BuiltIn" or "ModelUtil".
    /// Built-in doesn't require ModelUtil.exe installed.
    /// </summary>
    public string ConverterEngine => GetSetting("ConverterEngine", "BuiltIn");

    /// <summary>
    /// Processing mode: "Local" (in-process) or "Azure" (Azure Functions).
    /// </summary>
    public string ProcessingMode => GetSetting("ProcessingMode", "Local");

    /// <summary>
    /// Azure Functions base URL (e.g. https://my-func.azurewebsites.net).
    /// Only used when ProcessingMode == "Azure".
    /// </summary>
    public string AzureFunctionsUrl => GetSetting("AzureFunctionsUrl", "");

    /// <summary>
    /// Azure Blob Storage connection string.
    /// Only used when ProcessingMode == "Azure".
    /// </summary>
    public string AzureBlobConnectionString => GetSetting("AzureBlobConnectionString", "");

    /// <summary>
    /// Azure Functions host key for authentication.
    /// Only used when ProcessingMode == "Azure".
    /// </summary>
    public string AzureFunctionKey => GetSetting("AzureFunctionKey", "");

    public string ModelUtilPath => GetSetting("ModelUtilPath", "");
    public string PacCliPath => GetSetting("PacCliPath", "");
    public string PackageStoragePath => GetSetting("PackageStoragePath",
        Path.Combine(@"C:\DeployPortal", "Packages"));
    public string TempWorkingDir => GetSetting("TempWorkingDir",
        Path.Combine(@"C:\Temp", "DeployPortal"));
    public string DatabasePath => GetSetting("DatabasePath",
        Path.Combine(@"C:\DeployPortal", "deploy-portal.db"));

    /// <summary>
    /// Optional path to LCS template (folder or .zip). When set, Unified→LCS conversion uses it as skeleton so the result has the full LCS structure (AOSService/Packages exe, DLLs, Scripts, etc.). Leave empty for minimal LCS output.
    /// </summary>
    public string LcsTemplatePath => GetSetting("LcsTemplatePath", "");

    /// <summary>
    /// When true, deployment runs auth and "pac auth who" to verify connection but does not run "pac package deploy". Prevents accidental deploy.
    /// </summary>
    public bool SimulateDeployment => string.Equals(GetSetting("SimulateDeployment", "false"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>When true, pre-deploy validation requires Organization Friendly Name match when set on environment.</summary>
    public bool VerifyOrganizationFriendlyNameOnDeploy => string.Equals(GetSetting("VerifyOrganizationFriendlyNameOnDeploy", "false"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Azure DevOps: organization (e.g. contoso or org name from dev.azure.com).</summary>
    public string AzureDevOpsOrganization => GetSetting("AzureDevOpsOrganization", "");
    /// <summary>Azure DevOps: project name or ID.</summary>
    public string AzureDevOpsProject => GetSetting("AzureDevOpsProject", "");
    /// <summary>Azure DevOps PAT stored encrypted (Build Read). Empty if not set.</summary>
    public string AzureDevOpsPatEncrypted => GetSetting("AzureDevOpsPatEncrypted", "");

    /// <summary>Last used feed name for Release Pipeline deploy. Default "Packages".</summary>
    public string ReleasePipelineFeedName => GetSetting("ReleasePipelineFeedName", "Packages");

    /// <summary>Path where user settings are stored (for display in UI).</summary>
    public string UserSettingsFilePath => _userSettingsPath;

    /// <summary>
    /// Maximum number of deployments that can run at the same time. Stored in the database.
    /// Default: 2. Valid range: 1–20.
    /// </summary>
    public int MaxConcurrentDeployments => GetMaxConcurrentDeploymentsFromDb();

    // ========== Tool Validation ==========
    public record ToolStatus(string Name, string Path, bool Exists, string Message);

    public List<ToolStatus> ValidateTools()
    {
        var results = new List<ToolStatus>();

        // Converter engine
        var engine = ConverterEngine;
        var isBuiltIn = engine.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase);

        // ModelUtil.exe — only required if engine is "ModelUtil"
        var modelUtilPath = ModelUtilPath;
        if (isBuiltIn)
        {
            // Check that template files exist
            var templateDir = Path.Combine(AppContext.BaseDirectory, "Resources", "UnifiedTemplate");
            var templateExists = Directory.Exists(templateDir) &&
                                 File.Exists(Path.Combine(templateDir, "TemplatePackage.dll"));
            results.Add(new ToolStatus("LCS Converter",
                isBuiltIn ? "Built-in" : modelUtilPath,
                templateExists,
                templateExists
                    ? "Built-in converter active (no ModelUtil.exe needed)"
                    : "Template files missing in Resources/UnifiedTemplate/"));
        }
        else if (string.IsNullOrWhiteSpace(modelUtilPath))
        {
            // Try auto-detect
            var detected = GetEffectiveModelUtilPath();
            if (!string.IsNullOrWhiteSpace(detected) && File.Exists(detected))
                results.Add(new ToolStatus("ModelUtil.exe", detected, true, "Auto-detected"));
            else
                results.Add(new ToolStatus("ModelUtil.exe", "", false,
                    "Not configured. Switch to Built-in converter or specify ModelUtil.exe path."));
        }
        else
        {
            var exists = File.Exists(modelUtilPath);
            results.Add(new ToolStatus("ModelUtil.exe", modelUtilPath, exists,
                exists ? "Found" : "File not found at configured path"));
        }

        // PAC CLI
        var pacPath = PacCliPath;
        if (string.IsNullOrWhiteSpace(pacPath))
        {
            // Try to find pac in PATH
            var pacInPath = FindInPath("pac.cmd") ?? FindInPath("pac.exe") ?? FindInPath("pac");
            if (pacInPath != null)
            {
                results.Add(new ToolStatus("PAC CLI", pacInPath, true,
                    "Found in system PATH (auto-detected)"));
            }
            else
            {
                results.Add(new ToolStatus("PAC CLI", "", false,
                    "Not configured and not found in PATH. Required for deployment."));
            }
        }
        else
        {
            var exists = File.Exists(pacPath);
            results.Add(new ToolStatus("PAC CLI", pacPath, exists,
                exists ? "Found" : "File not found at configured path"));
        }

        // Azure CLI (az)
        var azInPath = FindInPath("az.cmd") ?? FindInPath("az.exe") ?? FindInPath("az");
        if (azInPath != null)
        {
            results.Add(new ToolStatus("Azure CLI", azInPath, true,
                "Found in system PATH. Required for 'Deploy via Release Pipeline'."));
        }
        else
        {
            results.Add(new ToolStatus("Azure CLI", "", false,
                "Not found in PATH. Required for 'Deploy via Release Pipeline' (Universal Package upload)."));
        }

        return results;
    }

    /// <summary>
    /// Returns the effective PAC CLI path — configured or auto-detected from PATH.
    /// </summary>
    public string GetEffectivePacPath()
    {
        var configured = PacCliPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        return FindInPath("pac.cmd") ?? FindInPath("pac.exe") ?? FindInPath("pac") ?? configured;
    }

    /// <summary>
    /// Returns the effective ModelUtil.exe path.
    /// </summary>
    public string GetEffectiveModelUtilPath()
    {
        var configured = ModelUtilPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        // Try common install locations
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dynamicsDir = Path.Combine(localAppData, "Microsoft", "Dynamics365");
        if (Directory.Exists(dynamicsDir))
        {
            try
            {
                var found = Directory.GetFiles(dynamicsDir, "ModelUtil.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (found != null) return found;
            }
            catch { }
        }

        return configured;
    }

    // ========== Read/Write ==========
    private static readonly string[] FileSettingKeys = new[] { "ConverterEngine", "ProcessingMode", "AzureFunctionsUrl", "AzureBlobConnectionString", "AzureFunctionKey", "ModelUtilPath", "PacCliPath", "PackageStoragePath", "TempWorkingDir", "DatabasePath", "LcsTemplatePath", "SimulateDeployment", "VerifyOrganizationFriendlyNameOnDeploy" };

    public Dictionary<string, string> GetAllSettings()
    {
        var userSettings = LoadUserSettings();
        var result = new Dictionary<string, string>();

        foreach (var key in FileSettingKeys)
        {
            if (userSettings.TryGetValue(key, out var userVal) && !string.IsNullOrEmpty(userVal))
                result[key] = userVal;
            else
                result[key] = _config[$"DeployPortal:{key}"] ?? "";
        }

        result[KeyMaxConcurrentDeployments] = MaxConcurrentDeployments.ToString();
        return result;
    }

    public void SaveSettings(Dictionary<string, string> settings)
    {
        if (settings.TryGetValue(KeyMaxConcurrentDeployments, out var mcVal) &&
            int.TryParse(mcVal, out var n) &&
            n >= MinMaxConcurrentDeployments &&
            n <= MaxMaxConcurrentDeployments)
        {
            SaveMaxConcurrentDeploymentsToDb(n);
        }

        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_userSettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var fileDict = new Dictionary<string, string>();
            foreach (var key in FileSettingKeys)
            {
                if (settings.TryGetValue(key, out var v))
                    fileDict[key] = v;
            }
            var json = JsonSerializer.Serialize(fileDict, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_userSettingsPath, json);
            _logger.LogInformation("User settings saved to {Path}", _userSettingsPath);
        }
    }

    private int GetMaxConcurrentDeploymentsFromDb()
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var row = db.AppSettings.AsNoTracking().FirstOrDefault(x => x.Key == KeyMaxConcurrentDeployments);
            if (row != null && int.TryParse(row.Value, out var v) && v >= MinMaxConcurrentDeployments && v <= MaxMaxConcurrentDeployments)
                return v;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read MaxConcurrentDeployments from database, using default {Default}", DefaultMaxConcurrentDeployments);
        }
        return DefaultMaxConcurrentDeployments;
    }

    private void SaveMaxConcurrentDeploymentsToDb(int value)
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var row = db.AppSettings.FirstOrDefault(x => x.Key == KeyMaxConcurrentDeployments);
            if (row != null)
                row.Value = value.ToString();
            else
                db.AppSettings.Add(new AppSetting { Key = KeyMaxConcurrentDeployments, Value = value.ToString() });
            db.SaveChanges();
            _logger.LogInformation("MaxConcurrentDeployments saved to database: {Value}", value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save MaxConcurrentDeployments to database");
            throw;
        }
    }

    /// <summary>
    /// Saves Azure DevOps settings (PAT encrypted, org, project). Pass null to leave a value unchanged.
    /// </summary>
    public void SaveAzureDevOpsSettings(string? patEncrypted, string? organization, string? project)
    {
        lock (_lock)
        {
            var userSettings = LoadUserSettings();
            if (patEncrypted != null) userSettings["AzureDevOpsPatEncrypted"] = patEncrypted;
            if (organization != null) userSettings["AzureDevOpsOrganization"] = organization;
            if (project != null) userSettings["AzureDevOpsProject"] = project;
            var dir = Path.GetDirectoryName(_userSettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(userSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_userSettingsPath, json);
            _logger.LogInformation("Azure DevOps settings updated.");
        }
    }

    /// <summary>Saves last used feed name for Release Pipeline deploy (persisted for next time).</summary>
    public void SaveReleasePipelineFeedName(string feedName)
    {
        if (string.IsNullOrWhiteSpace(feedName)) return;
        lock (_lock)
        {
            var userSettings = LoadUserSettings();
            userSettings["ReleasePipelineFeedName"] = feedName.Trim();
            var dir = Path.GetDirectoryName(_userSettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(userSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_userSettingsPath, json);
        }
    }

    private string GetSetting(string key, string defaultValue)
    {
        // Priority: user settings file > appsettings.json > default
        var userSettings = LoadUserSettings();
        if (userSettings.TryGetValue(key, out var userVal) && !string.IsNullOrEmpty(userVal))
            return userVal;

        return _config[$"DeployPortal:{key}"] ?? defaultValue;
    }

    private Dictionary<string, string> LoadUserSettings()
    {
        try
        {
            if (File.Exists(_userSettingsPath))
            {
                var json = File.ReadAllText(_userSettingsPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load user settings from {Path}", _userSettingsPath);
        }
        return new Dictionary<string, string>();
    }

    private static string? FindInPath(string executable)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            try
            {
                var fullPath = Path.Combine(dir, executable);
                if (File.Exists(fullPath)) return fullPath;
            }
            catch { }
        }
        return null;
    }
}
