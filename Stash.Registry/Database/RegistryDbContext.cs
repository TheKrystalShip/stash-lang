using Microsoft.EntityFrameworkCore;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Database;

/// <summary>
/// Entity Framework Core <see cref="DbContext"/> for the Stash package registry.
/// </summary>
/// <remarks>
/// <para>
/// Exposes one <see cref="DbSet{TEntity}"/> per registry entity and configures the
/// database schema in <see cref="OnModelCreating"/>. All column names follow
/// <c>snake_case</c> convention to match the SQLite schema. Foreign-key cascade
/// deletes are configured for versions → packages, tokens → users, and
/// owners → packages.
/// </para>
/// </remarks>
public sealed class RegistryDbContext : DbContext
{
    /// <summary>
    /// Initialises the context with the provided EF Core options.
    /// </summary>
    /// <param name="options">The <see cref="DbContextOptions{RegistryDbContext}"/> to use.</param>
    public RegistryDbContext(DbContextOptions<RegistryDbContext> options) : base(options) { }

    /// <summary>Gets the <see cref="PackageRecord"/> table.</summary>
    public DbSet<PackageRecord> Packages => Set<PackageRecord>();

    /// <summary>Gets the <see cref="VersionRecord"/> table.</summary>
    public DbSet<VersionRecord> Versions => Set<VersionRecord>();

    /// <summary>Gets the <see cref="UserRecord"/> table.</summary>
    public DbSet<UserRecord> Users => Set<UserRecord>();

    /// <summary>Gets the <see cref="TokenRecord"/> table.</summary>
    public DbSet<TokenRecord> Tokens => Set<TokenRecord>();

    /// <summary>Gets the <see cref="OwnerEntry"/> table.</summary>
    public DbSet<OwnerEntry> Owners => Set<OwnerEntry>();

    /// <summary>Gets the <see cref="AuditEntry"/> audit log table.</summary>
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    /// <summary>
    /// Configures entity mappings, column names, keys, and relationships.
    /// </summary>
    /// <param name="modelBuilder">The builder used to construct the EF Core model.</param>
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
            entity.Property(e => e.Deprecated).HasColumnName("deprecated").HasDefaultValue(false);
            entity.Property(e => e.DeprecationMessage).HasColumnName("deprecation_message");
            entity.Property(e => e.DeprecationAlternative).HasColumnName("deprecation_alternative");
            entity.Property(e => e.DeprecatedBy).HasColumnName("deprecated_by");
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
            entity.Property(e => e.Deprecated).HasColumnName("deprecated").HasDefaultValue(false);
            entity.Property(e => e.DeprecationMessage).HasColumnName("deprecation_message");
            entity.Property(e => e.DeprecatedBy).HasColumnName("deprecated_by");
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
