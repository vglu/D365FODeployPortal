using System.ComponentModel.DataAnnotations;

namespace DeployPortal.Models;

public class Environment
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    /// <summary>Optional for interactive sign-in mode (device code at deploy time).</summary>
    [MaxLength(100)]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Optional. When empty, deploy uses device code flow (user signs in at deploy time).</summary>
    [MaxLength(100)]
    public string ApplicationId { get; set; } = string.Empty;

    /// <summary>Optional. When empty with ApplicationId, environment uses interactive sign-in at deploy.</summary>
    public string ClientSecretEncrypted { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Deployment> Deployments { get; set; } = new List<Deployment>();

    /// <summary>True when environment has Service Principal credentials (non-interactive deploy and API).</summary>
    public bool HasServicePrincipal =>
        !string.IsNullOrWhiteSpace(ApplicationId) && !string.IsNullOrWhiteSpace(ClientSecretEncrypted);
}
