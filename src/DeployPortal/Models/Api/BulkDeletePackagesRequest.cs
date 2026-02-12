namespace DeployPortal.Models.Api;

public class BulkDeletePackagesRequest
{
    public List<int> Ids { get; set; } = new();
}
