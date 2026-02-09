using DeployPortal.PackageOps;

namespace DeployPortal.Services;

/// <summary>
/// LCS → Unified conversion using external ModelUtil.exe.
/// Delegates to ModelUtilConvertEngine from the shared PackageOps library.
/// </summary>
public class ConvertService
{
    private readonly SettingsService _settings;
    private readonly ILogger<ConvertService> _logger;

    public ConvertService(SettingsService settings, ILogger<ConvertService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> ConvertToUnifiedAsync(string lcsPackagePath, Action<string>? onLog = null)
    {
        var modelUtilPath = _settings.GetEffectiveModelUtilPath();
        var engine = new ModelUtilConvertEngine(modelUtilPath);
        var result = await engine.ConvertToUnifiedAsync(lcsPackagePath, onLog);
        _logger.LogInformation("ModelUtil conversion: {File}", Path.GetFileName(lcsPackagePath));
        return result;
    }
}
