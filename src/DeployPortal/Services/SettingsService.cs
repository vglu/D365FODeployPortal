using System.Text.Json;

namespace DeployPortal.Services;

/// <summary>
/// Manages application settings that can be changed at runtime via the UI.
/// Settings are persisted to a JSON file next to appsettings.json.
/// On read, user settings override appsettings.json values.
/// </summary>
public class SettingsService
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SettingsService> _logger;
    private readonly string _userSettingsPath;
    private static readonly object _lock = new();

    public SettingsService(IConfiguration config, IWebHostEnvironment env, ILogger<SettingsService> logger)
    {
        _config = config;
        _env = env;
        _logger = logger;
        _userSettingsPath = Path.Combine(AppContext.BaseDirectory, "usersettings.json");
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
        Path.Combine(AppContext.BaseDirectory, "Packages"));
    public string TempWorkingDir => GetSetting("TempWorkingDir",
        Path.Combine(Path.GetTempPath(), "DeployPortal"));
    public string DatabasePath => GetSetting("DatabasePath",
        Path.Combine(AppContext.BaseDirectory, "deploy-portal.db"));

    /// <summary>
    /// Optional path to LCS template (folder or .zip). When set, Unified→LCS conversion uses it as skeleton so the result has the full LCS structure (AOSService/Packages exe, DLLs, Scripts, etc.). Leave empty for minimal LCS output.
    /// </summary>
    public string LcsTemplatePath => GetSetting("LcsTemplatePath", "");

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
    public Dictionary<string, string> GetAllSettings()
    {
        var userSettings = LoadUserSettings();
        var keys = new[] { "ConverterEngine", "ProcessingMode", "AzureFunctionsUrl", "AzureBlobConnectionString", "AzureFunctionKey", "ModelUtilPath", "PacCliPath", "PackageStoragePath", "TempWorkingDir", "DatabasePath", "LcsTemplatePath" };
        var result = new Dictionary<string, string>();

        foreach (var key in keys)
        {
            if (userSettings.TryGetValue(key, out var userVal) && !string.IsNullOrEmpty(userVal))
                result[key] = userVal;
            else
                result[key] = _config[$"DeployPortal:{key}"] ?? "";
        }

        return result;
    }

    public void SaveSettings(Dictionary<string, string> settings)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_userSettingsPath, json);
            _logger.LogInformation("User settings saved to {Path}", _userSettingsPath);
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
