using Microsoft.EntityFrameworkCore;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Database;

public sealed class RegistryDbContext : DbContext
{
    public RegistryDbContext(DbContextOptions<RegistryDbContext> options) : base(options) { }

    public DbSet<PackageRecord> Packages => Set<PackageRecord>();
    public DbSet<VersionRecord> Versions => Set<VersionRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<TokenRecord> Tokens => Set<TokenRecord>();
    public DbSet<OwnerEntry> Owners => Set<OwnerEntry>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PackageRecord>(entity =>
        {
            entity.ToTable("packages");
            entity.HasKey(e => e.Name);
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.License).HasColumnName("license");
            entity.Property(e => e.Repository).HasColumnName("repository");
            entity.Property(e => e.Readme).HasColumnName("readme");
            entity.Property(e => e.Keywords).HasColumnName("keywords");
            entity.Property(e => e.Latest).HasColumnName("latest").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<VersionRecord>(entity =>
        {
            entity.ToTable("versions");
            entity.HasKey(e => new { e.PackageName, e.Version });
            entity.Property(e => e.PackageName).HasColumnName("package_name");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.StashVersion).HasColumnName("stash_version");
            entity.Property(e => e.Dependencies).HasColumnName("dependencies");
            entity.Property(e => e.Integrity).HasColumnName("integrity").IsRequired();
            entity.Property(e => e.PublishedAt).HasColumnName("published_at");
            entity.Property(e => e.PublishedBy).HasColumnName("published_by").IsRequired();
            entity.HasOne<PackageRecord>()
                .WithMany()
                .HasForeignKey(e => e.PackageName)
                .HasPrincipalKey(e => e.Name)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRecord>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Username);
            entity.Property(e => e.Username).HasColumnName("username");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(e => e.Role).HasColumnName("role").IsRequired().HasDefaultValue("user");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<TokenRecord>(entity =>
        {
            entity.ToTable("tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Username).HasColumnName("username").IsRequired();
            entity.Property(e => e.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.Property(e => e.Scope).HasColumnName("scope").IsRequired().HasDefaultValue("publish");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(e => e.Username)
                .HasPrincipalKey(e => e.Username)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OwnerEntry>(entity =>
        {
            entity.ToTable("owners");
            entity.HasKey(e => new { e.PackageName, e.Username });
            entity.Property(e => e.PackageName).HasColumnName("package_name");
            entity.Property(e => e.Username).HasColumnName("username");
            entity.HasOne<PackageRecord>()
                .WithMany()
                .HasForeignKey(e => e.PackageName)
                .HasPrincipalKey(e => e.Name)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.Action).HasColumnName("action").IsRequired();
            entity.Property(e => e.Package).HasColumnName("package");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.User).HasColumnName("user");
            entity.Property(e => e.Target).HasColumnName("target");
            entity.Property(e => e.Ip).HasColumnName("ip");
            entity.Property(e => e.Timestamp).HasColumnName("timestamp");
        });
    }
}
