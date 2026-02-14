namespace DeployPortal.Services.Deployment.Isolation;

/// <summary>
/// Default implementation of IIsolatedDirectoryManager.
/// Manages creation and cleanup of isolated directories for PAC CLI auth profiles.
/// </summary>
public class IsolatedDirectoryManager : IIsolatedDirectoryManager
{
    private readonly ISettingsService _settings;
    private readonly ILogger<IsolatedDirectoryManager> _logger;

    public IsolatedDirectoryManager(ISettingsService settings, ILogger<IsolatedDirectoryManager> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string CreateIsolatedDirectory(int deploymentId)
    {
        var tempDir = _settings.TempWorkingDir;
        var isolatedDir = Path.Combine(tempDir, $"pac_auth_{deploymentId}_{Guid.NewGuid():N}");

        Directory.CreateDirectory(isolatedDir);
        
        _logger.LogInformation("Created isolated PAC auth directory: {Dir}", isolatedDir);
        
        return isolatedDir;
    }

    public void DeleteIsolatedDirectory(string isolatedDirectory)
    {
        ArgumentNullException.ThrowIfNull(isolatedDirectory);

        try
        {
            if (Directory.Exists(isolatedDirectory))
            {
                Directory.Delete(isolatedDirectory, recursive: true);
                _logger.LogInformation("Deleted isolated PAC auth directory: {Dir}", isolatedDirectory);
            }
            else
            {
                _logger.LogDebug("Isolated directory does not exist, nothing to delete: {Dir}", isolatedDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete isolated PAC auth directory: {Dir}", isolatedDirectory);
            // Non-critical — deployment already succeeded/failed, just couldn't cleanup
        }
    }
}
