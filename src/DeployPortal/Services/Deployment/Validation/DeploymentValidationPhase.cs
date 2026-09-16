namespace DeployPortal.Services.Deployment.Validation;

/// <summary>
/// When a deployment validator runs: before package deploy or after.
/// </summary>
public enum DeploymentValidationPhase
{
    PreDeploy,
    PostDeploy
}
