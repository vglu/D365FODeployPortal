using DeployPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace DeployPortal.Services;

public class EnvironmentService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SecretProtectionService _secretService;
    private readonly ILogger<EnvironmentService> _logger;

    public EnvironmentService(
        IDbContextFactory<AppDbContext> dbFactory,
        SecretProtectionService secretService,
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
            TenantId = tenantId,
            ApplicationId = applicationId,
            ClientSecretEncrypted = _secretService.Encrypt(clientSecret),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Environments.Add(env);
        await db.SaveChangesAsync();

        _logger.LogInformation("Environment created: {Name} ({Url})", env.Name, env.Url);
        return env;
    }

    public async Task UpdateAsync(int id, string name, string url, string tenantId, string applicationId, string? newClientSecret)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var env = await db.Environments.FindAsync(id);
        if (env == null) return;

        env.Name = name;
        env.Url = url;
        env.TenantId = tenantId;
        env.ApplicationId = applicationId;

        if (!string.IsNullOrEmpty(newClientSecret))
        {
            env.ClientSecretEncrypted = _secretService.Encrypt(newClientSecret);
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Environment updated: {Name}", env.Name);
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

    public string GetDecryptedSecret(Models.Environment env)
    {
        return _secretService.Decrypt(env.ClientSecretEncrypted);
    }

    public string GetMaskedSecret(Models.Environment env)
    {
        try
        {
            var decrypted = _secretService.Decrypt(env.ClientSecretEncrypted);
            return SecretProtectionService.MaskSecret(decrypted);
        }
        catch
        {
            return "****";
        }
    }
}
