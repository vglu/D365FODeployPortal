using System.ComponentModel.DataAnnotations;

namespace DeployPortal.Models;

public class Environment
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string TenantId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ApplicationId { get; set; } = string.Empty;

    [Required]
    public string ClientSecretEncrypted { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Deployment> Deployments { get; set; } = new List<Deployment>();
}
