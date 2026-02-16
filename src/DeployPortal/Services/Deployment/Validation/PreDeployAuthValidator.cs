using DeployPortal.Services.Deployment.PacCli;

namespace DeployPortal.Services.Deployment.Validation;

/// <summary>
/// Pre-deployment validator: checks that PAC CLI authenticated to the correct environment.
/// Uses 'pac auth who' output; prefers Organization Friendly Name from DB when set.
/// </summary>
public class PreDeployAuthValidator : IDeploymentValidator
{
    private readonly ILogger<PreDeployAuthValidator> _logger;

    public PreDeployAuthValidator(ILogger<PreDeployAuthValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ValidateAsync(DeploymentContext context, Action<string>? onLog = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        // When "Additionally verify Friendly Name on deploy" is off, skip this validation entirely.
        if (!context.VerifyOrganizationFriendlyName)
        {
            onLog?.Invoke("[Pre-Deploy Validation] Skipped (verify Friendly Name is disabled in Settings).");
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(context.PacAuthWhoOutput))
        {
            throw new InvalidOperationException(
                "Cannot validate pre-deployment auth: 'pac auth who' output is missing in context. " +
                "Ensure PacAuthWhoOutput is set before calling this validator.");
        }

        onLog?.Invoke("[Pre-Deploy Validation] Verifying 'pac auth who' output...");

        var whoOutput = context.PacAuthWhoOutput;
        var whoFriendlyName = PacAuthWhoParser.ParseOrganizationFriendlyName(whoOutput);

        // If setting is on and we have Organization Friendly Name stored for this environment, require exact match
        if (context.VerifyOrganizationFriendlyName && !string.IsNullOrWhiteSpace(context.Environment.OrganizationFriendlyName))
        {
            var expectedFriendly = context.Environment.OrganizationFriendlyName.Trim();
            if (string.IsNullOrWhiteSpace(whoFriendlyName))
            {
                throw new InvalidOperationException(
                    $"❌ PRE-DEPLOYMENT VALIDATION FAILED! ❌\n" +
                    $"Expected Organization Friendly Name: {expectedFriendly}\n" +
                    $"But 'pac auth who' output does not contain 'Organization Friendly Name:' line.\n\n" +
                    $"'pac auth who' output:\n{whoOutput}");
            }

            if (!string.Equals(whoFriendlyName.Trim(), expectedFriendly, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "PRE-DEPLOYMENT VALIDATION FAILED! Expected Organization Friendly Name: {Expected}, actual: {Actual}",
                    expectedFriendly, whoFriendlyName);

                throw new InvalidOperationException(
                    $"❌ PRE-DEPLOYMENT VALIDATION FAILED! ❌\n" +
                    $"PAC CLI authenticated to WRONG environment!\n\n" +
                    $"Expected Organization Friendly Name: {expectedFriendly}\n" +
                    $"But 'pac auth who' shows: {whoFriendlyName}\n\n" +
                    $"This indicates the Service Principal may have access to multiple environments " +
                    $"and PAC CLI selected the wrong one.\n\n" +
                    $"'pac auth who' output:\n{whoOutput}");
            }

            onLog?.Invoke($"[Pre-Deploy Validation] ✓ Matched by Organization Friendly Name: {whoFriendlyName}");
            _logger.LogInformation(
                "Pre-deployment auth validation passed for environment: {Env} (Organization Friendly Name: {FriendlyName})",
                context.Environment.Url, whoFriendlyName);
            return Task.CompletedTask;
        }

        // Fallback: match by URL or environment name (legacy behavior)
        var expectedUrl = context.Environment.Url.ToLowerInvariant();
        var expectedName = context.Environment.Name.ToLowerInvariant();
        var whoOutputLower = whoOutput.ToLowerInvariant();
        var urlMatch = whoOutputLower.Contains(expectedUrl);
        var friendlyNameMatch = whoFriendlyName != null &&
            string.Equals(whoFriendlyName.Trim(), expectedName, StringComparison.OrdinalIgnoreCase);
        if (!friendlyNameMatch)
        {
            friendlyNameMatch = whoOutputLower.Contains($"organization friendly name: {expectedName}") ||
                                whoOutputLower.Contains($"organization: {expectedName}") ||
                                whoOutputLower.Contains($"default organization: {expectedName}");
        }

        if (!urlMatch && !friendlyNameMatch)
        {
            var errorMsg =
                $"❌ PRE-DEPLOYMENT VALIDATION FAILED! ❌\n" +
                $"PAC CLI authenticated to WRONG environment!\n\n" +
                $"Expected environment: {context.Environment.Name} ({context.Environment.Url})\n" +
                $"But 'pac auth who' output does not contain expected URL or Organization Name.\n\n" +
                $"This indicates the Service Principal may have access to multiple environments " +
                $"and PAC CLI selected the wrong one.\n\n" +
                $"'pac auth who' output:\n{context.PacAuthWhoOutput}";

            _logger.LogError(
                "PRE-DEPLOYMENT VALIDATION FAILED! Expected: {Expected}, 'pac auth who' output does not contain this URL or Name",
                context.Environment.Url);

            throw new InvalidOperationException(errorMsg);
        }

        var matchType = urlMatch ? "URL" : "Organization Friendly Name";
        onLog?.Invoke($"[Pre-Deploy Validation] ✓ Matched by {matchType}: {context.Environment.Name}");
        _logger.LogInformation(
            "Pre-deployment auth validation passed for environment: {Env} (matched by {MatchType})",
            context.Environment.Url, matchType);

        return Task.CompletedTask;
    }
}
