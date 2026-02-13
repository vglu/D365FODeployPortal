namespace DeployPortal.Services.Deployment.Validation;

/// <summary>
/// Pre-deployment validator: checks that PAC CLI authenticated to the correct environment.
/// Uses 'pac auth who' output to verify environment URL.
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

        if (string.IsNullOrWhiteSpace(context.PacAuthWhoOutput))
        {
            throw new InvalidOperationException(
                "Cannot validate pre-deployment auth: 'pac auth who' output is missing in context. " +
                "Ensure PacAuthWhoOutput is set before calling this validator.");
        }

        onLog?.Invoke("[Pre-Deploy Validation] Verifying 'pac auth who' output...");

        var expectedUrl = context.Environment.Url.ToLowerInvariant();
        var expectedName = context.Environment.Name.ToLowerInvariant();
        var whoOutput = context.PacAuthWhoOutput;
        var whoOutputLower = whoOutput.ToLowerInvariant();

        // Check 1: Try to match by URL (e.g., "cst-hfx-tst-07.crm.dynamics.com")
        // This works for Service Principal authentication
        var urlMatch = whoOutputLower.Contains(expectedUrl);

        // Check 2: Try to match by Organization Friendly Name (e.g., "CST-HFX-TST-07")
        // Example line: "Organization Friendly Name: CST-HFX-TST-07"
        var friendlyNameMatch = whoOutputLower.Contains($"organization friendly name: {expectedName}") ||
                                whoOutputLower.Contains($"organization: {expectedName}") ||
                                whoOutputLower.Contains($"default organization: {expectedName}");

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
            context.Environment.Url,
            matchType);

        return Task.CompletedTask;
    }
}
