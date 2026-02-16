namespace DeployPortal.Services.Deployment;

/// <summary>
/// Context for deployment validation.
/// Contains all information needed by validators to perform their checks.
/// </summary>
public class DeploymentContext
{
    public required Models.Environment Environment { get; init; }
    public required string IsolatedAuthDir { get; init; }
    public required string LogFilePath { get; init; }
    public required string PackagePath { get; init; }
    
    /// <summary>
    /// Output from 'pac auth who' command.
    /// Used by PreDeployAuthValidator.
    /// </summary>
    public string? PacAuthWhoOutput { get; set; }

    /// <summary>
    /// When true, PreDeployAuthValidator requires Organization Friendly Name match (if set on environment).
    /// From Settings: VerifyOrganizationFriendlyNameOnDeploy.
    /// </summary>
    public bool VerifyOrganizationFriendlyName { get; set; }
}
