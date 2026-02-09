using System.ComponentModel.DataAnnotations;

namespace DeployPortal.Models;

public class Deployment
{
    public int Id { get; set; }

    public int PackageId { get; set; }
    public Package Package { get; set; } = null!;

    public int EnvironmentId { get; set; }
    public Environment Environment { get; set; } = null!;

    public DeploymentStatus Status { get; set; } = DeploymentStatus.Queued;

    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [MaxLength(1000)]
    public string? LogFilePath { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Link to Azure DevOps work item / task for traceability.
    /// Pre-filled from Package.DevOpsTaskUrl but can be overridden per deployment.
    /// </summary>
    [MaxLength(1000)]
    public string? DevOpsTaskUrl { get; set; }

    public ICollection<DeploymentLog> Logs { get; set; } = new List<DeploymentLog>();
}
