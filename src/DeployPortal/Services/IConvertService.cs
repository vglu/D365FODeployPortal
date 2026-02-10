namespace DeployPortal.Services;

/// <summary>
/// Abstraction for LCS ↔ Unified package conversion.
/// Implementations: BuiltInConvertService (both directions), ConvertService (ModelUtil, Unified only).
/// Application uses a composite that delegates based on settings.
/// </summary>
public interface IConvertService
{
    Task<string> ConvertToUnifiedAsync(string lcsPackagePath, Action<string>? onLog = null);
    Task<string> ConvertToLcsAsync(string unifiedPackagePath, Action<string>? onLog = null);
}
