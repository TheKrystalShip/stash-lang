using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Unit tests for <see cref="PermissionResolver"/>.
/// Covers: direct user role, team-mediated, org-mediated, fail-closed on dangling references.
/// </summary>
public sealed class PermissionResolverTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly RegistryDbContext _ctx;
    private readonly PermissionResolver _resolver;
    private readonly StashRegistryDatabase _db;

    public PermissionResolverTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(_conn)
            .Options;
        _ctx = new RegistryDbContext(options);
        _db = new StashRegistryDatabase(_ctx);
        _db.Initialize();
        _resolver = new PermissionResolver(_ctx);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _conn.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> SeedOrgAsync(string orgName, string createdBy)
    {
        var org = await _db.CreateOrgAsync(orgName, null, createdBy);
        return org.Id;
    }

    private async Task SeedPackageAsync(string fullName)
    {
        await _ctx.Packages.AddAsync(new PackageRecord
        {
            Name = fullName,
            Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _ctx.SaveChangesAsync();
    }

    private void ExecRaw(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ── Direct user role ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetEffectiveRole_DirectUserOwner_ReturnsOwner()
    {
        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "user", "alice", "owner");

        string? role = await _resolver.GetEffectiveRoleAsync("alice", "@acme/widgets");

        Assert.Equal("owner", role);
    }

    [Fact]
    public async Task GetEffectiveRole_DirectUserReader_ReturnsReader()
    {
        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "user", "bob", "reader");

        string? role = await _resolver.GetEffectiveRoleAsync("bob", "@acme/widgets");

        Assert.Equal("reader", role);
    }

    [Fact]
    public async Task GetEffectiveRole_NoDirectRole_ReturnsNull()
    {
        await SeedPackageAsync("@acme/widgets");

        string? role = await _resolver.GetEffectiveRoleAsync("charlie", "@acme/widgets");

        Assert.Null(role);
    }

    // ── Team-mediated role ────────────────────────────────────────────────────

    [Fact]
    public async Task GetEffectiveRole_TeamMediated_ReturnsTeamRole()
    {
        // Setup: org owns @acme, alice is creator (auto org-owner), set up a team with publisher role.
        await _db.CreateUserAsync("alice", "hash", "user");
        string orgId = await SeedOrgAsync("acme", "alice");
        string teamId = (await _db.CreateTeamAsync(orgId, "publishers")).Id;
        await _db.AddTeamMemberAsync(teamId, "bob");

        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "team", teamId, "publisher");

        string? role = await _resolver.GetEffectiveRoleAsync("bob", "@acme/widgets");

        Assert.Equal("publisher", role);
    }

    [Fact]
    public async Task GetEffectiveRole_TeamMediated_DeniesNonMember()
    {
        await _db.CreateUserAsync("alice", "hash", "user");
        string orgId = await SeedOrgAsync("acme", "alice");
        string teamId = (await _db.CreateTeamAsync(orgId, "publishers")).Id;
        // charlie is NOT added to the team

        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "team", teamId, "publisher");

        string? role = await _resolver.GetEffectiveRoleAsync("charlie", "@acme/widgets");

        Assert.Null(role);
    }

    // ── Org-mediated role ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetEffectiveRole_OrgOwner_InheritsOwner()
    {
        // alice creates the org, so she is org owner → inherits package owner
        await _db.CreateUserAsync("alice", "hash", "user");
        await SeedOrgAsync("acme", "alice"); // auto-provisions @acme scope

        await SeedPackageAsync("@acme/widgets");

        string? role = await _resolver.GetEffectiveRoleAsync("alice", "@acme/widgets");

        Assert.Equal("owner", role);
    }

    [Fact]
    public async Task GetEffectiveRole_OrgMember_InheritsReader()
    {
        await _db.CreateUserAsync("alice", "hash", "user");
        string orgId = await SeedOrgAsync("acme", "alice");
        await _db.AddOrgMemberAsync(orgId, "bob", "member");

        await SeedPackageAsync("@acme/widgets");

        string? role = await _resolver.GetEffectiveRoleAsync("bob", "@acme/widgets");

        Assert.Equal("reader", role);
    }

    [Fact]
    public async Task GetEffectiveRole_NonOrgMember_NoAccess()
    {
        await _db.CreateUserAsync("alice", "hash", "user");
        await SeedOrgAsync("acme", "alice");

        await SeedPackageAsync("@acme/widgets");

        string? role = await _resolver.GetEffectiveRoleAsync("mallory", "@acme/widgets");

        Assert.Null(role);
    }

    // ── Best-of-multiple paths ────────────────────────────────────────────────

    [Fact]
    public async Task GetEffectiveRole_BestOfDirectAndTeam_ReturnsBest()
    {
        await _db.CreateUserAsync("alice", "hash", "user");
        string orgId = await SeedOrgAsync("acme", "alice");
        string teamId = (await _db.CreateTeamAsync(orgId, "maintainers")).Id;
        await _db.AddTeamMemberAsync(teamId, "bob");

        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "user", "bob", "reader");    // direct: reader
        await _db.AssignPackageRoleAsync("@acme/widgets", "team", teamId, "maintainer"); // team: maintainer

        string? role = await _resolver.GetEffectiveRoleAsync("bob", "@acme/widgets");

        // maintainer outranks reader
        Assert.Equal("maintainer", role);
    }

    // ── Fail-closed: dangling team reference ──────────────────────────────────

    [Fact]
    public async Task GetEffectiveRole_DanglingTeamReference_ReturnsNull()
    {
        // Build a scenario where the PackageRoleEntry points to a team that was subsequently deleted.
        // We do this by inserting the role entry directly and then deleting the TeamRecord.
        await _db.CreateUserAsync("alice", "hash", "user");
        string orgId = await SeedOrgAsync("acme", "alice");
        string teamId = (await _db.CreateTeamAsync(orgId, "engineers")).Id;
        await _db.AddTeamMemberAsync(teamId, "bob");

        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "team", teamId, "owner");

        // Confirm bob can access the package before the deletion
        string? roleBefore = await _resolver.GetEffectiveRoleAsync("bob", "@acme/widgets");
        Assert.Equal("owner", roleBefore);

        // Now delete the team. This cascades TeamMemberEntry rows (FK) but NOT PackageRoleEntry
        // rows (no FK from package_roles.principal_id to teams.id — polymorphic key).
        // Use raw SQL to bypass the TeamRecord→OrgRecord FK cascade that EF would handle.
        ExecRaw($"DELETE FROM teams WHERE id = '{teamId}'");
        _ctx.ChangeTracker.Clear();

        // The PackageRoleEntry with principal_id=teamId still exists, but the team row is gone.
        // The resolver must fail closed: bob gets no access.
        string? roleAfter = await _resolver.GetEffectiveRoleAsync("bob", "@acme/widgets");

        Assert.Null(roleAfter);
    }

    [Fact]
    public async Task GetEffectiveRole_DanglingOrgReference_ReturnsNull()
    {
        // Create an org-owned scope and package, verify access, then delete the org directly
        // to leave a dangling ScopeRecord.OwnerOrgId reference.
        await _db.CreateUserAsync("alice", "hash", "user");
        string orgId = await SeedOrgAsync("acme", "alice");
        await _db.AddOrgMemberAsync(orgId, "bob", "member");

        await SeedPackageAsync("@acme/widgets");

        // Confirm bob can access before deletion
        string? roleBefore = await _resolver.GetEffectiveRoleAsync("bob", "@acme/widgets");
        Assert.Equal("reader", roleBefore);

        // Delete the org record (bypassing the app-level 409 check via raw SQL).
        // This leaves the ScopeRecord's owner_org_id pointing at a missing org.
        ExecRaw($"DELETE FROM org_members WHERE org_id = '{orgId}'");
        ExecRaw($"DELETE FROM organizations WHERE id = '{orgId}'");
        _ctx.ChangeTracker.Clear();

        // Scope still exists with owner_org_id set, but the org row is gone.
        // Resolver must fail closed.
        string? roleAfter = await _resolver.GetEffectiveRoleAsync("bob", "@acme/widgets");

        Assert.Null(roleAfter);
    }

    // ── IsOrgMemberAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task IsOrgMember_ExistingMember_ReturnsTrue()
    {
        await _db.CreateUserAsync("alice", "hash", "user");
        string orgId = await SeedOrgAsync("acme", "alice");
        await _db.AddOrgMemberAsync(orgId, "bob", "member");

        bool result = await _resolver.IsOrgMemberAsync("bob", orgId);

        Assert.True(result);
    }

    [Fact]
    public async Task IsOrgMember_NonMember_ReturnsFalse()
    {
        await _db.CreateUserAsync("alice", "hash", "user");
        string orgId = await SeedOrgAsync("acme", "alice");

        bool result = await _resolver.IsOrgMemberAsync("charlie", orgId);

        Assert.False(result);
    }

    [Fact]
    public async Task IsOrgMember_DanglingOrgId_ReturnsFalse()
    {
        // Nonexistent org ID — fail closed without throwing
        bool result = await _resolver.IsOrgMemberAsync("bob", "nonexistent-org-id");

        Assert.False(result);
    }
}
