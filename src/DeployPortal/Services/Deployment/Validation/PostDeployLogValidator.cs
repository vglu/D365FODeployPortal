namespace DeployPortal.Services.Deployment.Validation;

/// <summary>
/// Post-deployment validator: parses deployment log file for install failures and verifies
/// "Deployment Target Organization Uri" (correct environment).
/// </summary>
public class PostDeployLogValidator : IDeploymentValidator
{
    private readonly ILogger<PostDeployLogValidator> _logger;

    public PostDeployLogValidator(ILogger<PostDeployLogValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ValidateAsync(DeploymentContext context, Action<string>? onLog = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        onLog?.Invoke("[Post-Deploy Validation] Verifying deployment outcome from log file...");

        // Wait a moment for log file to be fully written
        await Task.Delay(1000);

        if (!File.Exists(context.LogFilePath))
        {
            _logger.LogWarning("Log file not found for post-deployment validation: {LogPath}", context.LogFilePath);
            onLog?.Invoke($"[Warning] Log file not found, skipping post-deployment validation: {context.LogFilePath}");
            return;
        }

        try
        {
            var logContent = await File.ReadAllTextAsync(context.LogFilePath);

            // PAC may exit 0 while Package Deployer still reports FO install failure.
            var failureEvidence = PackageDeployFailureDetector.FindFailureEvidence(logContent);
            if (failureEvidence != null)
            {
                var errorMsg =
                    $"POST-DEPLOYMENT VALIDATION FAILED: package install reported failure in log.\n\n" +
                    $"Evidence: {failureEvidence}\n\n" +
                    $"Environment: {context.Environment.Name} ({context.Environment.Url})\n" +
                    $"Log file: {context.LogFilePath}";

                _logger.LogError(
                    "POST-DEPLOYMENT VALIDATION FAILED (install failure). Evidence: {Evidence}",
                    failureEvidence);

                onLog?.Invoke($"[Post-Deploy Validation] Install failure detected: {failureEvidence}");
                throw new InvalidOperationException(errorMsg);
            }

            onLog?.Invoke("[Post-Deploy Validation] No install-failure markers found in log.");
            onLog?.Invoke("[Post-Deploy Validation] Verifying deployment target from log file...");

            // Find the line with "Deployment Target Organization Uri:"
            // Example: "PackageDeployVerb Information: 8 : Message: Deployment Target Organization Uri: https://target-env.crm.dynamics.com/XRMServices/2011/Organization.svc/web?SDKClientVersion=9.2.49.14828"
            var uriLinePrefix = "Deployment Target Organization Uri:";
            var uriLine = logContent
                .Split('\n')
                .FirstOrDefault(line => line.Contains(uriLinePrefix, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(uriLine))
            {
                _logger.LogWarning("Could not find 'Deployment Target Organization Uri' in log file: {LogPath}", context.LogFilePath);
                onLog?.Invoke($"[Warning] Could not find Organization Uri in log file, skipping target validation.");
                return;
            }

            // Extract the URL from the line
            var uriStartIdx = uriLine.IndexOf(uriLinePrefix, StringComparison.OrdinalIgnoreCase) + uriLinePrefix.Length;
            var actualUri = uriLine.Substring(uriStartIdx).Trim();

            // Check if the actual URI contains the expected environment URL
            // Expected: context.Environment.Url = "target-env.crm.dynamics.com"
            // Actual: "https://target-env.crm.dynamics.com/XRMServices/2011/Organization.svc/web?SDKClientVersion=..."
            if (!actualUri.Contains(context.Environment.Url, StringComparison.OrdinalIgnoreCase))
            {
                // CRITICAL ERROR: Deployed to wrong environment!
                var errorMsg =
                    $"POST-DEPLOYMENT VALIDATION FAILED!\n" +
                    $"Package was deployed to WRONG environment!\n\n" +
                    $"Expected environment: {context.Environment.Name} ({context.Environment.Url})\n" +
                    $"Actual deployment target (from log): {actualUri}\n\n" +
                    $"This indicates a critical deployment routing issue. The package is now on the wrong environment!\n" +
                    $"Log file: {context.LogFilePath}";

                _logger.LogError(
                    "POST-DEPLOYMENT VALIDATION FAILED! Expected: {Expected}, Actual: {Actual}",
                    context.Environment.Url,
                    actualUri);

                throw new InvalidOperationException(errorMsg);
            }

            onLog?.Invoke($"[Post-Deploy Validation] Organization Uri from log: {actualUri}");
            onLog?.Invoke($"[Post-Deploy Validation] Matches expected environment: {context.Environment.Url}");

            _logger.LogInformation(
                "Post-deployment validation passed. Deployed to correct environment: {Env}",
                context.Environment.Url);
        }
        catch (InvalidOperationException)
        {
            // Re-throw validation failures
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during post-deployment log validation: {LogPath}", context.LogFilePath);
            onLog?.Invoke($"[Warning] Error validating deployment log: {ex.Message}");
            // Don't fail the deployment if we can't parse the log
        }
    }
}
