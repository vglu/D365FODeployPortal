namespace DeployPortal.Services;

/// <summary>
/// Selects BuiltIn or ModelUtil (ConvertService) for LCS→Unified based on settings.
/// Unified→LCS always uses BuiltIn (ModelUtil does not support reverse).
/// </summary>
public class CompositeConvertService : IConvertService
{
    private readonly SettingsService _settings;
    private readonly BuiltInConvertService _builtIn;
    private readonly ConvertService _modelUtil;

    public CompositeConvertService(
        SettingsService settings,
        BuiltInConvertService builtIn,
        ConvertService modelUtil)
    {
        _settings = settings;
        _builtIn = builtIn;
        _modelUtil = modelUtil;
    }

    public async Task<string> ConvertToUnifiedAsync(string lcsPackagePath, Action<string>? onLog = null)
    {
        var useBuiltIn = _settings.ConverterEngine.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase);
        return useBuiltIn
            ? await _builtIn.ConvertToUnifiedAsync(lcsPackagePath, onLog)
            : await _modelUtil.ConvertToUnifiedAsync(lcsPackagePath, onLog);
    }

    public Task<string> ConvertToLcsAsync(string unifiedPackagePath, Action<string>? onLog = null)
    {
        return _builtIn.ConvertToLcsAsync(unifiedPackagePath, onLog);
    }
}
