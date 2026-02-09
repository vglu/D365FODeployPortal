namespace DeployPortal.PackageOps;

/// <summary>
/// High-level interface for package operations (convert, merge).
/// Implemented by LocalPackageOpsService (in-process) and AzurePackageOpsService (Azure Functions).
/// The web application uses this interface and selects the implementation based on Settings → ProcessingMode.
/// </summary>
public interface IPackageOpsService
{
    /// <summary>
    /// Converts an LCS package to Unified format.
    /// Returns the path to the directory containing the Unified output.
    /// </summary>
    Task<string> ConvertToUnifiedAsync(string lcsPackagePath, Action<string>? onLog = null);

    /// <summary>
    /// Converts a Unified package to LCS format.
    /// Returns the path to the directory containing the LCS output.
    /// </summary>
    Task<string> ConvertToLcsAsync(string unifiedPackagePath, Action<string>? onLog = null);

    /// <summary>
    /// Merges multiple LCS packages into one.
    /// Returns the path to the merged output directory.
    /// </summary>
    string MergeLcs(List<string> packagePaths, Action<string>? onLog = null);

    /// <summary>
    /// Merges multiple Unified packages into one.
    /// Returns the path to the merged output directory.
    /// </summary>
    string MergeUnified(List<string> packagePaths, Action<string>? onLog = null);

    /// <summary>
    /// Detects the package type (LCS, Unified, Other).
    /// </summary>
    string DetectPackageType(string zipPath);

    /// <summary>
    /// Determines merge strategy for a set of package types.
    /// Returns "LCS", "Unified", or null if incompatible.
    /// </summary>
    string? DetectMergeStrategy(IEnumerable<string> packageTypes);
}
