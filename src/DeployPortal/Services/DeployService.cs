using System.Diagnostics;

namespace DeployPortal.Services;

public class DeployService
{
    private readonly SettingsService _settings;
    private readonly SecretProtectionService _secretService;
    private readonly ILogger<DeployService> _logger;

    public DeployService(SettingsService settings, SecretProtectionService secretService, ILogger<DeployService> logger)
    {
        _settings = settings;
        _secretService = secretService;
        _logger = logger;
    }

    private string PacCliPath => _settings.GetEffectivePacPath();

    private string ModelUtilDir
    {
        get
        {
            var path = _settings.GetEffectiveModelUtilPath();
            return !string.IsNullOrEmpty(path) ? Path.GetDirectoryName(path)! : AppContext.BaseDirectory;
        }
    }

    /// <summary>
    /// Authenticates to Power Platform using Service Principal and deploys the package.
    /// </summary>
    public async Task DeployPackageAsync(
        Models.Environment environment,
        string unifiedPackageDir,
        string logFilePath,
        Action<string>? onLog = null)
    {
        var clientSecret = _secretService.Decrypt(environment.ClientSecretEncrypted);

        // Step 1: Authenticate
        onLog?.Invoke($"Authenticating to {environment.Url}...");
        await RunPacCommandAsync(
            $"auth create --applicationId {environment.ApplicationId} --clientSecret \"{clientSecret}\" --tenant {environment.TenantId} --environment {environment.Url}",
            onLog);

        // Step 2: Deploy
        var templateDll = Path.Combine(unifiedPackageDir, "TemplatePackage.dll");
        onLog?.Invoke($"Starting deployment to {environment.Name}...");
        onLog?.Invoke($"Package: {templateDll}");

        await RunPacCommandAsync(
            $"package deploy --logConsole --package \"{templateDll}\" --logFile \"{logFilePath}\"",
            onLog);

        onLog?.Invoke($"Deployment to {environment.Name} completed.");
    }

    private async Task RunPacCommandAsync(string arguments, Action<string>? onLog = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PacCliPath,
            Arguments = arguments,
            WorkingDirectory = ModelUtilDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputTask = ReadStreamAsync(process.StandardOutput, line =>
        {
            onLog?.Invoke(line);
            _logger.LogDebug("[PAC] {Line}", line);
        });

        var errorTask = ReadStreamAsync(process.StandardError, line =>
        {
            onLog?.Invoke($"[ERROR] {line}");
            _logger.LogWarning("[PAC Error] {Line}", line);
        });

        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);

        if (process.ExitCode != 0)
        {
            throw new Exception($"PAC CLI failed with exit code {process.ExitCode}");
        }
    }

    private static async Task ReadStreamAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                onLine(line);
        }
    }
}
