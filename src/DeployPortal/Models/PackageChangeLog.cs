using System.ComponentModel.DataAnnotations;

namespace DeployPortal.Models;

/// <summary>
/// Tracks modifications made to packages (adding/removing models and licenses).
/// Provides audit trail for package content changes.
/// </summary>
public class PackageChangeLog
{
    public int Id { get; set; }

    /// <summary>
    /// Reference to the package that was modified.
    /// </summary>
    public int PackageId { get; set; }
    public Package Package { get; set; } = null!;

    /// <summary>
    /// Type of change performed.
    /// </summary>
    public PackageChangeType ChangeType { get; set; }

    /// <summary>
    /// What was changed: "Model" or "License".
    /// </summary>
    [Required, MaxLength(50)]
    public string ItemType { get; set; } = string.Empty;

    /// <summary>
    /// Name of the model or license file that was added/removed.
    /// Examples: "ApplicationSuite", "license_key.txt"
    /// </summary>
    [Required, MaxLength(500)]
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Additional details about the change (e.g., file size, model version).
    /// </summary>
    [MaxLength(2000)]
    public string? Details { get; set; }

    /// <summary>
    /// User who made the change (from authentication context).
    /// </summary>
    [MaxLength(200)]
    public string? ChangedBy { get; set; }

    /// <summary>
    /// When the change was made.
    /// </summary>
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional: Hash of the package file before the change (for versioning/rollback support).
    /// Reserved for future use.
    /// </summary>
    [MaxLength(128)]
    public string? PackageHashBefore { get; set; }
}

/// <summary>
/// Type of change performed on a package.
/// </summary>
public enum PackageChangeType
{
    /// <summary>
    /// A model or license was added to the package.
    /// </summary>
    Added = 1,

    /// <summary>
    /// A model or license was removed from the package.
    /// </summary>
    Removed = 2
}
