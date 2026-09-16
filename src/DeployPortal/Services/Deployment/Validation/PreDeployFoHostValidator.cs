using DeployPortal.Services.Deployment.PacCli;

namespace DeployPortal.Services.Deployment.Validation;

/// <summary>
/// Pre-deploy probe: runs <c>pac package show</c> against the Unified zip so Package Deployer
/// reads FO Application Host state before a long <c>pac package deploy</c>.
/// Fails early on ExternalOrchestration / EnvironmentNotInReadyState / host mismatch.
/// </summary>
public class PreDeployFoHostValidator : IDeploymentValidator
{
    private readonly IPacCliExecutor _pacExecutor;
    private readonly ILogger<PreDeployFoHostValidator> _logger;

    public PreDeployFoHostValidator(
        IPacCliExecutor pacExecutor,
        ILogger<PreDeployFoHostValidator> logger)
    {
        _pacExecutor = pacExecutor ?? throw new ArgumentNullException(nameof(pacExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DeploymentValidationPhase Phase => DeploymentValidationPhase.PreDeploy;

    public async Task ValidateAsync(DeploymentContext context, Action<string>? onLog = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.VerifyFoHostReadyOnDeploy)
        {
            onLog?.Invoke("[Pre-Deploy FO Host] Skipped (probe disabled in Settings).");
            return;
        }

        if (string.IsNullOrWhiteSpace(context.DeployZipPath) || !File.Exists(context.DeployZipPath))
        {
            throw new InvalidOperationException(
                "Cannot probe FO Application Host: Unified deploy zip is missing. " +
                "Ensure DeployZipPath is set before PreDeployFoHostValidator runs.");
        }

        var packageDir = Path.GetDirectoryName(context.PackagePath)
            ?? throw new InvalidOperationException($"Cannot resolve directory for package: {context.PackagePath}");

        onLog?.Invoke("[Pre-Deploy FO Host] Probing Application Host via pac package show (before full deploy)...");
        _logger.LogInformation(
            "FO host probe: package show on {Zip} for env {Env}",
            context.DeployZipPath,
            context.Environment.Name);

        var envVars = new Dictionary<string, string>
        {
            ["PAC_AUTH_PROFILE_DIRECTORY"] = context.IsolatedAuthDir
        };

        var arguments = $"package show --package \"{context.DeployZipPath}\" --verbose";
        var result = await _pacExecutor.ExecuteAsync(
            arguments,
            packageDir,
            envVars,
            onOutput: line => onLog?.Invoke($"[FO Host Probe] {line}"),
            onError: line => onLog?.Invoke($"[FO Host Probe][ERROR] {line}"));

        var combined = $"{result.StandardOutput}\n{result.StandardError}";
        var busy = PackageDeployFailureDetector.FindFoHostBusyEvidence(combined);
        if (busy != null)
        {
            _logger.LogWarning("FO host probe failed (busy/not ready): {Evidence}", busy);
            throw new InvalidOperationException(
                "PRE-DEPLOY FO HOST CHECK FAILED: the Finance and Operations Application Host " +
                "will not accept packages right now.\n\n" +
                $"{busy}\n\n" +
                "Typical causes: another package install, servicing, copy/sync, or ExternalOrchestration. " +
                "Wait until the host is ready, then retry. Full package deploy was not started.");
        }

        // Host-busy markers are the main goal. Non-zero exit without those markers is logged but
        // does not block deploy (package show can fail for unrelated reasons on some PAC versions).
        if (!result.IsSuccess)
        {
            onLog?.Invoke(
                $"[Pre-Deploy FO Host] Warning: pac package show exited {result.ExitCode} " +
                "without FO host-busy markers; continuing to deploy.");
            _logger.LogWarning(
                "FO host probe exited {Code} without busy markers for {Env}",
                result.ExitCode,
                context.Environment.Name);
            return;
        }

        onLog?.Invoke("[Pre-Deploy FO Host] Probe OK — no ExternalOrchestration / host-busy markers.");
        _logger.LogInformation("FO host probe passed for {Env}", context.Environment.Name);
    }
}
