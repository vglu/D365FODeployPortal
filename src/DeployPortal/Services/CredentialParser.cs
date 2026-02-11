using System.Text.RegularExpressions;

namespace DeployPortal.Services;

/// <summary>
/// Parses Service Principal credentials from script output or saved files.
/// Handles output from Setup-ServicePrincipal.ps1 and manual instruction format.
/// </summary>
public class CredentialParser
{
    public record ParsedCredentials
    {
        public string? ApplicationId { get; init; }
        public string? TenantId { get; init; }
        public string? ClientSecret { get; init; }
        public string? SecretExpiry { get; init; }
        public string? ServicePrincipalName { get; init; }
        public List<string> Environments { get; init; } = new();

        public bool HasCredentials => !string.IsNullOrWhiteSpace(ApplicationId)
                                   && !string.IsNullOrWhiteSpace(TenantId);
        public bool HasSecret => !string.IsNullOrWhiteSpace(ClientSecret)
                              && ClientSecret != "*** SHOWN IN CONSOLE ***"
                              && ClientSecret != "Not created (add environment mode)";
        public bool HasEnvironments => Environments.Count > 0;
    }

    /// <summary>
    /// Parses credential data from text (clipboard paste or file content).
    /// Supports multiple formats from the Setup-ServicePrincipal.ps1 script output.
    /// </summary>
    public static ParsedCredentials Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ParsedCredentials();

        var applicationId = ExtractValue(text,
            @"Application\s*\(?\s*Client\s*\)?\s*ID\s*[:=]\s*(.+)",
            @"--applicationId\s+""([^""]+)""",
            @"applicationId\s*[:=]\s*(.+)");

        var tenantId = ExtractValue(text,
            @"Directory\s*\(?\s*Tenant\s*\)?\s*ID\s*[:=]\s*(.+)",
            @"--tenant\s+""([^""]+)""",
            @"[Tt]enant\s*(?:ID)?\s*[:=]\s*(.+)");

        var clientSecret = ExtractValue(text,
            @"Client\s*Secret\s*(?:\(Value\))?\s*[:=]\s*(.+)",
            @"--clientSecret\s+""([^""]+)""");

        var secretExpiry = ExtractValue(text,
            @"(?:Secret\s+expir\w*|Срок действия секрета|Секрет действует до)\s*[:=]\s*(.+)",
            @"(?:until|expires?|до)\s+([\d./-]+)");

        var spName = ExtractValue(text,
            @"Service\s*Principal\s*[:=]\s*(.+)",
            @"AppDisplayName\s*[:=]\s*(.+)");

        var environments = ExtractEnvironments(text);

        return new ParsedCredentials
        {
            ApplicationId = CleanValue(applicationId),
            TenantId = CleanValue(tenantId),
            ClientSecret = CleanValue(clientSecret),
            SecretExpiry = CleanValue(secretExpiry),
            ServicePrincipalName = CleanValue(spName),
            Environments = environments
        };
    }

    private static string? ExtractValue(string text, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                var val = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(val))
                    return val;
            }
        }
        return null;
    }

    private static string? CleanValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // Remove quotes, trailing commas, whitespace
        return value.Trim().Trim('"', '\'', ',', ' ', '\r', '\n');
    }

    private static List<string> ExtractEnvironments(string text)
    {
        var envs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pattern 1: "Environments (OK):  env1.crm.dynamics.com, env2.crm.dynamics.com"
        var envsMatch = Regex.Match(text,
            @"Environments?\s*\(?OK\)?\s*[:=]\s*(.+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (envsMatch.Success)
        {
            foreach (var env in envsMatch.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var cleaned = env.Trim().Trim('"', '\'');
                if (IsValidEnvironmentUrl(cleaned))
                    envs.Add(cleaned);
            }
        }

        // Pattern 2: --environment "xxx.crm.dynamics.com"
        foreach (Match m in Regex.Matches(text, @"--environment\s+""([^""]+)""", RegexOptions.IgnoreCase))
        {
            var val = m.Groups[1].Value.Trim();
            if (IsValidEnvironmentUrl(val))
                envs.Add(val);
        }

        // Pattern 3: Standalone URLs matching *.crm.dynamics.com or *.crm*.dynamics.com
        foreach (Match m in Regex.Matches(text,
            @"(?:^|[\s""',|`])([a-zA-Z0-9][\w-]*\.crm\d*\.dynamics\.com)(?:$|[\s""',|`])",
            RegexOptions.Multiline))
        {
            envs.Add(m.Groups[1].Value.Trim());
        }

        // Pattern 4: Quoted list items like "env1.crm.dynamics.com","env2..."
        foreach (Match m in Regex.Matches(text,
            @"""([a-zA-Z0-9][\w-]*\.crm\d*\.dynamics\.com)"""))
        {
            envs.Add(m.Groups[1].Value.Trim());
        }

        return envs.OrderBy(e => e).ToList();
    }

    private static bool IsValidEnvironmentUrl(string url)
    {
        return !string.IsNullOrWhiteSpace(url)
            && Regex.IsMatch(url, @"^[a-zA-Z0-9][\w-]*\.crm\d*\.dynamics\.com$", RegexOptions.IgnoreCase);
    }
}
