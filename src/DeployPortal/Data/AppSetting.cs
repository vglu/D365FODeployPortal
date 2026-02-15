namespace DeployPortal.Data;

/// <summary>
/// Key-value setting stored in the database (e.g. MaxConcurrentDeployments).
/// Used for settings that should be configurable from the UI and persisted in DB.
/// </summary>
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
