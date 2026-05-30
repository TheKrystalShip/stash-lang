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
/// Unit tests for <see cref="RegistryAuthorizer"/>.
/// Covers: ceiling checks, admin-narrow intersection, visibility model, role-revoke actions.
/// </summary>
public sealed class RegistryAuthorizerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly RegistryDbContext _ctx;
    private readonly RegistryAuthorizer _authorizer;
    private readonly StashRegistryDatabase _db;

    public RegistryAuthorizerTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(_conn)
            .Options;
        _ctx = new RegistryDbContext(options);
        _db = new StashRegistryDatabase(_ctx);
        _db.Initialize();
        var resolver = new PermissionResolver(_ctx);
        _authorizer = new RegistryAuthorizer(resolver, _ctx);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _conn.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static UserPrincipal MakeUser(
        string username,
        UserRole role = UserRole.User,
        TokenCeiling ceiling = TokenCeiling.Publish,
        string tokenId = "tok-1")
        => new(username, role, ceiling, tokenId);

    private static AnonymousPrincipal Anon() => new();

    private static PackageResource PkgRes(string scope, string name) => new(scope, name);
    private static ScopeResource ScopeRes(string scope) => new(scope);

    private async Task SeedPackageAsync(string fullName, string visibility = "public")
    {
        await _ctx.Packages.AddAsync(new PackageRecord
        {
            Name = fullName,
            Latest = "1.0.0",
            Visibility = visibility,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _ctx.SaveChangesAsync();
    }

    private async Task SeedUserScopeAsync(string username)
    {
        await _db.CreateUserAsync(username, "hash", "user");
        // auto-provisions @<username> scope via CreateUserWithScopeAsync equivalent
        // Use direct DB seeding for simplicity in tests
        _ctx.ChangeTracker.Clear();
        bool scopeExists = await _ctx.Scopes.AnyAsync(s => s.Name == username);
        if (!scopeExists)
        {
            _ctx.Scopes.Add(new ScopeRecord
            {
                Name = username,
                OwnerType = "user",
                OwnerUsername = username
            });
            await _ctx.SaveChangesAsync();
        }
    }

    // ── Ceiling: publish ceiling + publisher role → ALLOW PublishVersion ──────

    [Fact]
    public async Task AuthorizeAsync_PublishCeilingOwner_PublishVersion_Allow()
    {
        await SeedUserScopeAsync("alice");
        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "user", "alice", "owner");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("alice", ceiling: TokenCeiling.Publish),
            RegistryAction.PublishVersion,
            PkgRes("acme", "widgets"));

        Assert.True(result.Allowed);
    }

    // ── Ceiling: read ceiling + PublishVersion → DENY TokenScopeInsufficient ──

    [Fact]
    public async Task AuthorizeAsync_ReadCeiling_PublishVersion_DenyTokenScopeInsufficient()
    {
        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "user", "alice", "owner");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("alice", ceiling: TokenCeiling.Read),
            RegistryAction.PublishVersion,
            PkgRes("acme", "widgets"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.TokenScopeInsufficient, result.Reason);
    }

    // ── Ceiling: read ceiling + ReadPackageMetadata + public → ALLOW ──────────
    // (public visibility bypasses even the "read ceiling required" so anonymous works too)

    [Fact]
    public async Task AuthorizeAsync_ReadCeiling_ReadPublicPackage_Allow()
    {
        await SeedPackageAsync("@acme/widgets", "public");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("alice", ceiling: TokenCeiling.Read),
            RegistryAction.ReadPackageMetadata,
            PkgRes("acme", "widgets"));

        Assert.True(result.Allowed);
    }

    // ── Anonymous + public package read → ALLOW ───────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_Anonymous_ReadPublicPackage_Allow()
    {
        await SeedPackageAsync("@acme/widgets", "public");

        var result = await _authorizer.AuthorizeAsync(
            Anon(),
            RegistryAction.ReadPackageMetadata,
            PkgRes("acme", "widgets"));

        Assert.True(result.Allowed);
    }

    // ── Anonymous + PublishVersion → DENY NotAuthenticated ───────────────────

    [Fact]
    public async Task AuthorizeAsync_Anonymous_PublishVersion_DenyNotAuthenticated()
    {
        await SeedPackageAsync("@acme/widgets");

        var result = await _authorizer.AuthorizeAsync(
            Anon(),
            RegistryAction.PublishVersion,
            PkgRes("acme", "widgets"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.NotAuthenticated, result.Reason);
    }

    // ── Anonymous + private package read → DENY VisibilityHidden ─────────────

    [Fact]
    public async Task AuthorizeAsync_Anonymous_ReadPrivatePackage_DenyVisibilityHidden()
    {
        await SeedPackageAsync("@acme/widgets", "private");

        var result = await _authorizer.AuthorizeAsync(
            Anon(),
            RegistryAction.ReadPackageMetadata,
            PkgRes("acme", "widgets"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.VisibilityHidden, result.Reason);
    }

    // ── Private package: authenticated no-role user → DENY VisibilityHidden ──

    [Fact]
    public async Task AuthorizeAsync_AuthenticatedNoRole_ReadPrivatePackage_DenyVisibilityHidden()
    {
        await SeedPackageAsync("@acme/widgets", "private");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("bob", ceiling: TokenCeiling.Read),
            RegistryAction.ReadPackageMetadata,
            PkgRes("acme", "widgets"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.VisibilityHidden, result.Reason);
    }

    // ── Private package: reader role → ALLOW ─────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_ReaderRole_ReadPrivatePackage_Allow()
    {
        await SeedPackageAsync("@acme/widgets", "private");
        await _db.AssignPackageRoleAsync("@acme/widgets", "user", "bob", "reader");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("bob", ceiling: TokenCeiling.Read),
            RegistryAction.ReadPackageMetadata,
            PkgRes("acme", "widgets"));

        Assert.True(result.Allowed);
    }

    // ── Internal package: org member → ALLOW ─────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_OrgMember_ReadInternalPackage_Allow()
    {
        await _db.CreateUserAsync("alice", "hash", "user");
        var org = await _db.CreateOrgAsync("acme", null, "alice");
        await _db.AddOrgMemberAsync(org.Id, "bob", "member");
        await SeedPackageAsync("@acme/widgets", "internal");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("bob", ceiling: TokenCeiling.Read),
            RegistryAction.ReadPackageMetadata,
            PkgRes("acme", "widgets"));

        Assert.True(result.Allowed);
    }

    // ── Internal package: non-org member → DENY VisibilityHidden ─────────────

    [Fact]
    public async Task AuthorizeAsync_NonOrgMember_ReadInternalPackage_DenyVisibilityHidden()
    {
        await _db.CreateUserAsync("alice", "hash", "user");
        await _db.CreateOrgAsync("acme", null, "alice");
        await SeedPackageAsync("@acme/widgets", "internal");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("mallory", ceiling: TokenCeiling.Read),
            RegistryAction.ReadPackageMetadata,
            PkgRes("acme", "widgets"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.VisibilityHidden, result.Reason);
    }

    // ── Admin narrow: admin + publish ceiling → ALLOW write without direct role

    [Fact]
    public async Task AuthorizeAsync_AdminPublishCeiling_PublishVersion_NoDirectRole_Allow()
    {
        await SeedPackageAsync("@acme/widgets");
        // admin has no direct role on the package

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("root", role: UserRole.Admin, ceiling: TokenCeiling.Publish),
            RegistryAction.PublishVersion,
            PkgRes("acme", "widgets"));

        Assert.True(result.Allowed);
    }

    // ── Admin narrow: admin + read ceiling → DENY TokenScopeInsufficient ──────

    [Fact]
    public async Task AuthorizeAsync_AdminReadCeiling_PublishVersion_DenyTokenScopeInsufficient()
    {
        await SeedPackageAsync("@acme/widgets");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("root", role: UserRole.Admin, ceiling: TokenCeiling.Read),
            RegistryAction.PublishVersion,
            PkgRes("acme", "widgets"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.TokenScopeInsufficient, result.Reason);
    }

    // ── Admin narrow: admin + read ceiling on private package → ALLOW ─────────

    [Fact]
    public async Task AuthorizeAsync_AdminReadCeiling_ReadPrivatePackage_Allow()
    {
        await SeedPackageAsync("@acme/widgets", "private");
        // admin, read ceiling, no direct role → should ALLOW because read is sufficient
        // for ReadPackageMetadata and admin short-circuits resource check

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("root", role: UserRole.Admin, ceiling: TokenCeiling.Read),
            RegistryAction.ReadPackageMetadata,
            PkgRes("acme", "widgets"));

        Assert.True(result.Allowed);
    }

    // ── RevokePackageRole: owner → ALLOW ─────────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_OwnerRole_RevokePackageRole_Allow()
    {
        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "user", "alice", "owner");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("alice", ceiling: TokenCeiling.Publish),
            RegistryAction.RevokePackageRole,
            PkgRes("acme", "widgets"));

        Assert.True(result.Allowed);
    }

    // ── RevokePackageRole: non-owner → DENY PackageRoleInsufficient ───────────

    [Fact]
    public async Task AuthorizeAsync_NonOwner_RevokePackageRole_DenyPackageRoleInsufficient()
    {
        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "user", "alice", "publisher");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("alice", ceiling: TokenCeiling.Publish),
            RegistryAction.RevokePackageRole,
            PkgRes("acme", "widgets"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.PackageRoleInsufficient, result.Reason);
    }

    // ── AdminRevokePackageRole: admin → ALLOW ─────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_Admin_AdminRevokePackageRole_Allow()
    {
        await SeedPackageAsync("@acme/widgets");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("root", role: UserRole.Admin, ceiling: TokenCeiling.Admin),
            RegistryAction.AdminRevokePackageRole,
            PkgRes("acme", "widgets"));

        Assert.True(result.Allowed);
    }

    // ── AdminRevokePackageRole: non-admin → DENY ──────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_NonAdmin_AdminRevokePackageRole_Deny()
    {
        await SeedPackageAsync("@acme/widgets");
        await _db.AssignPackageRoleAsync("@acme/widgets", "user", "alice", "owner");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("alice", ceiling: TokenCeiling.Admin),
            RegistryAction.AdminRevokePackageRole,
            PkgRes("acme", "widgets"));

        Assert.False(result.Allowed);
    }

    // ── AdminRevokePackageRole: admin + read ceiling → DENY (ceiling first) ───

    [Fact]
    public async Task AuthorizeAsync_AdminReadCeiling_AdminRevokePackageRole_DenyTokenScopeInsufficient()
    {
        await SeedPackageAsync("@acme/widgets");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("root", role: UserRole.Admin, ceiling: TokenCeiling.Read),
            RegistryAction.AdminRevokePackageRole,
            PkgRes("acme", "widgets"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.TokenScopeInsufficient, result.Reason);
    }

    // ── ScopeReserved: reserved scope CreatePackage → DENY ScopeReserved ──────

    [Fact]
    public async Task AuthorizeAsync_ReservedScope_CreatePackage_DenyScopeReserved()
    {
        var result = await _authorizer.AuthorizeAsync(
            MakeUser("alice", ceiling: TokenCeiling.Publish),
            RegistryAction.CreatePackage,
            PkgRes("stash", "anything"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.ScopeReserved, result.Reason);
    }

    [Fact]
    public async Task AuthorizeAsync_AdminReservedScope_CreatePackage_DenyScopeReserved()
    {
        // Even admin cannot publish into reserved scopes
        var result = await _authorizer.AuthorizeAsync(
            MakeUser("root", role: UserRole.Admin, ceiling: TokenCeiling.Publish),
            RegistryAction.CreatePackage,
            PkgRes("admin", "anything"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.ScopeReserved, result.Reason);
    }

    // ── CreatePackage: unclaimed scope (claim policy) → DENY ScopeNotOwned ───

    [Fact]
    public async Task AuthorizeAsync_UnclaimedScope_CreatePackage_DenyScopeNotOwned()
    {
        var result = await _authorizer.AuthorizeAsync(
            MakeUser("alice", ceiling: TokenCeiling.Publish),
            RegistryAction.CreatePackage,
            PkgRes("bigcorp", "utils"));

        Assert.False(result.Allowed);
        Assert.Equal(AuthzDenyReason.ScopeNotOwned, result.Reason);
    }

    // ── CreatePackage: caller owns scope → ALLOW ─────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_CallerOwnsScope_CreatePackage_Allow()
    {
        await SeedUserScopeAsync("alice");

        var result = await _authorizer.AuthorizeAsync(
            MakeUser("alice", ceiling: TokenCeiling.Publish),
            RegistryAction.CreatePackage,
            PkgRes("alice", "mylib"));

        Assert.True(result.Allowed);
    }

    // ── Search: anyone → ALLOW ────────────────────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_Anonymous_Search_Allow()
    {
        var result = await _authorizer.AuthorizeAsync(
            Anon(),
            RegistryAction.Search,
            new SearchResource());

        Assert.True(result.Allowed);
    }

    // ── PermissionResolver fail-closed: dangling team → PDP returns DENY ──────

    [Fact]
    public async Task AuthorizeAsync_DanglingTeamRole_PublishVersion_DenyPackageRoleInsufficient()
    {
        await _db.CreateUserAsync("alice", "hash", "user");
        var org = await _db.CreateOrgAsync("acme", null, "alice");
        var team = await _db.CreateTeamAsync(org.Id, "engineers");
        await _db.AddTeamMemberAsync(team.Id, "bob");

        await SeedPackageAsync("@acme/widgets", "public");
        await _db.AssignPackageRoleAsync("@acme/widgets", "team", team.Id, "owner");

        // Confirm bob can publish before the team is deleted
        var before = await _authorizer.AuthorizeAsync(
            MakeUser("bob", ceiling: TokenCeiling.Publish),
            RegistryAction.PublishVersion,
            PkgRes("acme", "widgets"));
        Assert.True(before.Allowed);

        // Delete the team via raw SQL (simulates dangling reference)
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM teams WHERE id = '{team.Id}'";
        cmd.ExecuteNonQuery();
        _ctx.ChangeTracker.Clear();

        // After the team is deleted, bob's effective role should be null → PDP denies
        var after = await _authorizer.AuthorizeAsync(
            MakeUser("bob", ceiling: TokenCeiling.Publish),
            RegistryAction.PublishVersion,
            PkgRes("acme", "widgets"));

        Assert.False(after.Allowed);
        Assert.Equal(AuthzDenyReason.PackageRoleInsufficient, after.Reason);
    }
}
