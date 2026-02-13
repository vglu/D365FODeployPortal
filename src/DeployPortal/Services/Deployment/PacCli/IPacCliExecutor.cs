namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Result of PAC CLI command execution.
/// </summary>
public record PacCliResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    
    public bool IsSuccess => ExitCode == 0;
}

/// <summary>
/// Abstraction over Process execution for PAC CLI commands.
/// Enables testability and mocking.
/// </summary>
public interface IPacCliExecutor
{
    /// <summary>
    /// Executes a PAC CLI command with custom environment variables.
    /// </summary>
    /// <param name="arguments">PAC CLI arguments (e.g., "auth who")</param>
    /// <param name="workingDirectory">Working directory for the process</param>
    /// <param name="environmentVariables">Custom environment variables (e.g., PAC_AUTH_PROFILE_DIRECTORY)</param>
    /// <param name="onOutput">Optional callback for real-time stdout</param>
    /// <param name="onError">Optional callback for real-time stderr</param>
    /// <returns>Command execution result</returns>
    Task<PacCliResult> ExecuteAsync(
        string arguments,
        string workingDirectory,
        IDictionary<string, string>? environmentVariables = null,
        Action<string>? onOutput = null,
        Action<string>? onError = null);
}
