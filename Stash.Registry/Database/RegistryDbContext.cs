using Microsoft.EntityFrameworkCore;
using Stash.Registry.Auth;
using Stash.Registry.Contracts;
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
/// deletes are configured where appropriate.
/// </para>
/// <para>
/// The <c>owners</c> table and <see cref="OwnerEntry"/> model have been replaced
/// by <c>package_roles</c> / <see cref="PackageRoleEntry"/> (D3 clean break).
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

    /// <summary>Gets the <see cref="RefreshTokenRecord"/> table.</summary>
    public DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();

    /// <summary>Gets the <see cref="AuditEntry"/> audit log table.</summary>
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    /// <summary>Gets the <see cref="OrganizationRecord"/> table.</summary>
    public DbSet<OrganizationRecord> Organizations => Set<OrganizationRecord>();

    /// <summary>Gets the <see cref="OrgMemberEntry"/> table.</summary>
    public DbSet<OrgMemberEntry> OrgMembers => Set<OrgMemberEntry>();

    /// <summary>Gets the <see cref="TeamRecord"/> table.</summary>
    public DbSet<TeamRecord> Teams => Set<TeamRecord>();

    /// <summary>Gets the <see cref="TeamMemberEntry"/> table.</summary>
    public DbSet<TeamMemberEntry> TeamMembers => Set<TeamMemberEntry>();

    /// <summary>Gets the <see cref="ScopeRecord"/> table.</summary>
    public DbSet<ScopeRecord> Scopes => Set<ScopeRecord>();

    /// <summary>Gets the <see cref="PackageRoleEntry"/> table (replaces the old <c>owners</c> table).</summary>
    public DbSet<PackageRoleEntry> PackageRoles => Set<PackageRoleEntry>();

    /// <summary>
    /// Configures entity mappings, column names, keys, relationships, and CHECK constraints.
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
            entity.Property(e => e.Visibility)
                .HasColumnName("visibility")
                .IsRequired()
                .HasDefaultValue(Visibilities.Public);
            entity.ToTable("packages", t => t.HasCheckConstraint(
                "CK_packages_visibility",
                "visibility IN ('public', 'private', 'internal')"));
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
            entity.Property(e => e.Role).HasColumnName("role").IsRequired().HasDefaultValue(UserRoles.User);
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

        modelBuilder.Entity<RefreshTokenRecord>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Username).HasColumnName("username").IsRequired();
            entity.Property(e => e.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.Property(e => e.AccessTokenId).HasColumnName("access_token_id").IsRequired();
            entity.Property(e => e.FamilyId).HasColumnName("family_id").IsRequired();
            entity.Property(e => e.MachineId).HasColumnName("machine_id").IsRequired();
            entity.Property(e => e.Scope).HasColumnName("scope").IsRequired().HasDefaultValue("publish");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.Consumed).HasColumnName("consumed").HasDefaultValue(false);
            entity.HasOne<UserRecord>()
                .WithMany()
                .HasForeignKey(e => e.Username)
                .HasPrincipalKey(e => e.Username)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => e.FamilyId);
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
            entity.Property(e => e.Decision).HasColumnName("decision");
            entity.Property(e => e.DenyReason).HasColumnName("deny_reason");
        });

        // ── New P2 tables ──────────────────────────────────────────────────────

        modelBuilder.Entity<OrganizationRecord>(entity =>
        {
            entity.ToTable("organizations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<OrgMemberEntry>(entity =>
        {
            entity.ToTable("org_members");
            entity.HasKey(e => new { e.OrgId, e.Username });
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Username).HasColumnName("username");
            entity.Property(e => e.OrgRole).HasColumnName("org_role").IsRequired().HasDefaultValue(OrgRoles.Member);
            entity.Property(e => e.JoinedAt).HasColumnName("joined_at");
            entity.ToTable("org_members", t => t.HasCheckConstraint(
                "CK_org_members_org_role",
                "org_role IN ('owner', 'member')"));
            entity.HasOne<OrganizationRecord>()
                .WithMany()
                .HasForeignKey(e => e.OrgId)
                .HasPrincipalKey(e => e.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TeamRecord>(entity =>
        {
            entity.ToTable("teams");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrgId).HasColumnName("org_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasOne<OrganizationRecord>()
                .WithMany()
                .HasForeignKey(e => e.OrgId)
                .HasPrincipalKey(e => e.Id)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.OrgId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<TeamMemberEntry>(entity =>
        {
            entity.ToTable("team_members");
            entity.HasKey(e => new { e.TeamId, e.Username });
            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.Username).HasColumnName("username");
            entity.Property(e => e.JoinedAt).HasColumnName("joined_at");
            entity.HasOne<TeamRecord>()
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .HasPrincipalKey(e => e.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScopeRecord>(entity =>
        {
            entity.ToTable("scopes");
            entity.HasKey(e => e.Name);
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OwnerType).HasColumnName("owner_type").IsRequired();
            entity.Property(e => e.OwnerUsername).HasColumnName("owner_username");
            entity.Property(e => e.OwnerOrgId).HasColumnName("owner_org_id");
            entity.Property(e => e.State).HasColumnName("state").IsRequired()
                .HasDefaultValue(Stash.Registry.Database.Models.ScopeStates.Claimed);
            entity.ToTable("scopes", t =>
            {
                t.HasCheckConstraint(
                    "CK_scopes_owner_type",
                    "owner_type IN ('user', 'org', 'system')");
                // Single-owner invariant: system rows have neither column set;
                // non-system rows have exactly one of owner_username or owner_org_id.
                t.HasCheckConstraint(
                    "CK_scopes_single_owner",
                    "(owner_type = 'system' AND owner_username IS NULL AND owner_org_id IS NULL) OR " +
                    "(owner_type = 'user' AND owner_username IS NOT NULL AND owner_org_id IS NULL) OR " +
                    "(owner_type = 'org' AND owner_org_id IS NOT NULL AND owner_username IS NULL)");
                t.HasCheckConstraint(
                    "CK_scopes_state",
                    "state IN ('claimed', 'pending')");
            });
        });

        modelBuilder.Entity<PackageRoleEntry>(entity =>
        {
            entity.ToTable("package_roles");
            entity.HasKey(e => new { e.PackageName, e.PrincipalType, e.PrincipalId });
            entity.Property(e => e.PackageName).HasColumnName("package_name");
            entity.Property(e => e.PrincipalType).HasColumnName("principal_type");
            entity.Property(e => e.PrincipalId).HasColumnName("principal_id");
            entity.Property(e => e.Role).HasColumnName("role").IsRequired();
            entity.ToTable("package_roles", t =>
            {
                t.HasCheckConstraint(
                    "CK_package_roles_principal_type",
                    "principal_type IN ('user', 'team', 'org')");
                t.HasCheckConstraint(
                    "CK_package_roles_role",
                    "role IN ('owner', 'maintainer', 'publisher', 'reader')");
            });
            entity.HasOne<PackageRecord>()
                .WithMany()
                .HasForeignKey(e => e.PackageName)
                .HasPrincipalKey(e => e.Name)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
