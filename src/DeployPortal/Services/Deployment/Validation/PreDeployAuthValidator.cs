using DeployPortal.Services.Deployment.PacCli;

namespace DeployPortal.Services.Deployment.Validation;

/// <summary>
/// Pre-deployment validator: checks that PAC CLI authenticated to the correct environment.
/// Always requires a bind signal from 'pac auth who' (URL host and/or org friendly name vs Environment.Name).
/// Optional stricter Friendly Name match when Settings toggle is on and DB value is set.
/// </summary>
public class PreDeployAuthValidator : IDeploymentValidator
{
    private readonly ILogger<PreDeployAuthValidator> _logger;

    public PreDeployAuthValidator(ILogger<PreDeployAuthValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DeploymentValidationPhase Phase => DeploymentValidationPhase.PreDeploy;

    public Task ValidateAsync(DeploymentContext context, Action<string>? onLog = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.PacAuthWhoOutput))
        {
            throw new InvalidOperationException(
                "Cannot validate pre-deployment auth: 'pac auth who' output is missing in context. " +
                "Ensure PacAuthWhoOutput is set before calling this validator.");
        }

        onLog?.Invoke("[Pre-Deploy Validation] Verifying PAC auth is bound to the selected environment...");

        var whoOutput = context.PacAuthWhoOutput;
        var expectedUrl = (context.Environment.Url ?? "").Trim().TrimEnd('/');
        var expectedName = (context.Environment.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(expectedUrl) && string.IsNullOrWhiteSpace(expectedName))
        {
            throw new InvalidOperationException(
                "Cannot validate pre-deployment auth: environment URL and Name are empty.");
        }

        var whoLower = whoOutput.ToLowerInvariant();
        var urlHost = expectedUrl.ToLowerInvariant()
            .Replace("https://", "", StringComparison.Ordinal)
            .Replace("http://", "", StringComparison.Ordinal)
            .TrimEnd('/');

        // PAC auth who often has Friendly Name / Default organization, not the CRM URL.
        var whoFriendly = PacAuthWhoParser.ParseOrganizationFriendlyName(whoOutput);
        var urlMatch = !string.IsNullOrWhiteSpace(urlHost) &&
                       whoLower.Contains(urlHost, StringComparison.Ordinal);
        var nameMatch = !string.IsNullOrWhiteSpace(expectedName) &&
                        (string.Equals(
                             PacAuthWhoParser.NormalizeForCompare(whoFriendly),
                             PacAuthWhoParser.NormalizeForCompare(expectedName),
                             StringComparison.OrdinalIgnoreCase) ||
                         whoLower.Contains($"default organization: {expectedName.ToLowerInvariant()}") ||
                         whoLower.Contains($"connected to... {expectedName.ToLowerInvariant()}"));

        if (!urlMatch && !nameMatch)
        {
            throw new InvalidOperationException(
                "PRE-DEPLOYMENT VALIDATION FAILED: PAC auth is not bound to the selected environment.\n\n" +
                $"Expected environment: {expectedName} ({expectedUrl})\n" +
                $"Organization Friendly Name from who: {whoFriendly ?? "(missing)"}\n\n" +
                $"'pac auth who' output:\n{whoOutput}");
        }

        onLog?.Invoke(urlMatch
            ? $"[Pre-Deploy Validation] Auth who contains expected URL host: {urlHost}"
            : $"[Pre-Deploy Validation] Auth who org matches environment name: {expectedName}");

        // Optional extra: stored Friendly Name when enabled.
        if (context.VerifyOrganizationFriendlyName &&
            !string.IsNullOrWhiteSpace(context.Environment.OrganizationFriendlyName))
        {
            var expectedFriendly = context.Environment.OrganizationFriendlyName.Trim();
            if (string.IsNullOrWhiteSpace(whoFriendly))
            {
                throw new InvalidOperationException(
                    "PRE-DEPLOYMENT VALIDATION FAILED: Organization Friendly Name expected but missing in 'pac auth who'.\n\n" +
                    $"Expected: {expectedFriendly}\n\n" +
                    $"'pac auth who' output:\n{whoOutput}");
            }

            var expectedNorm = PacAuthWhoParser.NormalizeForCompare(expectedFriendly);
            var whoNorm = PacAuthWhoParser.NormalizeForCompare(whoFriendly);
            if (!string.Equals(whoNorm, expectedNorm, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "PRE-DEPLOYMENT VALIDATION FAILED! Expected Organization Friendly Name: {Expected}, actual: {Actual}",
                    expectedFriendly, whoFriendly);

                throw new InvalidOperationException(
                    "PRE-DEPLOYMENT VALIDATION FAILED: Organization Friendly Name mismatch.\n\n" +
                    $"Expected: {expectedFriendly}\n" +
                    $"Actual: {whoFriendly}\n\n" +
                    $"'pac auth who' output:\n{whoOutput}");
            }

            onLog?.Invoke($"[Pre-Deploy Validation] Stored Friendly Name matched: {whoFriendly}");
        }
        else if (!context.VerifyOrganizationFriendlyName)
        {
            onLog?.Invoke("[Pre-Deploy Validation] Extra Friendly Name setting off; bind check by URL/name applied.");
        }

        _logger.LogInformation(
            "Pre-deployment auth validation passed for environment: {Env}",
            context.Environment.Url);

        return Task.CompletedTask;
    }
}
