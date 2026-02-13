namespace DeployPortal.Models.Api;

public class FromBuildRequestDto
{
    public string Organization { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public int BuildId { get; set; }
    public string ArtifactName { get; set; } = string.Empty;
    /// <summary>PAT with Build (Read) scope. Can be passed in header X-AzureDevOps-PAT instead.</summary>
    public string? Pat { get; set; }
}
