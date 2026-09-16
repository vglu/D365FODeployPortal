using DeployPortal.Services.Deployment.Isolation;
using DeployPortal.Services.Deployment.PacCli;
using DeployPortal.Services.Deployment.Validation;

namespace DeployPortal.Services.Deployment;

/// <summary>
/// Main orchestrator for D365FO package deployments.
/// Coordinates authentication, validation (pre and post), and deployment using injected services.
/// Follows Single Responsibility Principle — delegates specific tasks to specialized services.
/// </summary>
public class DeployService : IDeployService
{
    private readonly IPacAuthService _authService;
    private readonly IPacDeploymentService _deploymentService;
    private readonly IIsolatedDirectoryManager _directoryManager;
    private readonly IEnumerable<IDeploymentValidator> _validators;
    private readonly ISettingsService _settings;
    private readonly ILogger<DeployService> _logger;

    public DeployService(
        IPacAuthService authService,
        IPacDeploymentService deploymentService,
        IIsolatedDirectoryManager directoryManager,
        IEnumerable<IDeploymentValidator> validators,
        ISettingsService settings,
        ILogger<DeployService> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _deploymentService = deploymentService ?? throw new ArgumentNullException(nameof(deploymentService));
        _directoryManager = directoryManager ?? throw new ArgumentNullException(nameof(directoryManager));
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task DeployPackageAsync(
        Models.Environment environment,
        string unifiedPackageDir,
        string logFilePath,
        string isolatedAuthDir,
        Action<string>? onLog = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(unifiedPackageDir);
        ArgumentNullException.ThrowIfNull(logFilePath);
        ArgumentNullException.ThrowIfNull(isolatedAuthDir);

        Directory.CreateDirectory(isolatedAuthDir);
        onLog?.Invoke($"[Isolation] Using dedicated PAC auth directory: {isolatedAuthDir}");
        _logger.LogInformation("Starting deployment to {Env} using isolated auth dir: {Dir}", environment.Name, isolatedAuthDir);

        string? deployZipPath = null;
        try
        {
            // Exclusive bind window: auth create/select + who + FO probe must not overlap
            // another deployment's auth (parallel long installs start after this releases).
            using (await _authService.AcquireAuthBindGateAsync(onLog))
            {
                await _authService.AuthenticateAsync(environment, isolatedAuthDir, onLog);

                onLog?.Invoke("Verifying connection (pac auth who)...");
                var whoOutput = await _authService.WhoAmIAsync(isolatedAuthDir);
                onLog?.Invoke("Connection verified.");

                var packagePath = Path.Combine(unifiedPackageDir, "TemplatePackage.dll");
                if (!File.Exists(packagePath))
                    throw new FileNotFoundException($"TemplatePackage.dll not found in {unifiedPackageDir}", packagePath);

                var importConfigPath = Path.Combine(unifiedPackageDir, "PackageAssets", "ImportConfig.xml");
                if (!File.Exists(importConfigPath))
                {
                    throw new FileNotFoundException(
                        "ImportConfig.xml not found next to TemplatePackage.dll. " +
                        $"Looked for: {importConfigPath}",
                        importConfigPath);
                }

                // One zip for FO host probe + package deploy (DLL + PackageAssets stay together).
                onLog?.Invoke("Packaging Unified folder for PAC (probe + deploy)...");
                deployZipPath = UnifiedPackageZipBuilder.CreateZip(unifiedPackageDir);
                onLog?.Invoke($"Unified zip: {Path.GetFileName(deployZipPath)}");

                var context = new DeploymentContext
                {
                    Environment = environment,
                    IsolatedAuthDir = isolatedAuthDir,
                    LogFilePath = logFilePath,
                    PackagePath = packagePath,
                    DeployZipPath = deployZipPath,
                    PacAuthWhoOutput = whoOutput,
                    VerifyOrganizationFriendlyName = _settings.VerifyOrganizationFriendlyNameOnDeploy,
                    VerifyFoHostReadyOnDeploy = _settings.VerifyFoHostReadyOnDeploy
                };

                await RunValidatorsAsync(context, DeploymentValidationPhase.PreDeploy, onLog);

                // Re-select after probe so package deploy cannot pick up another profile.
                await _authService.SelectProfileAsync(environment, isolatedAuthDir, onLog);

                if (_settings.SimulateDeployment)
                {
                    onLog?.Invoke("[SIMULATION] Deployment is disabled in Settings. Package deploy skipped. Auth and connection check completed successfully.");
                    onLog?.Invoke($"Deployment to {environment.Name} completed (simulated).");
                    _logger.LogInformation("Deployment simulated (SimulateDeployment = true)");
                    return;
                }

                onLog?.Invoke($"Starting deployment to {environment.Name}...");
            }

            // Gate released: long FnO import may overlap other deployments' auth/bind windows.
            var dllPath = Path.Combine(unifiedPackageDir, "TemplatePackage.dll");
            await _deploymentService.DeployAsync(dllPath, logFilePath, isolatedAuthDir, onLog, deployZipPath);
            onLog?.Invoke($"PAC package deploy finished for {environment.Name}.");

            var postContext = new DeploymentContext
            {
                Environment = environment,
                IsolatedAuthDir = isolatedAuthDir,
                LogFilePath = logFilePath,
                PackagePath = dllPath,
                DeployZipPath = deployZipPath,
                VerifyOrganizationFriendlyName = _settings.VerifyOrganizationFriendlyNameOnDeploy,
                VerifyFoHostReadyOnDeploy = _settings.VerifyFoHostReadyOnDeploy
            };
            await RunValidatorsAsync(postContext, DeploymentValidationPhase.PostDeploy, onLog);
            onLog?.Invoke("[Post-Deploy Validation] Confirmed: no failure markers; package targeted the expected environment.");

            _logger.LogInformation("Deployment to {Env} completed successfully", environment.Name);
        }
        finally
        {
            if (!string.IsNullOrEmpty(deployZipPath))
            {
                try
                {
                    if (File.Exists(deployZipPath))
                        File.Delete(deployZipPath);
                }
                catch
                {
                    /* ignore */
                }
            }

            onLog?.Invoke($"[Cleanup] Removing isolated PAC auth directory...");
            _directoryManager.DeleteIsolatedDirectory(isolatedAuthDir);
            onLog?.Invoke($"[Cleanup] Removed isolated PAC auth directory: {isolatedAuthDir}");
        }
    }

    private async Task RunValidatorsAsync(
        DeploymentContext context,
        DeploymentValidationPhase phase,
        Action<string>? onLog)
    {
        var applicableValidators = _validators
            .Where(v => v.Phase == phase)
            .ToList();

        if (applicableValidators.Count == 0)
        {
            _logger.LogWarning("No {Phase} validators found", phase);
            return;
        }

        foreach (var validator in applicableValidators)
        {
            _logger.LogDebug("Running {Phase} validator: {ValidatorType}", phase, validator.GetType().Name);
            await validator.ValidateAsync(context, onLog);
        }
    }
}
