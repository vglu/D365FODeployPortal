namespace DeployPortal.PackageOps;

/// <summary>
/// In-process implementation of IPackageOpsService.
/// Runs ConvertEngine + MergeEngine locally on the same machine as the web app.
/// </summary>
public class LocalPackageOpsService : IPackageOpsService
{
    private readonly string _tempDir;
    private readonly string _templateDir;

    /// <param name="tempDir">Temporary working directory for extraction/conversion.</param>
    /// <param name="templateDir">Path to UnifiedTemplate resources directory.</param>
    public LocalPackageOpsService(string tempDir, string templateDir)
    {
        _tempDir = tempDir;
        _templateDir = templateDir;
    }

    public async Task<string> ConvertToUnifiedAsync(string lcsPackagePath, Action<string>? onLog = null)
    {
        var engine = new ConvertEngine(_tempDir, _templateDir);
        return await engine.ConvertToUnifiedAsync(lcsPackagePath, onLog);
    }

    public async Task<string> ConvertToLcsAsync(string unifiedPackagePath, Action<string>? onLog = null)
    {
        var engine = new ConvertEngine(_tempDir, _templateDir);
        return await engine.ConvertToLcsAsync(unifiedPackagePath, onLog);
    }

    public string MergeLcs(List<string> packagePaths, Action<string>? onLog = null)
    {
        var engine = new MergeEngine(_tempDir);
        return engine.MergeLcs(packagePaths, onLog);
    }

    public string MergeUnified(List<string> packagePaths, Action<string>? onLog = null)
    {
        var engine = new MergeEngine(_tempDir);
        return engine.MergeUnified(packagePaths, onLog);
    }

    public string DetectPackageType(string zipPath) => PackageAnalyzer.DetectPackageType(zipPath);

    public string? DetectMergeStrategy(IEnumerable<string> packageTypes) =>
        PackageAnalyzer.DetectMergeStrategy(packageTypes);
}
