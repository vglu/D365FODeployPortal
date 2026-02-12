using System.IO.Compression;
using System.Threading.Channels;
using DeployPortal.Components;
using DeployPortal.Data;
using DeployPortal.Hubs;
using DeployPortal.Models.Api;
using DeployPortal.PackageOps;
using DeployPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

// ── CLI: convert LCS → Unified (for use in container: docker run ... convert /input/package.zip [/output/Unified.zip])
if (args.Length >= 1 && string.Equals(args[0], "convert", StringComparison.OrdinalIgnoreCase))
{
    var inputPath = args.Length >= 2 ? args[1] : Environment.GetEnvironmentVariable("CONVERT_INPUT");
    var outputPath = args.Length >= 3 ? args[2] : Environment.GetEnvironmentVariable("CONVERT_OUTPUT");
    if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
    {
        Console.Error.WriteLine("Usage: DeployPortal.dll convert <input-lcs.zip> [output-unified.zip]");
        Console.Error.WriteLine("   Or: set CONVERT_INPUT (and optionally CONVERT_OUTPUT), then run with 'convert'");
        Console.Error.WriteLine("Example (container): docker run --rm -v C:\\Downloads:/data vglu/d365fo-deploy-portal:latest convert /data/package.zip /data/Unified.zip");
        Environment.Exit(1);
    }
    if (string.IsNullOrEmpty(outputPath))
        outputPath = Path.Combine(Path.GetDirectoryName(inputPath)!, Path.GetFileNameWithoutExtension(inputPath) + "_Unified.zip");

    try
    {
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        var tempDir = config["DeployPortal:TempWorkingDir"];
        if (string.IsNullOrWhiteSpace(tempDir))
            tempDir = Path.Combine(Path.GetTempPath(), "DeployPortal");
        var templateDir = Path.Combine(AppContext.BaseDirectory, "Resources", "UnifiedTemplate");
        if (!Directory.Exists(templateDir) || !File.Exists(Path.Combine(templateDir, "TemplatePackage.dll")))
        {
            Console.Error.WriteLine("Template not found: Resources/UnifiedTemplate/ (Built-in converter required)");
            Environment.Exit(2);
        }
        Directory.CreateDirectory(tempDir);
        var workDir = Path.Combine(tempDir, $"cli_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        var tempZip = Path.Combine(workDir, Path.GetFileName(inputPath));
        File.Copy(inputPath, tempZip);
        var engine = new ConvertEngine(tempDir, templateDir);
        Console.WriteLine($"Converting: {inputPath}");
        var outputDir = await engine.ConvertToUnifiedAsync(tempZip, msg => Console.WriteLine(msg));
        ZipFile.CreateFromDirectory(outputDir, outputPath);
        var size = new FileInfo(outputPath).Length;
        Console.WriteLine($"Created: {outputPath} ({size / 1024} KB)");
        try { Directory.Delete(workDir, true); } catch { }
        try { Directory.Delete(outputDir, true); } catch { }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.Exit(3);
    }
    Environment.Exit(0);
}

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
    var dataProtectionKeysDir = builder.Configuration["DeployPortal:DataProtectionKeysPath"];
    if (string.IsNullOrWhiteSpace(dataProtectionKeysDir))
        dataProtectionKeysDir = Path.Combine(AppContext.BaseDirectory, "keys");
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
    builder.Services.AddScoped<IConvertService, CompositeConvertService>();
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

    // Allow large package uploads (e.g. LCS AIO zip)
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = 2L * 1024 * 1024 * 1024; // 2 GB
    });
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024; // 2 GB
    });

    // OpenAPI / Swagger
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "D365FO Deploy Portal API",
            Version = "v1",
            Description = "REST API for package upload, conversion, merge, and download. Use from Postman, curl, or Azure DevOps pipelines."
        });
    });

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

    // Swagger (API docs and UI)
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Deploy Portal API v1"));

    // SignalR hub
    app.MapHub<DeployLogHub>("/hubs/deploylog");

    // ── REST API (for Postman, curl, pipelines) ───────────────────────────────────────
    var api = app.MapGroup("/api").WithTags("API");

    api.MapGet("/packages", async (PackageService pkg) =>
    {
        var list = await pkg.GetAllAsync();
        return Results.Ok(list.Select(PackageDto.From));
    })
    .WithName("GetPackages")
    .WithOpenApi()
    .Produces<List<PackageDto>>(200);

    api.MapGet("/packages/{id:int}", async (int id, PackageService pkg) =>
    {
        var p = await pkg.GetByIdAsync(id);
        if (p == null) return Results.NotFound();
        return Results.Ok(PackageDto.From(p));
    })
    .WithName("GetPackage")
    .WithOpenApi()
    .Produces<PackageDto>(200).Produces(404);

    api.MapPost("/packages/upload", async (HttpContext ctx, PackageService pkg) =>
    {
        if (!ctx.Request.HasFormContentType || ctx.Request.Form.Files.Count == 0)
            return Results.BadRequest("No file or empty file.");
        var file = ctx.Request.Form.Files.GetFile("file") ?? ctx.Request.Form.Files[0];
        if (file == null || file.Length == 0)
            return Results.BadRequest("No file or empty file.");
        var fileName = file.FileName;
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("File must be a .zip.");
        var packageType = ctx.Request.Form["packageType"].FirstOrDefault();
        var devOpsTaskUrl = ctx.Request.Form["devOpsTaskUrl"].FirstOrDefault();
        await using var stream = file.OpenReadStream();
        var package = await pkg.UploadAsync(fileName, stream, packageType, devOpsTaskUrl);
        return Results.Created($"/api/packages/{package.Id}", PackageDto.From(package));
    })
    .WithName("UploadPackage")
    .WithOpenApi()
    .Accepts<IFormFile>("multipart/form-data")
    .Produces<PackageDto>(201).Produces(400);

    api.MapPost("/packages/{id:int}/convert/unified", async (int id, PackageService pkg) =>
    {
        var source = await pkg.GetByIdAsync(id);
        if (source == null) return Results.NotFound("Package not found.");
        if (source.PackageType != "LCS" && source.PackageType != "Merged")
            return Results.BadRequest($"Cannot convert {source.PackageType} to Unified. Only LCS or Merged.");
        try
        {
            var result = await pkg.ConvertToUnifiedAsync(source);
            return Results.Ok(PackageDto.From(result));
        }
        catch (FileNotFoundException ex) { return Results.NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
    })
    .WithName("ConvertToUnified")
    .WithOpenApi()
    .Produces<PackageDto>(200).Produces(400).Produces(404);

    api.MapPost("/packages/{id:int}/convert/lcs", async (int id, PackageService pkg) =>
    {
        var source = await pkg.GetByIdAsync(id);
        if (source == null) return Results.NotFound("Package not found.");
        if (source.PackageType != "Unified")
            return Results.BadRequest($"Cannot convert {source.PackageType} to LCS. Only Unified.");
        try
        {
            var result = await pkg.ConvertToLcsAsync(source);
            return Results.Ok(PackageDto.From(result));
        }
        catch (FileNotFoundException ex) { return Results.NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
    })
    .WithName("ConvertToLcs")
    .WithOpenApi()
    .Produces<PackageDto>(200).Produces(400).Produces(404);

    api.MapPost("/packages/merge", async (MergeRequestDto body, PackageService pkgSvc, MergeService mergeSvc) =>
    {
        if (body.PackageIds == null || body.PackageIds.Count < 2)
            return Results.BadRequest("At least 2 package IDs required.");
        var packages = new List<DeployPortal.Models.Package>();
        foreach (var pid in body.PackageIds.Distinct())
        {
            var p = await pkgSvc.GetByIdAsync(pid);
            if (p == null) return Results.NotFound($"Package {pid} not found.");
            packages.Add(p);
        }
        var outputName = string.IsNullOrWhiteSpace(body.MergeName)
            ? $"Merged_{DateTime.UtcNow:yyyyMMdd_HHmmss}" : body.MergeName!.Trim();
        try
        {
            var result = await mergeSvc.MergePackagesAsync(packages, outputName);
            return Results.Created($"/api/packages/{result.Id}", PackageDto.From(result));
        }
        catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    })
    .WithName("MergePackages")
    .WithOpenApi()
    .Produces<PackageDto>(201).Produces(400).Produces(404);

    api.MapPost("/packages/delete-bulk", async (BulkDeletePackagesRequest body, PackageService pkg) =>
    {
        if (body?.Ids == null || body.Ids.Count == 0)
            return Results.BadRequest("Ids array required.");
        var deleted = 0;
        foreach (var id in body.Ids.Distinct())
        {
            try
            {
                await pkg.DeleteAsync(id);
                deleted++;
            }
            catch { /* skip missing */ }
        }
        return Results.Ok(new { deleted, message = $"{deleted} package(s) deleted." });
    })
    .WithName("DeletePackagesBulk")
    .WithOpenApi()
    .Produces(200).Produces(400);

    api.MapPost("/packages/{id:int}/refresh-licenses", async (int id, PackageService pkg) =>
    {
        try
        {
            var count = await pkg.RefreshLicenseInfoAsync(id);
            return Results.Ok(new { count, message = count > 0 ? $"{count} license file(s) found." : "No license files in package." });
        }
        catch (InvalidOperationException ex) { return Results.NotFound(ex.Message); }
        catch (FileNotFoundException ex) { return Results.NotFound(ex.Message); }
    })
    .WithName("RefreshLicenseInfo")
    .WithOpenApi()
    .Produces(200).Produces(404);

    // Upload license files into an existing package (multipart: one or more "file" parts).
    api.MapPost("/packages/{id:int}/licenses", async (int id, HttpContext ctx, PackageService pkg) =>
    {
        if (!ctx.Request.HasFormContentType || ctx.Request.Form.Files.Count == 0)
            return Results.BadRequest("No files. Send multipart/form-data with one or more 'file' parts.");
        var licenseFiles = new List<(string FileName, byte[] Content)>();
        foreach (var formFile in ctx.Request.Form.Files)
        {
            var file = formFile;
            var fileName = file.FileName ?? "license.dat";
            if (string.IsNullOrWhiteSpace(Path.GetFileName(fileName)))
                fileName = $"license_{licenseFiles.Count + 1}.dat";
            await using var stream = file.OpenReadStream();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            licenseFiles.Add((fileName.Trim(), ms.ToArray()));
        }
        if (licenseFiles.Count == 0)
            return Results.BadRequest("No file or empty file.");
        try
        {
            await pkg.InjectLicenseFilesAsync(id, licenseFiles);
            return Results.Ok(new { count = licenseFiles.Count, message = $"Injected {licenseFiles.Count} license file(s) into package." });
        }
        catch (FileNotFoundException) { return Results.NotFound("Package not found."); }
        catch (InvalidOperationException ex) { return Results.NotFound(ex.Message); }
    })
    .WithName("UploadLicensesIntoPackage")
    .WithOpenApi()
    .Accepts<IFormFile>("multipart/form-data")
    .Produces(200).Produces(400).Produces(404);

    // Start a deployment (returns deployment id / batch number).
    api.MapPost("/deployments", async (DeployRequestDto body, DeploymentOrchestrator orchestrator, IDbContextFactory<AppDbContext> dbFactory) =>
    {
        if (body.PackageId <= 0 || body.EnvironmentId <= 0)
            return Results.BadRequest("PackageId and EnvironmentId must be positive.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var packageExists = await db.Packages.AnyAsync(p => p.Id == body.PackageId);
        if (!packageExists)
            return Results.NotFound("Package not found.");
        var envExists = await db.Environments.AnyAsync(e => e.Id == body.EnvironmentId);
        if (!envExists)
            return Results.NotFound("Environment not found.");
        var d = new DeployPortal.Models.Deployment
        {
            PackageId = body.PackageId,
            EnvironmentId = body.EnvironmentId,
            Status = DeployPortal.Models.DeploymentStatus.Queued,
            QueuedAt = DateTime.UtcNow,
            DevOpsTaskUrl = string.IsNullOrWhiteSpace(body.DevOpsTaskUrl) ? null : body.DevOpsTaskUrl.Trim()
        };
        db.Deployments.Add(d);
        await db.SaveChangesAsync();
        await orchestrator.EnqueueAsync(d.Id);
        return Results.Created($"/api/deployments/{d.Id}", new { deploymentId = d.Id, status = "Queued" });
    })
    .WithName("StartDeployment")
    .WithOpenApi()
    .Produces(201).Produces(400).Produces(404);

    // Get deployment status by id (batch number).
    api.MapGet("/environments/export", async (EnvironmentService envSvc) =>
    {
        var data = await envSvc.GetExportDataAsync();
        var fileName = $"environments_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Results.File(bytes, "application/json", fileName);
    })
    .WithName("ExportEnvironments")
    .WithOpenApi()
    .Produces<List<EnvironmentExportDto>>(200);

    api.MapPost("/environments/import", async (List<EnvironmentExportDto> body, EnvironmentService envSvc) =>
    {
        if (body == null || body.Count == 0)
            return Results.BadRequest("JSON array of environments required.");
        var created = await envSvc.ImportFromExportAsync(body);
        return Results.Ok(new { created, message = $"{created} environment(s) imported." });
    })
    .WithName("ImportEnvironments")
    .WithOpenApi()
    .Produces(200).Produces(400);

    api.MapGet("/deployments/{id:int}", async (int id, IDbContextFactory<AppDbContext> dbFactory) =>
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var d = await db.Deployments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (d == null)
            return Results.NotFound("Deployment not found.");
        return Results.Ok(DeploymentDto.From(d));
    })
    .WithName("GetDeployment")
    .WithOpenApi()
    .Produces<DeploymentDto>(200).Produces(404);

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

// Expose for WebApplicationFactory in tests
public partial class Program { }
