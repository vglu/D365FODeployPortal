namespace DeployPortal.Services;

/// <summary>
/// Application settings (runtime, persisted to user file). Abstraction for testability (DIP).
/// </summary>
public interface ISettingsService
{
    string ConverterEngine { get; }
    string ProcessingMode { get; }
    string AzureFunctionsUrl { get; }
    string AzureBlobConnectionString { get; }
    string AzureFunctionKey { get; }
    string ModelUtilPath { get; }
    string PacCliPath { get; }
    string PackageStoragePath { get; }
    string TempWorkingDir { get; }
    string DatabasePath { get; }
    string LcsTemplatePath { get; }
    bool SimulateDeployment { get; }
    string AzureDevOpsOrganization { get; }
    string AzureDevOpsProject { get; }
    string AzureDevOpsPatEncrypted { get; }
    string ReleasePipelineFeedName { get; }
    string UserSettingsFilePath { get; }

    /// <summary>Maximum number of concurrent deployments (1–20). Stored in database. Default: 2.</summary>
    int MaxConcurrentDeployments { get; }

    List<SettingsService.ToolStatus> ValidateTools();
    string GetEffectivePacPath();
    string GetEffectiveModelUtilPath();
    Dictionary<string, string> GetAllSettings();
    void SaveSettings(Dictionary<string, string> settings);
    void SaveAzureDevOpsSettings(string? patEncrypted, string? organization, string? project);
    void SaveReleasePipelineFeedName(string feedName);
}
