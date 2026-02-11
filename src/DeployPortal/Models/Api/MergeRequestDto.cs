namespace DeployPortal.Models.Api;

public class MergeRequestDto
{
    public List<int> PackageIds { get; set; } = new();
    public string? MergeName { get; set; }
}
