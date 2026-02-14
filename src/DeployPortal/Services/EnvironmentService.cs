using DeployPortal.Data;
using DeployPortal.Models.Api;
using Microsoft.EntityFrameworkCore;

namespace DeployPortal.Services;

public class EnvironmentService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ISecretProtectionService _secretService;
    private readonly ILogger<EnvironmentService> _logger;

    public EnvironmentService(
        IDbContextFactory<AppDbContext> dbFactory,
        ISecretProtectionService secretService,
        ILogger<EnvironmentService> logger)
    {
        _dbFactory = dbFactory;
        _secretService = secretService;
        _logger = logger;
    }

    public async Task<List<Models.Environment>> GetAllAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Environments.OrderBy(e => e.Name).ToListAsync();
    }

    public async Task<List<Models.Environment>> GetActiveAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Environments.Where(e => e.IsActive).OrderBy(e => e.Name).ToListAsync();
    }

    public async Task<Models.Environment?> GetByIdAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Environments.FindAsync(id);
    }

    public async Task<Models.Environment> CreateAsync(string name, string url, string tenantId, string applicationId, string clientSecret)
    {
        var env = new Models.Environment
        {
            Name = name,
            Url = url,
            TenantId = tenantId ?? string.Empty,
            ApplicationId = applicationId ?? string.Empty,
            ClientSecretEncrypted = string.IsNullOrEmpty(clientSecret) ? string.Empty : _secretService.Encrypt(clientSecret),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Environments.Add(env);
        await db.SaveChangesAsync();

        _logger.LogInformation("Environment created: {Name} ({Url}), SP={HasSp}", env.Name, env.Url, env.HasServicePrincipal);
        return env;
    }

    public async Task UpdateAsync(int id, string name, string url, string tenantId, string applicationId, string? newClientSecret)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var env = await db.Environments.FindAsync(id);
        if (env == null) return;

        env.Name = name;
        env.Url = url;
        env.TenantId = tenantId ?? string.Empty;
        env.ApplicationId = applicationId ?? string.Empty;

        if (!string.IsNullOrEmpty(newClientSecret))
            env.ClientSecretEncrypted = _secretService.Encrypt(newClientSecret);
        else if (string.IsNullOrEmpty(applicationId))
            env.ClientSecretEncrypted = string.Empty; // switching to interactive mode

        await db.SaveChangesAsync();
        _logger.LogInformation("Environment updated: {Name}, SP={HasSp}", env.Name, env.HasServicePrincipal);
    }

    public async Task ToggleActiveAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var env = await db.Environments.FindAsync(id);
        if (env == null) return;

        env.IsActive = !env.IsActive;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var env = await db.Environments.FindAsync(id);
        if (env == null) return;

        db.Environments.Remove(env);
        await db.SaveChangesAsync();

        _logger.LogInformation("Environment deleted: {Name}", env.Name);
    }

    /// <summary>Returns all environments as export DTOs (for backup; includes encrypted secret for same-machine restore).</summary>
    public async Task<List<Models.Api.EnvironmentExportDto>> GetExportDataAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var list = await db.Environments.OrderBy(e => e.Name).ToListAsync();
        return list.Select(e => new EnvironmentExportDto
        {
            Name = e.Name,
            Url = e.Url,
            TenantId = e.TenantId,
            ApplicationId = e.ApplicationId,
            ClientSecretEncrypted = e.ClientSecretEncrypted,
            IsActive = e.IsActive
        }).ToList();
    }

    /// <summary>Creates environments from export data (restore backup; same machine so encrypted secrets are used as-is).</summary>
    public async Task<int> ImportFromExportAsync(IEnumerable<EnvironmentExportDto> data)
    {
        var created = 0;
        await using var db = await _dbFactory.CreateDbContextAsync();
        foreach (var dto in data)
        {
            if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Url)) continue;
            var env = new Models.Environment
            {
                Name = dto.Name.Trim(),
                Url = dto.Url.Trim(),
                TenantId = dto.TenantId ?? string.Empty,
                ApplicationId = dto.ApplicationId ?? string.Empty,
                ClientSecretEncrypted = dto.ClientSecretEncrypted ?? string.Empty,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            db.Environments.Add(env);
            created++;
        }
        await db.SaveChangesAsync();
        _logger.LogInformation("Imported {Count} environment(s) from backup", created);
        return created;
    }

    public string GetDecryptedSecret(Models.Environment env)
    {
        if (string.IsNullOrEmpty(env.ClientSecretEncrypted)) return string.Empty;
        return _secretService.Decrypt(env.ClientSecretEncrypted);
    }

    public string GetMaskedSecret(Models.Environment env)
    {
        if (string.IsNullOrEmpty(env.ClientSecretEncrypted)) return "(Interactive sign-in)";
        try
        {
            var decrypted = _secretService.Decrypt(env.ClientSecretEncrypted);
            return _secretService.MaskSecret(decrypted);
        }
        catch
        {
            return "****";
        }
    }
}
