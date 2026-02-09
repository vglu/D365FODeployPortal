namespace DeployPortal.Models;

public enum DeploymentStatus
{
    Queued,
    Merging,
    Converting,
    Deploying,
    Success,
    Failed,
    Cancelled
}
