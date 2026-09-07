namespace DeployPortal.Services.Deployment.Validation;

/// <summary>
/// Detects Package Deployer / PAC CLI failure signals that may appear even when PAC exits with code 0.
/// </summary>
public static class PackageDeployFailureDetector
{
    // Explicit failure markers observed in PAC / PackageDeployer output.
    // Keep specific enough to avoid false positives on unrelated warnings.
    private static readonly string[] FailureMarkers =
    [
        "RaiseFailEvent",
        "Installation failed for Finance and Operations",
        "Error: Installation failed",
        "Failed to Load the Import Configuration",
        "Config File Missing",
        "Selected Plugin is null",
        "PackageDeployVerb Error",
        "Package deployment failed",
        "The installation of the package failed"
    ];

    /// <summary>
    /// Returns the first line that indicates deployment failure, or null if none found.
    /// </summary>
    public static string? FindFailureEvidence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();

            // PAC CLI summary lines: "Error: <message>"
            if (trimmed.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                && trimmed.Length > "Error:".Length)
            {
                return trimmed;
            }

            foreach (var marker in FailureMarkers)
            {
                if (trimmed.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return trimmed;
            }
        }

        return null;
    }

    public static bool HasFailure(string? text) => FindFailureEvidence(text) != null;
}
