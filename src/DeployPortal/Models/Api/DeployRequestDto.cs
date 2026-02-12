namespace DeployPortal.Models.Api;

/// <summary>Request body for starting a deployment.</summary>
public class DeployRequestDto
{
    public int PackageId { get; set; }
    public int EnvironmentId { get; set; }
    public string? DevOpsTaskUrl { get; set; }
}
