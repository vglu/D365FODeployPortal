using Microsoft.EntityFrameworkCore;
using DeployPortal.Models;

namespace DeployPortal.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Models.Environment> Environments => Set<Models.Environment>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<DeploymentLog> DeploymentLogs => Set<DeploymentLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Models.Environment>(entity =>
        {
            entity.HasIndex(e => e.Url).IsUnique();
            entity.HasIndex(e => e.Name);
        });

        modelBuilder.Entity<Package>(entity =>
        {
            entity.HasIndex(e => e.UploadedAt);
        });

        modelBuilder.Entity<Deployment>(entity =>
        {
            entity.HasOne(d => d.Package)
                .WithMany(p => p.Deployments)
                .HasForeignKey(d => d.PackageId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Environment)
                .WithMany(e => e.Deployments)
                .HasForeignKey(d => d.EnvironmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(d => d.Status);
            entity.HasIndex(d => d.QueuedAt);
        });

        modelBuilder.Entity<DeploymentLog>(entity =>
        {
            entity.HasOne(l => l.Deployment)
                .WithMany(d => d.Logs)
                .HasForeignKey(l => l.DeploymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(l => l.DeploymentId);
        });
    }
}
