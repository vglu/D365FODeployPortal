namespace DeployPortal.Services.Deployment.Validation;

/// <summary>
/// Validator for deployment operations.
/// Implements Chain of Responsibility pattern — validators can be chained together.
/// </summary>
public interface IDeploymentValidator
{
    /// <summary>
    /// Validates deployment context.
    /// Throws InvalidOperationException if validation fails.
    /// </summary>
    /// <param name="context">Deployment context with all necessary information</param>
    /// <param name="onLog">Optional callback for logging validation steps</param>
    Task ValidateAsync(DeploymentContext context, Action<string>? onLog = null);
}
