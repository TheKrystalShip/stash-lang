using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

namespace Stash.Tests.Registry;

/// <summary>
/// Tests for the P2 schema: organizations, org_members, teams, team_members, scopes,
/// package_roles tables, and the packages.visibility column.
/// </summary>
public sealed class RegistryScopeAndOrgTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static (StashRegistryDatabase db, SqliteConnection conn) CreateTestDb()
    {
        // Keep the connection open for the lifetime of the test so the in-memory DB
        // isn't dropped when the DbContext disposes it.
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(conn)
            .Options;
        var context = new RegistryDbContext(options);
        var db = new StashRegistryDatabase(context);
        db.Initialize();
        return (db, conn);
    }

    /// <summary>Execute raw SQL via the same connection used by the test DB.</summary>
    private static void ExecRaw(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static PackageRecord MakePackage(string name) => new()
    {
        Name = name,
        Latest = "1.0.0",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // ── Table existence ──────────────────────────────────────────────────────────

    [Fact]
    public void Initialize_CreatesAllP2Tables()
    {
        var (_, conn) = CreateTestDb();

        string[] expectedTables =
        [
            "packages", "versions", "users", "tokens", "refresh_tokens", "audit_log",
            "organizations", "org_members", "teams", "team_members", "scopes", "package_roles"
        ];

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var reader = cmd.ExecuteReader();
        var actualTables = new System.Collections.Generic.List<string>();
        while (reader.Read()) actualTables.Add(reader.GetString(0));

        foreach (string table in expectedTables)
        {
            Assert.Contains(table, actualTables);
        }
    }

    [Fact]
    public void PackagesTable_HasVisibilityColumn()
    {
        var (_, conn) = CreateTestDb();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(packages)";
        using var reader = cmd.ExecuteReader();
        var columns = new System.Collections.Generic.List<string>();
        while (reader.Read()) columns.Add(reader.GetString(1)); // column name at index 1

        Assert.Contains("visibility", columns);
    }

    // ── packages.visibility CHECK constraint ─────────────────────────────────────

    [Fact]
    public async Task PackageVisibility_DefaultsToPublic()
    {
        var (db, _) = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("@alice/pub-pkg"));

        PackageRecord? pkg = await db.GetPackageAsync("@alice/pub-pkg");

        Assert.NotNull(pkg);
        Assert.Equal(Visibilities.Public, pkg.Visibility);
    }

    [Fact]
    public void PackageVisibility_InvalidValue_FailsCheckConstraint()
    {
        var (_, conn) = CreateTestDb();

        // Insert a package with a valid visibility first, then try invalid via raw SQL
        // to bypass EF Core's model validation and hit the DB-level CHECK constraint.
        ExecRaw(conn, """
            INSERT INTO packages (name, latest, created_at, updated_at, deprecated, visibility)
            VALUES ('@scope/valid', '1.0.0', '2026-01-01', '2026-01-01', 0, 'public')
            """);

        var ex = Assert.Throws<SqliteException>(() =>
            ExecRaw(conn, """
                INSERT INTO packages (name, latest, created_at, updated_at, deprecated, visibility)
                VALUES ('@scope/bad-vis', '1.0.0', '2026-01-01', '2026-01-01', 0, 'secret')
                """));

        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("internal")]
    public void PackageVisibility_ValidValues_Accepted(string visibility)
    {
        var (_, conn) = CreateTestDb();

        // Should not throw
        ExecRaw(conn, $"""
            INSERT INTO packages (name, latest, created_at, updated_at, deprecated, visibility)
            VALUES ('@scope/{visibility}-pkg', '1.0.0', '2026-01-01', '2026-01-01', 0, '{visibility}')
            """);
    }

    // ── scopes single-owner CHECK constraint ─────────────────────────────────────

    [Fact]
    public void ScopeRecord_SystemOwner_NeitherColumnRequired()
    {
        var (_, conn) = CreateTestDb();

        // system scope with both owner columns NULL is valid
        ExecRaw(conn,
            "INSERT INTO scopes (name, owner_type, owner_username, owner_org_id) " +
            "VALUES ('stash', 'system', NULL, NULL)");
    }

    [Fact]
    public void ScopeRecord_UserOwner_RequiresOwnerUsername()
    {
        var (_, conn) = CreateTestDb();

        // valid: owner_type=user + owner_username set
        ExecRaw(conn,
            "INSERT INTO scopes (name, owner_type, owner_username, owner_org_id) " +
            "VALUES ('alice', 'user', 'alice', NULL)");
    }

    [Fact]
    public void ScopeRecord_UserOwner_WithBothColumnsSet_FailsCheckConstraint()
    {
        var (_, conn) = CreateTestDb();

        var ex = Assert.Throws<SqliteException>(() =>
            ExecRaw(conn,
                "INSERT INTO scopes (name, owner_type, owner_username, owner_org_id) " +
                "VALUES ('bad', 'user', 'alice', 'org-123')"));

        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScopeRecord_UserOwner_WithNoOwnerColumn_FailsCheckConstraint()
    {
        var (_, conn) = CreateTestDb();

        var ex = Assert.Throws<SqliteException>(() =>
            ExecRaw(conn,
                "INSERT INTO scopes (name, owner_type, owner_username, owner_org_id) " +
                "VALUES ('bad2', 'user', NULL, NULL)"));

        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScopeRecord_OrgOwner_RequiresOwnerOrgId()
    {
        var (_, conn) = CreateTestDb();

        // valid: owner_type=org + owner_org_id set
        ExecRaw(conn,
            "INSERT INTO scopes (name, owner_type, owner_username, owner_org_id) " +
            "VALUES ('acme', 'org', NULL, 'org-abc')");
    }

    [Fact]
    public void ScopeRecord_InvalidOwnerType_FailsCheckConstraint()
    {
        var (_, conn) = CreateTestDb();

        var ex = Assert.Throws<SqliteException>(() =>
            ExecRaw(conn,
                "INSERT INTO scopes (name, owner_type, owner_username, owner_org_id) " +
                "VALUES ('bad3', 'robot', 'alice', NULL)"));

        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── package_roles principal_type and role CHECK constraints ─────────────────

    [Fact]
    public void PackageRoleEntry_InvalidPrincipalType_FailsCheckConstraint()
    {
        var (_, conn) = CreateTestDb();
        ExecRaw(conn,
            "INSERT INTO packages (name, latest, created_at, updated_at, deprecated, visibility) " +
            "VALUES ('@scope/chk-pkg', '1.0.0', '2026-01-01', '2026-01-01', 0, 'public')");

        var ex = Assert.Throws<SqliteException>(() =>
            ExecRaw(conn,
                "INSERT INTO package_roles (package_name, principal_type, principal_id, role) " +
                "VALUES ('@scope/chk-pkg', 'company', 'acme', 'owner')"));

        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackageRoleEntry_InvalidRole_FailsCheckConstraint()
    {
        var (_, conn) = CreateTestDb();
        ExecRaw(conn,
            "INSERT INTO packages (name, latest, created_at, updated_at, deprecated, visibility) " +
            "VALUES ('@scope/chk2-pkg', '1.0.0', '2026-01-01', '2026-01-01', 0, 'public')");

        var ex = Assert.Throws<SqliteException>(() =>
            ExecRaw(conn,
                "INSERT INTO package_roles (package_name, principal_type, principal_id, role) " +
                "VALUES ('@scope/chk2-pkg', 'user', 'alice', 'superuser')"));

        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("team")]
    [InlineData("org")]
    public void PackageRoleEntry_ValidPrincipalTypes_Accepted(string principalType)
    {
        var (_, conn) = CreateTestDb();
        ExecRaw(conn,
            $"INSERT INTO packages (name, latest, created_at, updated_at, deprecated, visibility) " +
            $"VALUES ('@scope/{principalType}-type', '1.0.0', '2026-01-01', '2026-01-01', 0, 'public')");

        // Should not throw
        ExecRaw(conn,
            $"INSERT INTO package_roles (package_name, principal_type, principal_id, role) " +
            $"VALUES ('@scope/{principalType}-type', '{principalType}', 'id-123', 'reader')");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("maintainer")]
    [InlineData("publisher")]
    [InlineData("reader")]
    public void PackageRoleEntry_ValidRoles_Accepted(string role)
    {
        var (_, conn) = CreateTestDb();
        ExecRaw(conn,
            $"INSERT INTO packages (name, latest, created_at, updated_at, deprecated, visibility) " +
            $"VALUES ('@scope/role-{role}', '1.0.0', '2026-01-01', '2026-01-01', 0, 'public')");

        // Should not throw
        ExecRaw(conn,
            $"INSERT INTO package_roles (package_name, principal_type, principal_id, role) " +
            $"VALUES ('@scope/role-{role}', 'user', 'alice', '{role}')");
    }

    // ── org_members org_role CHECK constraint ────────────────────────────────────

    [Fact]
    public void OrgMemberEntry_InvalidOrgRole_FailsCheckConstraint()
    {
        var (_, conn) = CreateTestDb();
        ExecRaw(conn,
            "INSERT INTO organizations (id, name, created_at, created_by) " +
            "VALUES ('org-1', 'acme', '2026-01-01', 'alice')");

        var ex = Assert.Throws<SqliteException>(() =>
            ExecRaw(conn,
                "INSERT INTO org_members (org_id, username, org_role, joined_at) " +
                "VALUES ('org-1', 'alice', 'superadmin', '2026-01-01')"));

        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("member")]
    public void OrgMemberEntry_ValidOrgRoles_Accepted(string orgRole)
    {
        var (_, conn) = CreateTestDb();
        ExecRaw(conn,
            $"INSERT INTO organizations (id, name, created_at, created_by) " +
            $"VALUES ('org-{orgRole}', 'org-{orgRole}', '2026-01-01', 'alice')");

        // Should not throw
        ExecRaw(conn,
            $"INSERT INTO org_members (org_id, username, org_role, joined_at) " +
            $"VALUES ('org-{orgRole}', 'alice', '{orgRole}', '2026-01-01')");
    }

    // ── P3: Bootstrap system scope seeding ───────────────────────────────────────

    [Fact]
    public async Task SeedSystemScopes_FreshDb_CreatesBothSystemScopes()
    {
        var (db, _) = CreateTestDb();

        await db.SeedSystemScopesAsync();

        ScopeRecord? stashScope = await db.GetScopeAsync("stash");
        ScopeRecord? adminScope = await db.GetScopeAsync("admin");

        Assert.NotNull(stashScope);
        Assert.Equal(ScopeOwnerTypes.System, stashScope.OwnerType);
        Assert.Null(stashScope.OwnerUsername);
        Assert.Null(stashScope.OwnerOrgId);

        Assert.NotNull(adminScope);
        Assert.Equal(ScopeOwnerTypes.System, adminScope.OwnerType);
        Assert.Null(adminScope.OwnerUsername);
        Assert.Null(adminScope.OwnerOrgId);
    }

    [Fact]
    public async Task SeedSystemScopes_AlreadySeeded_IsIdempotent()
    {
        var (db, _) = CreateTestDb();

        // Seed twice — should not throw or duplicate
        await db.SeedSystemScopesAsync();
        await db.SeedSystemScopesAsync();

        ScopeRecord? stashScope = await db.GetScopeAsync("stash");
        ScopeRecord? adminScope = await db.GetScopeAsync("admin");

        Assert.NotNull(stashScope);
        Assert.NotNull(adminScope);
    }

    // ── P3: Registration auto-provisions @<username> scope ──────────────────────

    [Fact]
    public async Task CreateUserWithScope_NewUser_ProvisionesPersonalScope()
    {
        var (db, _) = CreateTestDb();

        await db.CreateUserWithScopeAsync("alice", "h4sh3d");

        ScopeRecord? scope = await db.GetScopeAsync("alice");
        Assert.NotNull(scope);
        Assert.Equal(ScopeOwnerTypes.User, scope.OwnerType);
        Assert.Equal("alice", scope.OwnerUsername);
        Assert.Null(scope.OwnerOrgId);
    }

    [Fact]
    public async Task CreateUserWithScope_FirstUser_BecomesAdmin()
    {
        var (db, _) = CreateTestDb();

        string role = await db.CreateUserWithScopeAsync("alice", "h4sh3d");

        Assert.Equal("admin", role);
    }

    [Fact]
    public async Task CreateUserWithScope_SecondUser_GetsUserRole()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "h4sh3d");

        string role = await db.CreateUserWithScopeAsync("bob", "h4sh3d");

        Assert.Equal("user", role);
    }

    [Fact]
    public async Task CreateUserWithScope_DuplicateUsername_ThrowsAndNoOrphanScope()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "h4sh3d");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.CreateUserWithScopeAsync("alice", "other-hash"));

        // Scope must exist exactly once (from the first call)
        bool scopeExists = await db.ScopeExistsAsync("alice");
        Assert.True(scopeExists);
    }

    // ── P3: Registration collision with existing scopes (done_when #3) ──────────

    [Fact]
    public async Task CreateUserWithScope_CollisionWithSystemScope_Throws409()
    {
        var (db, _) = CreateTestDb();

        // Seed system scopes first (as bootstrap does)
        await db.SeedSystemScopesAsync();

        // 'stash' is a system scope — registering a user named 'stash' must fail
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.CreateUserWithScopeAsync("stash", "h4sh3d"));

        Assert.Contains("scope", ex.Message, StringComparison.OrdinalIgnoreCase);

        // No user row must have been created
        var user = await db.GetUserAsync("stash");
        Assert.Null(user);
    }

    [Fact]
    public async Task CreateUserWithScope_CollisionWithAdminSystemScope_Throws()
    {
        var (db, _) = CreateTestDb();
        await db.SeedSystemScopesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.CreateUserWithScopeAsync("admin", "h4sh3d"));

        var user = await db.GetUserAsync("admin");
        Assert.Null(user);
    }

    [Fact]
    public async Task CreateUserWithScope_CollisionWithExistingUserScope_ThrowsAndRollsBack()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "h4sh3d");

        // Register 'alice' again — the scope 'alice' already exists (user-owned)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.CreateUserWithScopeAsync("alice", "other-hash"));
    }

    // ── P3: ScopeExistsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ScopeExistsAsync_NonExistentScope_ReturnsFalse()
    {
        var (db, _) = CreateTestDb();

        bool exists = await db.ScopeExistsAsync("nonexistent");

        Assert.False(exists);
    }

    [Fact]
    public async Task ScopeExistsAsync_ExistingScope_ReturnsTrue()
    {
        var (db, _) = CreateTestDb();
        await db.SeedSystemScopesAsync();

        bool exists = await db.ScopeExistsAsync("stash");

        Assert.True(exists);
    }

    // ── P5: Organization creation ─────────────────────────────────────────────

    [Fact]
    public async Task CreateOrgAsync_NewOrg_CreatesOrgAndScope()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");

        var org = await db.CreateOrgAsync("acme", "Acme Corp", "alice");

        Assert.NotNull(org);
        Assert.Equal("acme", org.Name);
        Assert.Equal("Acme Corp", org.DisplayName);
        Assert.Equal("alice", org.CreatedBy);

        // The scope should have been provisioned
        var scope = await db.GetScopeAsync("acme");
        Assert.NotNull(scope);
        Assert.Equal(ScopeOwnerTypes.Org, scope.OwnerType);
        Assert.Equal(org.Id, scope.OwnerOrgId);
        Assert.Null(scope.OwnerUsername);
    }

    [Fact]
    public async Task CreateOrgAsync_NewOrg_CreatorBecomesOwner()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");

        var org = await db.CreateOrgAsync("acme", null, "alice");

        bool isOwner = await db.IsOrgOwnerAsync(org.Id, "alice");
        Assert.True(isOwner);
    }

    [Fact]
    public async Task CreateOrgAsync_CollidingScope_Throws()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        // 'alice' scope already exists from user registration
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.CreateOrgAsync("alice", null, "alice"));
    }

    [Fact]
    public async Task CreateOrgAsync_CollidingSystemScope_Throws()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.SeedSystemScopesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.CreateOrgAsync("stash", null, "alice"));
    }

    [Fact]
    public async Task GetOrgAsync_ExistingOrg_ReturnsOrg()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateOrgAsync("acme", "Acme", "alice");

        var org = await db.GetOrgAsync("acme");
        Assert.NotNull(org);
        Assert.Equal("acme", org.Name);
    }

    [Fact]
    public async Task GetOrgAsync_NonExistent_ReturnsNull()
    {
        var (db, _) = CreateTestDb();

        var org = await db.GetOrgAsync("nonexistent");
        Assert.Null(org);
    }

    // ── P5: Org membership ────────────────────────────────────────────────────

    [Fact]
    public async Task AddOrgMemberAsync_ValidUser_AddsMember()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("bob", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");

        await db.AddOrgMemberAsync(org.Id, "bob", "member");

        var members = await db.GetOrgMembersAsync(org.Id);
        Assert.Contains(members, m => m.Username == "bob" && m.OrgRole == OrgRoles.Member);
    }

    [Fact]
    public async Task AddOrgMemberAsync_DuplicateMember_Throws()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");

        // alice is already an owner-member from CreateOrgAsync
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.AddOrgMemberAsync(org.Id, "alice", "member"));
    }

    [Fact]
    public async Task RemoveOrgMemberAsync_ExistingMember_RemovesMember()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("bob", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        await db.AddOrgMemberAsync(org.Id, "bob", "member");

        await db.RemoveOrgMemberAsync(org.Id, "bob");

        var members = await db.GetOrgMembersAsync(org.Id);
        Assert.DoesNotContain(members, m => m.Username == "bob");
    }

    [Fact]
    public async Task IsOrgOwnerAsync_OwnerUser_ReturnsTrue()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");

        bool isOwner = await db.IsOrgOwnerAsync(org.Id, "alice");
        Assert.True(isOwner);
    }

    [Fact]
    public async Task IsOrgOwnerAsync_MemberUser_ReturnsFalse()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("bob", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        await db.AddOrgMemberAsync(org.Id, "bob", "member");

        bool isOwner = await db.IsOrgOwnerAsync(org.Id, "bob");
        Assert.False(isOwner);
    }

    // ── P5: Team operations ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateTeamAsync_ValidOrg_CreatesTeam()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");

        var team = await db.CreateTeamAsync(org.Id, "engineering");

        Assert.NotNull(team);
        Assert.Equal("engineering", team.Name);
        Assert.Equal(org.Id, team.OrgId);
    }

    [Fact]
    public async Task CreateTeamAsync_DuplicateNameInSameOrg_Throws()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        await db.CreateTeamAsync(org.Id, "engineering");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.CreateTeamAsync(org.Id, "engineering"));
    }

    [Fact]
    public async Task AddTeamMemberAsync_ValidTeam_AddsMember()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("bob", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        var team = await db.CreateTeamAsync(org.Id, "engineering");

        await db.AddTeamMemberAsync(team.Id, "bob");

        var teamRecord = await db.GetTeamAsync(team.Id);
        Assert.NotNull(teamRecord);
    }

    [Fact]
    public async Task GetTeamByNameAsync_ExistingTeam_ReturnsTeam()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        await db.CreateTeamAsync(org.Id, "engineering");

        var team = await db.GetTeamByNameAsync(org.Id, "engineering");
        Assert.NotNull(team);
        Assert.Equal("engineering", team.Name);
    }

    // ── F03: Search visibility — org-mediated and team-mediated readers ───────

    [Fact]
    public async Task Search_OrgOwnerWithNoDirectRole_CanFindPrivatePackageInOrgScope()
    {
        // Regression for F03: an org owner with NO direct package_roles row must still
        // appear in search results for private packages in their org's scope.
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");

        var pkg = MakePackage("@acme/widget");
        pkg.Visibility = Visibilities.Private;
        await db.CreatePackageAsync(pkg);
        // No direct package_roles row for alice on @acme/widget

        SearchResult result = await db.SearchPackagesAsync("widget", 1, 20, "alice");

        Assert.Single(result.Packages);
        Assert.Equal("@acme/widget", result.Packages[0].Package.Name);
    }

    [Fact]
    public async Task Search_TeamMediatedReader_CanFindPrivatePackage()
    {
        // Anti-drift test: search must grant access via team-mediated branch.
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("bob", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        var team = await db.CreateTeamAsync(org.Id, "readers-team");
        await db.AddTeamMemberAsync(team.Id, "bob");
        // bob is NOT an org member — only a team member

        var pkg = MakePackage("@acme/lib");
        pkg.Visibility = Visibilities.Private;
        await db.CreatePackageAsync(pkg);
        await db.AssignPackageRoleAsync("@acme/lib", "team", team.Id, "reader");

        SearchResult result = await db.SearchPackagesAsync("lib", 1, 20, "bob");

        // Bob can read via team role — search must surface the private package
        Assert.Contains(result.Packages, p => p.Package.Name == "@acme/lib");
    }

    [Fact]
    public async Task Search_OrgMediatedReader_CanFindPrivatePackage()
    {
        // Plain org member (not owner) inherits reader floor and must appear in search.
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("bob", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        await db.AddOrgMemberAsync(org.Id, "bob", "member");

        var pkg = MakePackage("@acme/internal-pkg");
        pkg.Visibility = Visibilities.Private;
        await db.CreatePackageAsync(pkg);
        // No direct package_roles row for bob

        SearchResult result = await db.SearchPackagesAsync("internal-pkg", 1, 20, "bob");

        // Org member inherits reader floor — search must surface the private package
        Assert.Contains(result.Packages, p => p.Package.Name == "@acme/internal-pkg");
    }

    [Fact]
    public async Task Search_UserOutsideOrg_CannotFindPrivateOrgPackage()
    {
        // charlie is not an org member and has no roles — must not see private org packages.
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("charlie", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");

        var pkg = MakePackage("@acme/secret");
        pkg.Visibility = Visibilities.Private;
        await db.CreatePackageAsync(pkg);

        SearchResult result = await db.SearchPackagesAsync("secret", 1, 20, "charlie");

        Assert.Empty(result.Packages);
    }
}

/// <summary>
/// HTTP-level integration tests for F05 (scope collision with usernames/orgs)
/// and F06 (grammar enforcement at register/orgs/scopes endpoints).
/// </summary>
public sealed class RegistryScopeAndOrgHttpTests : IDisposable
{
    private SqliteConnection? _connection;

    public void Dispose() => _connection?.Dispose();

    private WebApplicationFactory<Stash.Registry.Program> CreateDevFactory()
    {
        _connection?.Dispose();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var conn = _connection;

        return new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                // Pin the content root to the registry project relative to the solution
                // so WebApplicationFactory does not guess it from the current working
                // directory (which throws DirectoryNotFoundException in full-suite runs).
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(DbContextOptions<RegistryDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddDbContext<RegistryDbContext>(options =>
                        options.UseSqlite(conn));

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });
    }

    private static StringContent Json(object body) =>
        new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    /// <summary>
    /// Registers a user, logs in, and returns a publish-ceiling Bearer token.
    /// Login now issues a read-ceiling token (P5 least-privilege default); tests that
    /// publish/claim/create-org need an explicit publish-ceiling upgrade.
    /// </summary>
    private static async Task<string> RegisterAndLoginAsync(HttpClient client, string username, string password = "Password123!")
    {
        await client.PostAsync("/api/v1/auth/register", Json(new { username, password }));
        var loginResp = await client.PostAsync("/api/v1/auth/login", Json(new { username, password }));
        loginResp.EnsureSuccessStatusCode();
        string loginJson = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginJson);
        string loginToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        // Login issues a read-ceiling token; upgrade to publish-ceiling for write tests.
        var savedAuth = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginToken);
        var tokenResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "1d" }));
        client.DefaultRequestHeaders.Authorization = savedAuth;

        tokenResp.EnsureSuccessStatusCode();
        string tokenJson = await tokenResp.Content.ReadAsStringAsync();
        using var tokenDoc = JsonDocument.Parse(tokenJson);
        return tokenDoc.RootElement.GetProperty("token").GetString()!;
    }

    // ── F06: Grammar enforcement at POST /auth/register ─────────────────────────

    [Fact]
    public async Task Register_UppercaseUsername_Returns400()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "Alice_42", password = "Password123!" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_LeadingDigitUsername_Returns400()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "9foo", password = "Password123!" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_40CharUsername_Returns400()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        string tooLong = new string('a', 40); // 40 chars — one over the 39-char limit
        var response = await client.PostAsync("/api/v1/auth/register",
            Json(new { username = tooLong, password = "Password123!" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_UnderscoreUsername_Returns400()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/register",
            Json(new { username = "user_name", password = "Password123!" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── F06: Grammar enforcement at POST /api/v1/orgs ───────────────────────────

    [Fact]
    public async Task CreateOrg_InvalidGrammarName_Returns400()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        string token = await RegisterAndLoginAsync(client, "alice");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/v1/orgs",
            Json(new { name = "My_Org" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── F06: Grammar enforcement at POST /api/v1/scopes ─────────────────────────

    [Fact]
    public async Task ClaimScope_InvalidGrammarName_Returns400()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        string token = await RegisterAndLoginAsync(client, "alice");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/v1/scopes",
            Json(new { scope = "9invalid", owner_type = "user", owner = "alice" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── F05: Collision checks at POST /api/v1/scopes ────────────────────────────

    [Fact]
    public async Task ClaimScope_CollisionWithExistingUsername_Returns409()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        // Register alice — this auto-provisions the 'alice' scope
        string token = await RegisterAndLoginAsync(client, "alice");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Now try to claim the scope 'alice' explicitly — must collide with the user
        var response = await client.PostAsync("/api/v1/scopes",
            Json(new { scope = "alice", owner_type = "user", owner = "alice" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ClaimScope_CollisionWithExistingOrgName_Returns409()
    {
        await using var factory = CreateDevFactory();
        using var client = factory.CreateClient();

        string token = await RegisterAndLoginAsync(client, "alice");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Create the org 'acme' — auto-provisions the 'acme' scope
        await client.PostAsync("/api/v1/orgs", Json(new { name = "acme" }));

        // Now try to claim the scope 'acme' for alice — must collide with the org
        var response = await client.PostAsync("/api/v1/scopes",
            Json(new { scope = "acme", owner_type = "user", owner = "alice" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
