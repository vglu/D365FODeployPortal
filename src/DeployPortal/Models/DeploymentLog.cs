using System.ComponentModel.DataAnnotations;

namespace DeployPortal.Models;

public class DeploymentLog
{
    public long Id { get; set; }

    public int DeploymentId { get; set; }
    public Deployment Deployment { get; set; } = null!;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(20)]
    public string Level { get; set; } = "Info"; // Info, Warning, Error

    [Required]
    public string Message { get; set; } = string.Empty;
}
