using System.ComponentModel.DataAnnotations;

namespace DeployPortal.Models;

public class Package
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string StoredFilePath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [Required, MaxLength(50)]
    public string PackageType { get; set; } = "LCS"; // LCS, Merged, Unified

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public int? ParentMergeFromId { get; set; }

    /// <summary>
    /// JSON array of source package names used to create this merged package.
    /// Example: ["PackageA","PackageB"]
    /// </summary>
    [MaxLength(4000)]
    public string? MergeSourceNames { get; set; }

    /// <summary>
    /// Optional link to Azure DevOps work item / task associated with this package.
    /// </summary>
    [MaxLength(1000)]
    public string? DevOpsTaskUrl { get; set; }

    /// <summary>
    /// JSON array of license file names found in the package.
    /// Example: ["license1.txt","license2.xml"]
    /// Null or empty means no license files.
    /// </summary>
    [MaxLength(4000)]
    public string? LicenseFileNames { get; set; }

    /// <summary>
    /// When true, package is archived and shown only in the Archive tab.
    /// </summary>
    public bool IsArchived { get; set; } = false;

    /// <summary>
    /// When the package was archived.
    /// </summary>
    public DateTime? ArchivedAt { get; set; }

    public ICollection<Deployment> Deployments { get; set; } = new List<Deployment>();
}
