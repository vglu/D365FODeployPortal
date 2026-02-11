using DeployPortal.Models;

namespace DeployPortal.Models.Api;

/// <summary>Package summary for API responses (no server file path).</summary>
public class PackageDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string PackageType { get; set; } = "LCS";
    public DateTime UploadedAt { get; set; }
    public int? ParentMergeFromId { get; set; }
    public string? MergeSourceNames { get; set; }
    public string? DevOpsTaskUrl { get; set; }
    public string? LicenseFileNames { get; set; }

    public static PackageDto From(Package p)
    {
        return new PackageDto
        {
            Id = p.Id,
            Name = p.Name,
            OriginalFileName = p.OriginalFileName,
            FileSizeBytes = p.FileSizeBytes,
            PackageType = p.PackageType,
            UploadedAt = p.UploadedAt,
            ParentMergeFromId = p.ParentMergeFromId,
            MergeSourceNames = p.MergeSourceNames,
            DevOpsTaskUrl = p.DevOpsTaskUrl,
            LicenseFileNames = p.LicenseFileNames
        };
    }
}
