namespace DeployPortal.PackageOps;

/// <summary>
/// One variant of a model (same module name) from a single package.
/// </summary>
public class LcsModelConflictVariant
{
    public int PackageIndex { get; set; }
    public List<string> FileNames { get; set; } = new();
    public string Version { get; set; } = "";
}

/// <summary>
/// A model that appears in more than one package with different versions.
/// </summary>
public class LcsModelConflict
{
    public string ModuleName { get; set; } = "";
    public List<LcsModelConflictVariant> Variants { get; set; } = new();
}

/// <summary>
/// User choice for a conflict: keep all variants (-1) or keep only the variant from the given package index.
/// </summary>
public class LcsModelConflictResolution
{
    public string ModuleName { get; set; } = "";
    /// <summary> -1 = keep both/all; otherwise 0-based package index to keep. </summary>
    public int KeepPackageIndex { get; set; } = -1;
}
