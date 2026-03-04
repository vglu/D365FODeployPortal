namespace DeployPortal.Models.Api;

public class MergeRequestDto
{
    public List<int> PackageIds { get; set; } = new();
    public string? MergeName { get; set; }
    /// <summary>Optional. For LCS merge: which variant to keep per conflicting model (-1 = keep both).</summary>
    public List<MergeConflictResolutionDto>? ModelConflictResolutions { get; set; }
}

public class MergeConflictResolutionDto
{
    public string ModuleName { get; set; } = "";
    /// <summary>-1 = keep all variants; otherwise 0-based package index to keep.</summary>
    public int KeepPackageIndex { get; set; } = -1;
}

public class MergePreviewRequestDto
{
    public List<int> PackageIds { get; set; } = new();
}

public class MergeConflictVariantDto
{
    public int PackageIndex { get; set; }
    public List<string> FileNames { get; set; } = new();
    public string Version { get; set; } = "";
}

public class MergeConflictDto
{
    public string ModuleName { get; set; } = "";
    public List<MergeConflictVariantDto> Variants { get; set; } = new();
}

public class MergePreviewResponseDto
{
    public string? Strategy { get; set; }
    public List<MergeConflictDto> Conflicts { get; set; } = new();
}
