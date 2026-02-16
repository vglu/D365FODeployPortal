namespace DeployPortal.Models.Api;

/// <summary>Environment data for export/import (backup restore). ClientSecretEncrypted is included for same-machine restore.</summary>
public class EnvironmentExportDto
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ApplicationId { get; set; } = string.Empty;
    public string ClientSecretEncrypted { get; set; } = string.Empty;
    public string? OrganizationFriendlyName { get; set; }
    public bool IsActive { get; set; } = true;
}
