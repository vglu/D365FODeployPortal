namespace DeployPortal.Models;

/// <summary>
/// Represents a D365FO model inside a package.
/// </summary>
public class ModelInfo
{
    /// <summary>
    /// Model name (e.g., "ApplicationSuite", "SISHeavyHighway").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Model version (e.g., "10.0.1234.5").
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Model publisher/vendor.
    /// </summary>
    public string? Publisher { get; set; }

    /// <summary>
    /// Size of the model package in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// File name of the model package (e.g., "Dynamics.AX.ApplicationSuite.1.0.0.0.nupkg").
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// List of dependencies (other models this model depends on).
    /// </summary>
    public List<string> Dependencies { get; set; } = new();
}

/// <summary>
/// Represents a license file inside a package.
/// </summary>
public class LicenseInfo
{
    /// <summary>
    /// License file name (e.g., "license_key.txt", "ISV_License.xml").
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Size of the license file in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// License file content (for preview/download).
    /// </summary>
    public byte[]? Content { get; set; }

    /// <summary>
    /// Content type (e.g., "text/plain", "application/xml").
    /// </summary>
    public string? ContentType { get; set; }
}
