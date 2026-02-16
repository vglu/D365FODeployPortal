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

        // Step 0: Create isolated directory
        Directory.CreateDirectory(isolatedAuthDir);
        onLog?.Invoke($"[Isolation] Using dedicated PAC auth directory: {isolatedAuthDir}");
        _logger.LogInformation("Starting deployment to {Env} using isolated auth dir: {Dir}", environment.Name, isolatedAuthDir);

        try
        {
            // Step 1: Authenticate
            await _authService.AuthenticateAsync(environment, isolatedAuthDir, onLog);

            // Step 2: Verify connection (who am I)
            onLog?.Invoke("Verifying connection (pac auth who)...");
            var whoOutput = await _authService.WhoAmIAsync(isolatedAuthDir);
            onLog?.Invoke("Connection verified.");

            // Step 3: Pre-deployment validation (CHECK 1)
            var packagePath = Path.Combine(unifiedPackageDir, "TemplatePackage.dll");
            var context = new DeploymentContext
            {
                Environment = environment,
                IsolatedAuthDir = isolatedAuthDir,
                LogFilePath = logFilePath,
                PackagePath = packagePath,
                PacAuthWhoOutput = whoOutput,
                VerifyOrganizationFriendlyName = _settings.VerifyOrganizationFriendlyNameOnDeploy
            };

            await RunValidatorsAsync(context, isPreDeploy: true, onLog);

            // Step 4: Check simulation mode
            if (_settings.SimulateDeployment)
            {
                onLog?.Invoke("[SIMULATION] Deployment is disabled in Settings. Package deploy skipped. Auth and connection check completed successfully.");
                onLog?.Invoke($"Deployment to {environment.Name} completed (simulated).");
                _logger.LogInformation("Deployment simulated (SimulateDeployment = true)");
                return;
            }

            // Step 5: Deploy
            onLog?.Invoke($"Starting deployment to {environment.Name}...");
            await _deploymentService.DeployAsync(packagePath, logFilePath, isolatedAuthDir, onLog);
            onLog?.Invoke($"Deployment to {environment.Name} completed.");

            // Step 6: Post-deployment validation (CHECK 2)
            await RunValidatorsAsync(context, isPreDeploy: false, onLog);
            onLog?.Invoke("[Post-Deploy Validation] ✓ Confirmed: package was deployed to correct environment.");

            _logger.LogInformation("Deployment to {Env} completed successfully", environment.Name);
        }
        finally
        {
            // Cleanup: Delete isolated auth directory
            onLog?.Invoke($"[Cleanup] Removing isolated PAC auth directory...");
            _directoryManager.DeleteIsolatedDirectory(isolatedAuthDir);
            onLog?.Invoke($"[Cleanup] Removed isolated PAC auth directory: {isolatedAuthDir}");
        }
    }

    /// <summary>
    /// Runs all validators for the specified phase (pre-deploy or post-deploy).
    /// Pre-deploy validators: PreDeployAuthValidator
    /// Post-deploy validators: PostDeployLogValidator
    /// </summary>
    private async Task RunValidatorsAsync(DeploymentContext context, bool isPreDeploy, Action<string>? onLog)
    {
        var phase = isPreDeploy ? "PRE-DEPLOY" : "POST-DEPLOY";
        var validatorType = isPreDeploy ? typeof(PreDeployAuthValidator) : typeof(PostDeployLogValidator);

        var applicableValidators = _validators
            .Where(v => v.GetType() == validatorType)
            .ToList();

        if (!applicableValidators.Any())
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
