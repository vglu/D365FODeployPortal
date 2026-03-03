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

        // Fallback: any line containing "Friendly Name" and a colon (e.g. different spacing/encoding)
        foreach (var row in whoOutput.Split(["\r", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = row.Trim();
            if (trimmed.Contains("Friendly Name", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(':'))
            {
                var colonIdx = trimmed.LastIndexOf(':');
                if (colonIdx >= 0 && colonIdx < trimmed.Length - 1)
                {
                    var value = trimmed[(colonIdx + 1)..].Trim();
                    if (!string.IsNullOrEmpty(value)) return value;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes a string for comparison: trim and collapse consecutive whitespace to a single space.
    /// </summary>
    public static string NormalizeForCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var t = value.Trim();
        return string.Join(' ', t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
