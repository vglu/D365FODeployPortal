namespace DeployPortal.Services.Deployment.PacCli;

/// <summary>
/// Parses output of 'pac auth who' (e.g. Organization Friendly Name).
/// </summary>
public static class PacAuthWhoParser
{
    private const string OrganizationFriendlyNamePrefix = "Organization Friendly Name:";

    /// <summary>
    /// Extracts "Organization Friendly Name" value from 'pac auth who' output.
    /// Example line: "Organization Friendly Name:   C365 AFSPM (Unified)"
    /// </summary>
    /// <returns>Trimmed value or null if not found.</returns>
    public static string? ParseOrganizationFriendlyName(string whoOutput)
    {
        if (string.IsNullOrWhiteSpace(whoOutput)) return null;

        using var reader = new StringReader(whoOutput);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var idx = line.IndexOf(OrganizationFriendlyNamePrefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var value = line[(idx + OrganizationFriendlyNamePrefix.Length)..].Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }
}
