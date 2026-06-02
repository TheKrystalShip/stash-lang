using System.Net;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Auth;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Tests for fail-closed behavior of the permission resolver:
/// dangling team/org/scope references must yield DENY rather than throw or ALLOW.
/// </summary>
public sealed class FailClosedResolverTests : RegistryAuthzTestBase
{
    // ── Unit-test helpers ─────────────────────────────────────────────────────

    private static (RegistryDbContext Ctx, SqliteConnection Conn) CreateIsolatedContext()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new RegistryDbContext(options);
        ctx.Database.EnsureCreated();
        return (ctx, conn);
    }

    // ── Dangling team: role row points at a deleted team ──────────────────────

    [Fact]
    public async Task FailClosed_DanglingTeamRole_PermissionResolverReturnsNull()
    {
        var (ctx, conn) = CreateIsolatedContext();
        try
        {
            // Seed: org, package, team role, team member — then delete the team row
            ctx.Packages.Add(new PackageRecord
            {
                Name = "@alice/pkg",
                Latest = "1.0.0",
                Visibility = "private",
                CreatedAt = System.DateTime.UtcNow,
                UpdatedAt = System.DateTime.UtcNow
            });
            var org = new OrganizationRecord
            {
                Id = System.Guid.NewGuid().ToString(),
                Name = "alice-team-org",
                CreatedAt = System.DateTime.UtcNow,
                CreatedBy = "alice"
            };
            ctx.Organizations.Add(org);
            await ctx.SaveChangesAsync();

            // Create a team record under the org
            var team = new TeamRecord
            {
                Id = System.Guid.NewGuid().ToString(),
                OrgId = org.Id,
                Name = "devs",
                CreatedAt = System.DateTime.UtcNow
            };
            ctx.Teams.Add(team);
            await ctx.SaveChangesAsync();

            // Assign team a reader role on the package
            ctx.PackageRoles.Add(new PackageRoleEntry
            {
                PackageName = "@alice/pkg",
                PrincipalType = PrincipalTypes.Team,
                PrincipalId = team.Id,
                Role = PackageRoles.Reader
            });
            // Make bob a member of the team
            ctx.TeamMembers.Add(new TeamMemberEntry
            {
                TeamId = team.Id,
                Username = "bob",
                JoinedAt = System.DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();

            // Delete the team row directly via raw SQL (bypass EF cascade to preserve TeamMemberEntry)
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM teams WHERE id = '{team.Id}'";
            cmd.ExecuteNonQuery();

            // PermissionResolver must return null — no grant through dangling team
            ctx.ChangeTracker.Clear(); // force re-query
            var resolver = new PermissionResolver(ctx);
            string? role = await resolver.GetEffectiveRoleAsync("bob", "@alice/pkg");

            Assert.Null(role);
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }

    // ── Dangling org: role row points at a deleted org ────────────────────────

    [Fact]
    public async Task FailClosed_DanglingOrgRole_PermissionResolverReturnsNull()
    {
        var (ctx, conn) = CreateIsolatedContext();
        try
        {
            // Create an org
            var org = new OrganizationRecord
            {
                Id = System.Guid.NewGuid().ToString(),
                Name = "acme",
                CreatedAt = System.DateTime.UtcNow,
                CreatedBy = "alice"
            };
            ctx.Organizations.Add(org);

            // Create scope owned by the org
            ctx.Scopes.Add(new ScopeRecord
            {
                Name = "acme",
                OwnerType = ScopeOwnerTypes.Org,
                OwnerOrgId = org.Id
            });

            // Create package under org-owned scope
            ctx.Packages.Add(new PackageRecord
            {
                Name = "@acme/lib",
                Latest = "1.0.0",
                Visibility = "private",
                CreatedAt = System.DateTime.UtcNow,
                UpdatedAt = System.DateTime.UtcNow
            });
            ctx.PackageRoles.Add(new PackageRoleEntry
            {
                PackageName = "@acme/lib",
                PrincipalType = PrincipalTypes.User,
                PrincipalId = "alice",
                Role = PackageRoles.Owner
            });

            // Add bob as org member
            ctx.OrgMembers.Add(new OrgMemberEntry
            {
                OrgId = org.Id,
                Username = "bob",
                OrgRole = OrgRoles.Member,
                JoinedAt = System.DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();

            // Verify bob has access before deletion
            ctx.ChangeTracker.Clear();
            var resolver = new PermissionResolver(ctx);
            bool isMember = await resolver.IsOrgMemberAsync("bob", org.Id);
            Assert.True(isMember); // sanity check

            // Delete org row directly via raw SQL (bypass cascade to keep OrgMemberEntry)
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM organizations WHERE id = '{org.Id}'";
            cmd.ExecuteNonQuery();

            // Fail-closed: IsOrgMemberAsync checks org existence first → false
            ctx.ChangeTracker.Clear();
            bool memberAfter = await resolver.IsOrgMemberAsync("bob", org.Id);
            Assert.False(memberAfter);

            // GetEffectiveRoleAsync: org-mediated path skipped (org gone) → null
            string? role = await resolver.GetEffectiveRoleAsync("bob", "@acme/lib");
            Assert.Null(role);
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }

    // ── Dangling scope: package row's owning scope was removed ────────────────

    [Fact]
    public async Task FailClosed_DanglingScope_CreatePackageDenied()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-dscope");
        await SeedScopeAsync(factory, "alice-dscope", "alice-dscope");
        SetBearer(client, token);

        // First publish succeeds
        byte[] tarball = CreateTarball("@alice-dscope/lib", "1.0.0");
        var create = await client.PutAsync("/api/v1/packages/alice-dscope/lib",
            TarballContent(tarball));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // Remove the scope row via ADO.NET on the shared connection
        using (var svc = factory.Services.CreateScope())
        {
            var dbCtx = svc.ServiceProvider.GetRequiredService<Stash.Registry.Database.RegistryDbContext>();
            var underlying = (Microsoft.Data.Sqlite.SqliteConnection)dbCtx.Database.GetDbConnection();
            bool wasOpen = underlying.State == System.Data.ConnectionState.Open;
            if (!wasOpen) await underlying.OpenAsync();
            using var cmd = underlying.CreateCommand();
            cmd.CommandText = "DELETE FROM scopes WHERE name = 'alice-dscope'";
            await cmd.ExecuteNonQueryAsync();
        }

        // Creating a new package into the now-dangling scope → DENY (no throw, clean 403)
        byte[] tarball2 = CreateTarball("@alice-dscope/lib2", "1.0.0");
        var denied = await client.PutAsync("/api/v1/packages/alice-dscope/lib2",
            TarballContent(tarball2));

        // Must be a clean 403, not a 500
        Assert.NotEqual(HttpStatusCode.InternalServerError, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
