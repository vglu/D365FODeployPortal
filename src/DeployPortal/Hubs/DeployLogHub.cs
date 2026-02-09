using Microsoft.AspNetCore.SignalR;

namespace DeployPortal.Hubs;

public class DeployLogHub : Hub
{
    public async Task JoinDeploymentGroup(int deploymentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"deployment-{deploymentId}");
    }

    public async Task LeaveDeploymentGroup(int deploymentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"deployment-{deploymentId}");
    }
}
