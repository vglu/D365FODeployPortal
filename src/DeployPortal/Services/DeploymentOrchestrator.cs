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
    /// <summary>Minimum delay in seconds between starting one deployment and the next. Exposed for tests.</summary>
    public const int DelayBetweenStartsSeconds = 30;
    private static readonly TimeSpan DelayBetweenStarts = TimeSpan.FromSeconds(DelayBetweenStartsSeconds);

    private readonly Channel<DeploymentRequest> _channel;
    private readonly IServiceProvider _services;
    private readonly ILogger<DeploymentOrchestrator> _logger;
    private readonly ISettingsService _settings;
    private DateTime _lastDeploymentStart = DateTime.MinValue;
    private readonly object _startLock = new();

    public DeploymentOrchestrator(
        Channel<DeploymentRequest> channel,
        IServiceProvider services,
        ILogger<DeploymentOrchestrator> logger,
        ISettingsService settings)
    {
        _channel = channel;
        _services = services;
        _logger = logger;
        _settings = settings;
    }

    public async Task EnqueueAsync(int deploymentId)
    {
        await _channel.Writer.WriteAsync(new DeploymentRequest(deploymentId));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Recover after app restart: mark Converting/Deploying as Failed (interrupted), re-queue Queued
        try
        {
            using var scope = _services.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);

            var stuck = await db.Deployments
                .Where(d => d.Status == DeploymentStatus.Merging || d.Status == DeploymentStatus.Converting || d.Status == DeploymentStatus.Deploying)
                .ToListAsync(stoppingToken);
            foreach (var d in stuck)
            {
                d.Status = DeploymentStatus.Failed;
                d.ErrorMessage = "Deployment was interrupted (application stopped or restarted).";
                d.CompletedAt = DateTime.UtcNow;
            }
            if (stuck.Count > 0)
            {
                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Marked {Count} interrupted deployment(s) as Failed.", stuck.Count);
            }

            var queuedIds = await db.Deployments
                .Where(d => d.Status == DeploymentStatus.Queued)
                .Select(d => d.Id)
                .ToListAsync(stoppingToken);
            foreach (var id in queuedIds)
                await EnqueueAsync(id);
            if (queuedIds.Count > 0)
                _logger.LogInformation("Re-queued {Count} deployment(s) that were Queued before restart.", queuedIds.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup recovery of deployment state failed.");
        }

        var concurrency = Math.Max(SettingsService.MinMaxConcurrentDeployments, Math.Min(SettingsService.MaxMaxConcurrentDeployments, _settings.MaxConcurrentDeployments));
        _logger.LogInformation("DeploymentOrchestrator started with {Concurrency} concurrent deployment(s).", concurrency);

        async Task RunWorkerAsync()
        {
            while (await _channel.Reader.WaitToReadAsync(stoppingToken))
            {
                if (!_channel.Reader.TryRead(out var request))
                    continue;
                try
                {
                    await WaitDelayBetweenStartsAsync(stoppingToken);
                    await ProcessDeploymentAsync(request.DeploymentId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing deployment {DeploymentId}", request.DeploymentId);
                }
            }
        }

        var workers = Enumerable.Range(0, concurrency).Select(_ => RunWorkerAsync()).ToArray();
        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown: host is stopping, token was canceled. Exit without logging as failure.
        }
    }

    /// <summary>Waits so that at least <see cref="DelayBetweenStarts"/> has passed since the previous deployment start.</summary>
    private async Task WaitDelayBetweenStartsAsync(CancellationToken ct)
    {
        TimeSpan toWait;
        lock (_startLock)
        {
            var now = DateTime.UtcNow;
            var elapsed = _lastDeploymentStart == DateTime.MinValue ? DelayBetweenStarts : now - _lastDeploymentStart;
            toWait = elapsed >= DelayBetweenStarts ? TimeSpan.Zero : DelayBetweenStarts - elapsed;
        }
        if (toWait > TimeSpan.Zero)
        {
            _logger.LogDebug("Waiting {Seconds}s before starting next deployment.", toWait.TotalSeconds);
            await Task.Delay(toWait, ct);
        }
        lock (_startLock)
        {
            _lastDeploymentStart = DateTime.UtcNow;
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
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

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

        if (deployment.Status == DeploymentStatus.Cancelled)
        {
            _logger.LogInformation("Deployment {Id} was cancelled, skipping.", deploymentId);
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

        string? deployDir = null;
        try
        {
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
                // LCS or Merged — needs conversion. Copy package to a unique temp path first so that
                // parallel deployments of the same package get separate output dirs (no file overwrite).
                deployment.Status = DeploymentStatus.Converting;
                await db.SaveChangesAsync(ct);

                var engine = settingsService.ConverterEngine;
                var useBuiltIn = engine.Equals("BuiltIn", StringComparison.OrdinalIgnoreCase);

                Log($"Starting conversion {packageType} -> Unified (engine: {(useBuiltIn ? "Built-in" : "ModelUtil")})");

                var uniqueCopyPath = Path.Combine(tempDir, $"deploy_{deploymentId}_{Guid.NewGuid():N}.zip");
                try
                {
                    File.Copy(deployment.Package.StoredFilePath, uniqueCopyPath, overwrite: false);
                    deployDir = await convertService.ConvertToUnifiedAsync(uniqueCopyPath, msg => Log(msg));
                }
                finally
                {
                    try { if (File.Exists(uniqueCopyPath)) File.Delete(uniqueCopyPath); } catch { /* ignore */ }
                }
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
        finally
        {
            // Always remove temporary deploy directory (success or failure)
            if (!string.IsNullOrEmpty(deployDir) && Directory.Exists(deployDir))
            {
                try { Directory.Delete(deployDir, true); } catch { /* ignore */ }
            }
        }

        // Notify status change
        await hubContext.Clients.Group($"deployment-{deploymentId}")
            .SendAsync("StatusChanged", deployment.Status.ToString(), cancellationToken: ct);
    }
}
