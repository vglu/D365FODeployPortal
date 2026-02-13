using System.Diagnostics;

namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Default implementation of IPacCliExecutor using System.Diagnostics.Process.
/// Executes PAC CLI commands with custom environment variables.
/// </summary>
public class PacCliExecutor : IPacCliExecutor
{
    private readonly string _pacCliPath;
    private readonly ILogger<PacCliExecutor> _logger;

    public PacCliExecutor(string pacCliPath, ILogger<PacCliExecutor> logger)
    {
        _pacCliPath = pacCliPath ?? throw new ArgumentNullException(nameof(pacCliPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PacCliResult> ExecuteAsync(
        string arguments,
        string workingDirectory,
        IDictionary<string, string>? environmentVariables = null,
        Action<string>? onOutput = null,
        Action<string>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = _pacCliPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Apply custom environment variables (e.g., PAC_AUTH_PROFILE_DIRECTORY)
        if (environmentVariables != null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                psi.EnvironmentVariables[key] = value;
                _logger.LogDebug("Set environment variable: {Key}={Value}", key, value);
            }
        }

        using var process = new Process { StartInfo = psi };
        
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
                onOutput?.Invoke(e.Data);
                _logger.LogDebug("[PAC] {Line}", e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                errorBuilder.AppendLine(e.Data);
                onError?.Invoke(e.Data);
                _logger.LogWarning("[PAC Error] {Line}", e.Data);
            }
        };

        _logger.LogInformation("Executing PAC CLI: {Path} {Args}", _pacCliPath, arguments);
        
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        await process.WaitForExitAsync();

        var result = new PacCliResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = outputBuilder.ToString(),
            StandardError = errorBuilder.ToString()
        };

        if (result.ExitCode != 0)
        {
            _logger.LogError(
                "PAC CLI failed with exit code {ExitCode}. Error: {Error}",
                result.ExitCode,
                result.StandardError);
        }
        else
        {
            _logger.LogInformation("PAC CLI completed successfully");
        }

        return result;
    }
}
