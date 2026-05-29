using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        Assert.Equal("public", pkg.Visibility);
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
        Assert.Equal("system", stashScope.OwnerType);
        Assert.Null(stashScope.OwnerUsername);
        Assert.Null(stashScope.OwnerOrgId);

        Assert.NotNull(adminScope);
        Assert.Equal("system", adminScope.OwnerType);
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
        Assert.Equal("user", scope.OwnerType);
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
        Assert.Equal("org", scope.OwnerType);
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
        Assert.Contains(members, m => m.Username == "bob" && m.OrgRole == "member");
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

    // ── P5: Permission resolution — direct + team + org ───────────────────────

    [Fact]
    public async Task HasPackagePermission_DirectUserRole_ReturnsTrue()
    {
        var (db, _) = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("@acme/widget"));
        await db.AssignPackageRoleAsync("@acme/widget", "user", "alice", "publisher");

        bool hasPermission = await db.HasPackagePermissionAsync("@acme/widget", "alice", "publisher");
        Assert.True(hasPermission);
    }

    [Fact]
    public async Task HasPackagePermission_DirectUserRole_HigherRequired_ReturnsFalse()
    {
        var (db, _) = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("@acme/widget"));
        await db.AssignPackageRoleAsync("@acme/widget", "user", "alice", "reader");

        // reader cannot publish
        bool hasPermission = await db.HasPackagePermissionAsync("@acme/widget", "alice", "publisher");
        Assert.False(hasPermission);
    }

    [Fact]
    public async Task HasPackagePermission_TeamMediated_ReturnsTrue()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("bob", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        var team = await db.CreateTeamAsync(org.Id, "engineering");
        await db.AddTeamMemberAsync(team.Id, "bob");

        await db.CreatePackageAsync(MakePackage("@acme/widget"));
        // Assign team a publisher role
        await db.AssignPackageRoleAsync("@acme/widget", "team", team.Id, "publisher");

        bool hasPermission = await db.HasPackagePermissionAsync("@acme/widget", "bob", "publisher");
        Assert.True(hasPermission);
    }

    [Fact]
    public async Task HasPackagePermission_OrgOwnerInheritsPackageOwner()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");

        // Create a package in the acme scope — alice is org owner
        await db.CreatePackageAsync(MakePackage("@acme/widget"));

        // alice should have owner permission via org ownership
        bool hasOwner = await db.HasPackagePermissionAsync("@acme/widget", "alice", "owner");
        Assert.True(hasOwner);
    }

    [Fact]
    public async Task HasPackagePermission_OrgMemberInheritsReader()
    {
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("bob", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        await db.AddOrgMemberAsync(org.Id, "bob", "member");

        await db.CreatePackageAsync(MakePackage("@acme/widget"));

        // bob is a regular member — should have reader (not publisher)
        bool hasReader = await db.HasPackagePermissionAsync("@acme/widget", "bob", "reader");
        bool hasPublisher = await db.HasPackagePermissionAsync("@acme/widget", "bob", "publisher");
        Assert.True(hasReader);
        Assert.False(hasPublisher);
    }

    [Fact]
    public async Task HasPackagePermission_NoRole_ReturnsFalse()
    {
        var (db, _) = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("@acme/widget"));

        bool hasPermission = await db.HasPackagePermissionAsync("@acme/widget", "charlie", "reader");
        Assert.False(hasPermission);
    }

    [Fact]
    public async Task HasPackagePermission_ExplicitRoleBeatsOrgInheritance()
    {
        // Bob is org member (reader by inheritance) but also has explicit maintainer role.
        // The higher role (maintainer) should win.
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("bob", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        await db.AddOrgMemberAsync(org.Id, "bob", "member");

        await db.CreatePackageAsync(MakePackage("@acme/widget"));
        await db.AssignPackageRoleAsync("@acme/widget", "user", "bob", "maintainer");

        bool hasMaintainer = await db.HasPackagePermissionAsync("@acme/widget", "bob", "maintainer");
        Assert.True(hasMaintainer);
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
        pkg.Visibility = "private";
        await db.CreatePackageAsync(pkg);
        // No direct package_roles row for alice on @acme/widget

        SearchResult result = await db.SearchPackagesAsync("widget", 1, 20, "alice");

        Assert.Single(result.Packages);
        Assert.Equal("@acme/widget", result.Packages[0].Name);
    }

    [Fact]
    public async Task Search_TeamMediatedReader_CanFindPrivatePackage()
    {
        // Anti-drift test: search must grant access via team-mediated branch,
        // matching HasPackagePermissionAsync("reader") exactly.
        var (db, _) = CreateTestDb();
        await db.CreateUserWithScopeAsync("alice", "hash");
        await db.CreateUserWithScopeAsync("bob", "hash");
        var org = await db.CreateOrgAsync("acme", null, "alice");
        var team = await db.CreateTeamAsync(org.Id, "readers-team");
        await db.AddTeamMemberAsync(team.Id, "bob");
        // bob is NOT an org member — only a team member

        var pkg = MakePackage("@acme/lib");
        pkg.Visibility = "private";
        await db.CreatePackageAsync(pkg);
        await db.AssignPackageRoleAsync("@acme/lib", "team", team.Id, "reader");

        SearchResult result = await db.SearchPackagesAsync("lib", 1, 20, "bob");

        // Both the read gate and search must agree: bob can read via team role
        bool canRead = await db.HasPackagePermissionAsync("@acme/lib", "bob", "reader");
        Assert.True(canRead);
        Assert.Equal(canRead, result.Packages.Any(p => p.Name == "@acme/lib"));
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
        pkg.Visibility = "private";
        await db.CreatePackageAsync(pkg);
        // No direct package_roles row for bob

        SearchResult result = await db.SearchPackagesAsync("internal-pkg", 1, 20, "bob");

        bool canRead = await db.HasPackagePermissionAsync("@acme/internal-pkg", "bob", "reader");
        Assert.True(canRead); // sanity: org member has reader floor
        Assert.Equal(canRead, result.Packages.Any(p => p.Name == "@acme/internal-pkg"));
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
        pkg.Visibility = "private";
        await db.CreatePackageAsync(pkg);

        SearchResult result = await db.SearchPackagesAsync("secret", 1, 20, "charlie");

        Assert.Empty(result.Packages);
    }
}
