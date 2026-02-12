using DeployPortal.Models;

namespace DeployPortal.Models.Api;

/// <summary>Deployment summary for API responses.</summary>
public class DeploymentDto
{
    public int Id { get; set; }
    public int PackageId { get; set; }
    public int EnvironmentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DevOpsTaskUrl { get; set; }

    public static DeploymentDto From(Deployment d)
    {
        return new DeploymentDto
        {
            Id = d.Id,
            PackageId = d.PackageId,
            EnvironmentId = d.EnvironmentId,
            Status = d.Status.ToString(),
            QueuedAt = d.QueuedAt,
            StartedAt = d.StartedAt,
            CompletedAt = d.CompletedAt,
            ErrorMessage = d.ErrorMessage,
            DevOpsTaskUrl = d.DevOpsTaskUrl
        };
    }
}
