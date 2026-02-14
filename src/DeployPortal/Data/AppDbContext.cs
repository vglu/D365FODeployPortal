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
    public DbSet<PackageChangeLog> PackageChangeLogs => Set<PackageChangeLog>();

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
            entity.HasIndex(d => d.IsArchived); // Index for archive filter
        });

        modelBuilder.Entity<DeploymentLog>(entity =>
        {
            entity.HasOne(l => l.Deployment)
                .WithMany(d => d.Logs)
                .HasForeignKey(l => l.DeploymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(l => l.DeploymentId);
        });

        modelBuilder.Entity<PackageChangeLog>(entity =>
        {
            entity.HasOne(c => c.Package)
                .WithMany()
                .HasForeignKey(c => c.PackageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => c.PackageId);
            entity.HasIndex(c => c.ChangedAt);
        });
    }
}
