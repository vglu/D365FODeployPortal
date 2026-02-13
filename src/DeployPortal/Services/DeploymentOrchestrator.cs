using System.IO.Compression;
using System.Threading.Channels;
using DeployPortal.Data;
using DeployPortal.Hubs;
using DeployPortal.Models;
using DeployPortal.Services.Deployment;
using DeployPortal.Services.Deployment.Isolation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DeployPortal.Services;

public record DeploymentRequest(int DeploymentId);

public class DeploymentOrchestrator : BackgroundService
{
    private readonly Channel<DeploymentRequest> _channel;
    private readonly IServiceProvider _services;
    private readonly ILogger<DeploymentOrchestrator> _logger;

    public DeploymentOrchestrator(
        Channel<DeploymentRequest> channel,
        IServiceProvider services,
        ILogger<DeploymentOrchestrator> logger)
    {
        _channel = channel;
        _services = services;
        _logger = logger;
    }

    public async Task EnqueueAsync(int deploymentId)
    {
        await _channel.Writer.WriteAsync(new DeploymentRequest(deploymentId));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeploymentOrchestrator started.");

        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessDeploymentAsync(request.DeploymentId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deployment {DeploymentId}", request.DeploymentId);
            }
        }
    }

    private async Task ProcessDeploymentAsync(int deploymentId, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var convertService = scope.ServiceProvider.GetRequiredService<IConvertService>();
        var deployService = scope.ServiceProvider.GetRequiredService<IDeployService>();
        var directoryManager = scope.ServiceProvider.GetRequiredService<IIsolatedDirectoryManager>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<DeployLogHub>>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deployment = await db.Deployments
            .Include(d => d.Package)
            .Include(d => d.Environment)
            .FirstOrDefaultAsync(d => d.Id == deploymentId, ct);

        if (deployment == null)
        {
            _logger.LogWarning("Deployment {Id} not found", deploymentId);
            return;
        }

        var tempDir = settingsService.TempWorkingDir;
        var logDir = Path.Combine(tempDir, "logs");
        Directory.CreateDirectory(logDir);
        var logFilePath = Path.Combine(logDir, $"deploy_{deploymentId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");

        deployment.StartedAt = DateTime.UtcNow;
        deployment.LogFilePath = logFilePath;

        async void Log(string message, string level = "Info")
        {
            var logEntry = new DeploymentLog
            {
                DeploymentId = deploymentId,
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = message
            };
            db.DeploymentLogs.Add(logEntry);
            await db.SaveChangesAsync(ct);

            // Push to SignalR
            await hubContext.Clients.Group($"deployment-{deploymentId}")
                .SendAsync("ReceiveLog", logEntry.Timestamp.ToString("HH:mm:ss"), level, message, cancellationToken: ct);
        }

        try
        {
            string deployDir;
            var packageType = deployment.Package.PackageType;

            if (packageType == "Unified")
            {
                // Already a Unified package — extract and deploy directly
                deployment.Status = DeploymentStatus.Converting;
                await db.SaveChangesAsync(ct);
                Log("Package is already Unified — extracting for deployment...");

                deployDir = Path.Combine(tempDir, $"deploy_{deploymentId}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(deployDir);
                System.IO.Compression.ZipFile.ExtractToDirectory(
                    deployment.Package.StoredFilePath, deployDir);

                Log("Unified package extracted successfully.");
            }
            else
            {
                // LCS or Merged — needs conversion
                deployment.Status = DeploymentStatus.Converting;
                await db.SaveChangesAsync(ct);

                var engine = settingsService.ConverterEngine;
                var useBuiltIn = engine.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase);

                Log($"Starting conversion {packageType} -> Unified (engine: {(useBuiltIn ? "Built-in" : "ModelUtil")})");

                deployDir = await convertService.ConvertToUnifiedAsync(
                    deployment.Package.StoredFilePath,
                    msg => Log(msg));
            }

            // Step 2: Deploy
            deployment.Status = DeploymentStatus.Deploying;
            await db.SaveChangesAsync(ct);
            Log($"Starting deployment to {deployment.Environment.Name}");

            // Create isolated PAC auth directory for this deployment using IsolatedDirectoryManager
            // This ensures parallel deployments don't interfere with each other
            var isolatedAuthDir = directoryManager.CreateIsolatedDirectory(deploymentId);
            
            await deployService.DeployPackageAsync(
                deployment.Environment,
                deployDir,
                logFilePath,
                isolatedAuthDir,
                msg => Log(msg));

            // Success
            deployment.Status = DeploymentStatus.Success;
            deployment.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            Log("Deployment completed successfully!");

            // Cleanup temporary directory
            try { Directory.Delete(deployDir, true); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            deployment.Status = DeploymentStatus.Failed;
            deployment.CompletedAt = DateTime.UtcNow;
            deployment.ErrorMessage = ex.Message;
            await db.SaveChangesAsync(ct);
            Log($"Deployment FAILED: {ex.Message}", "Error");
            _logger.LogError(ex, "Deployment {Id} failed", deploymentId);
        }

        // Notify status change
        await hubContext.Clients.Group($"deployment-{deploymentId}")
            .SendAsync("StatusChanged", deployment.Status.ToString(), cancellationToken: ct);
    }
}
