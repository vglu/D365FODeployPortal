namespace DeployPortal.Services.Deployment.Validation;

/// <summary>
/// Validator for deployment operations.
/// Implements Chain of Responsibility pattern — validators can be chained together.
/// </summary>
public interface IDeploymentValidator
{
    /// <summary>Whether this validator runs before or after <c>pac package deploy</c>.</summary>
    DeploymentValidationPhase Phase { get; }

    /// <summary>
    /// Validates deployment context.
    /// Throws InvalidOperationException if validation fails.
    /// </summary>
    Task ValidateAsync(DeploymentContext context, Action<string>? onLog = null);
}
