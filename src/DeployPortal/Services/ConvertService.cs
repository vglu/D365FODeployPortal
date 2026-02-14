using DeployPortal.PackageOps;

namespace DeployPortal.Services;

/// <summary>
/// LCS → Unified conversion using external ModelUtil.exe.
/// Only ConvertToUnified is supported; ConvertToLcs throws (use BuiltIn for reverse).
/// </summary>
public class ConvertService : IConvertService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<ConvertService> _logger;

    public ConvertService(ISettingsService settings, ILogger<ConvertService> logger)
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

    public Task<string> ConvertToLcsAsync(string unifiedPackagePath, Action<string>? onLog = null)
    {
        throw new NotSupportedException("ModelUtil does not support Unified → LCS; use built-in converter.");
    }
}
