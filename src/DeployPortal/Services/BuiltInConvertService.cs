using DeployPortal.PackageOps;

namespace DeployPortal.Services;

/// <summary>
/// Built-in LCS ↔ Unified converter.
/// Delegates to ConvertEngine from the shared PackageOps library.
/// </summary>
public class BuiltInConvertService
{
    private readonly ILogger<BuiltInConvertService> _logger;
    private readonly SettingsService _settings;

    public BuiltInConvertService(SettingsService settings, ILogger<BuiltInConvertService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    private static string TemplatePath =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "UnifiedTemplate");

    public async Task<string> ConvertToUnifiedAsync(string lcsPackagePath, Action<string>? onLog = null)
    {
        var engine = new ConvertEngine(_settings.TempWorkingDir, TemplatePath);
        var result = await engine.ConvertToUnifiedAsync(lcsPackagePath, onLog);
        _logger.LogInformation("Built-in conversion LCS → Unified: {File}", Path.GetFileName(lcsPackagePath));
        return result;
    }

    public async Task<string> ConvertToLcsAsync(string unifiedPackagePath, Action<string>? onLog = null)
    {
        var engine = new ConvertEngine(_settings.TempWorkingDir, TemplatePath);
        var result = await engine.ConvertToLcsAsync(unifiedPackagePath, onLog);
        _logger.LogInformation("Built-in conversion Unified → LCS: {File}", Path.GetFileName(unifiedPackagePath));
        return result;
    }

    /// <summary>
    /// Extracts module name from a NuGet-style filename.
    /// Delegates to PackageAnalyzer.ExtractModuleName.
    /// </summary>
    internal static string ExtractModuleName(string fileName) =>
        PackageAnalyzer.ExtractModuleName(fileName);
}
