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
}
