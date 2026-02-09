using System.Threading.Channels;
using DeployPortal.Components;
using DeployPortal.Data;
using DeployPortal.Hubs;
using DeployPortal.PackageOps;
using DeployPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File("logs/deploy-portal-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // Database
    var dbPath = builder.Configuration["DeployPortal:DatabasePath"];
    if (string.IsNullOrEmpty(dbPath))
        dbPath = Path.Combine(AppContext.BaseDirectory, "deploy-portal.db");
    var dbDir = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(dbDir))
        Directory.CreateDirectory(dbDir);

    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));

    // Data Protection (for encrypting secrets) — persist keys to a stable directory
    // so they survive container restarts and app redeployments
    var dataProtectionKeysDir = builder.Configuration["DeployPortal:DataProtectionKeysPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "keys");
    Directory.CreateDirectory(dataProtectionKeysDir);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir));

    // Settings service (singleton — reads/writes config file)
    builder.Services.AddSingleton<SettingsService>();

    // Services — all scoped to avoid lifecycle issues
    builder.Services.AddScoped<SecretProtectionService>();
    builder.Services.AddScoped<EnvironmentService>();
    builder.Services.AddScoped<PackageService>();
    builder.Services.AddScoped<MergeService>();
    builder.Services.AddScoped<ConvertService>();
    builder.Services.AddScoped<BuiltInConvertService>();
    builder.Services.AddScoped<DeployService>();

    // IPackageOpsService — switches between Local and Azure based on Settings → ProcessingMode
    builder.Services.AddScoped<IPackageOpsService>(sp =>
    {
        var settings = sp.GetRequiredService<SettingsService>();
        if (settings.ProcessingMode.Equals("Azure", StringComparison.OrdinalIgnoreCase))
        {
            return new AzurePackageOpsService(
                settings.AzureFunctionsUrl,
                settings.AzureBlobConnectionString,
                settings.AzureFunctionKey,
                settings.TempWorkingDir);
        }
        var templateDir = Path.Combine(AppContext.BaseDirectory, "Resources", "UnifiedTemplate");
        return new LocalPackageOpsService(settings.TempWorkingDir, templateDir);
    });

    // Deployment queue
    var channel = Channel.CreateUnbounded<DeploymentRequest>();
    builder.Services.AddSingleton(channel);
    builder.Services.AddSingleton<DeploymentOrchestrator>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DeploymentOrchestrator>());

    // SignalR
    builder.Services.AddSignalR();

    // MudBlazor
    builder.Services.AddMudServices();

    // Blazor
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // Detailed errors in development for easier debugging
    if (builder.Environment.IsDevelopment())
    {
        builder.Services.Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options =>
        {
            options.DetailedErrors = true;
        });
    }

    var app = builder.Build();

    // Auto-migrate database
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();

        // Apply schema migrations for new columns
        try
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE Packages ADD COLUMN MergeSourceNames TEXT NULL");
            Log.Information("Added MergeSourceNames column to Packages table");
        }
        catch { /* Column already exists */ }

        try
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE Packages ADD COLUMN DevOpsTaskUrl TEXT NULL");
            Log.Information("Added DevOpsTaskUrl column to Packages table");
        }
        catch { /* Column already exists */ }

        try
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE Deployments ADD COLUMN DevOpsTaskUrl TEXT NULL");
            Log.Information("Added DevOpsTaskUrl column to Deployments table");
        }
        catch { /* Column already exists */ }

        try
        {
            db.Database.ExecuteSqlRaw(
                "ALTER TABLE Packages ADD COLUMN LicenseFileNames TEXT NULL");
            Log.Information("Added LicenseFileNames column to Packages table");
        }
        catch { /* Column already exists */ }
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
    }

    app.UseAntiforgery();
    app.MapStaticAssets();

    // SignalR hub
    app.MapHub<DeployLogHub>("/hubs/deploylog");

    // ── Package download API ──
    app.MapGet("/api/packages/{id:int}/download", async (int id, IDbContextFactory<AppDbContext> dbFactory) =>
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var pkg = await db.Packages.FindAsync(id);
        if (pkg == null)
            return Results.NotFound("Package not found");
        if (!File.Exists(pkg.StoredFilePath))
            return Results.NotFound("Package file not found on disk");

        var fileName = !string.IsNullOrEmpty(pkg.OriginalFileName) ? pkg.OriginalFileName : $"{pkg.Name}.zip";
        return Results.File(pkg.StoredFilePath, "application/zip", fileName);
    });

    // ── License files download API ──
    app.MapGet("/api/packages/{id:int}/licenses", async (int id, IDbContextFactory<AppDbContext> dbFactory) =>
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var pkg = await db.Packages.FindAsync(id);
        if (pkg == null)
            return Results.NotFound("Package not found");
        if (!File.Exists(pkg.StoredFilePath))
            return Results.NotFound("Package file not found on disk");

        var licenses = DeployPortal.PackageOps.PackageAnalyzer.ExtractLicenseFileContents(pkg.StoredFilePath);
        if (licenses.Count == 0)
            return Results.NotFound("No license files found in package");

        if (licenses.Count == 1)
        {
            var lic = licenses[0];
            return Results.File(lic.Content, "application/octet-stream", lic.FileName);
        }

        // Multiple files -> bundle as ZIP
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var lic in licenses)
            {
                var entry = archive.CreateEntry(lic.FileName);
                using var entryStream = entry.Open();
                entryStream.Write(lic.Content);
            }
        }
        ms.Position = 0;
        return Results.File(ms.ToArray(), "application/zip", $"{pkg.Name}_Licenses.zip");
    });

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    Log.Information("DeployPortal started at {Urls}", string.Join(", ", app.Urls));
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
