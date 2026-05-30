using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Database;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Tests for cascade-refusal invariants:
/// - DELETE /scopes/{scope} while scope owns packages → 409 ScopeNotEmpty
/// - DELETE /orgs/{org} while org owns scopes or packages → 409 OrgNotEmpty
/// - After children deleted, both deletes succeed 204.
/// </summary>
public sealed class CascadeRefusalTests : RegistryAuthzTestBase
{
    // ── Scope: non-empty scope cannot be deleted ──────────────────────────────

    [Fact]
    public async Task DeleteScope_WithPackages_Returns409_RowsUnchanged()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "cascade-scope1");
        await SeedScopeAsync(factory, "cascade-scope1", "cascade-scope1");
        await SeedPackageAsync(factory, "@cascade-scope1/lib", "cascade-scope1");
        SetBearer(client, token);

        // Deletion should fail with 409
        var deleteResp = await client.DeleteAsync("/api/v1/scopes/cascade-scope1");
        Assert.Equal(HttpStatusCode.Conflict, deleteResp.StatusCode);
        string body = await deleteResp.Content.ReadAsStringAsync();
        Assert.Contains("ScopeNotEmpty", body);

        // Scope row must still exist
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        Assert.True(await db.ScopeExistsAsync("cascade-scope1"));
    }

    [Fact]
    public async Task DeleteScope_AfterDeletingAllPackages_Returns204()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "cascade-scope2");
        await SeedScopeAsync(factory, "cascade-scope2", "cascade-scope2");
        SetBearer(client, token);

        // No packages — delete should succeed
        var deleteResp = await client.DeleteAsync("/api/v1/scopes/cascade-scope2");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        Assert.False(await db.ScopeExistsAsync("cascade-scope2"));
    }

    // ── Org: non-empty org cannot be deleted ──────────────────────────────────

    [Fact]
    public async Task DeleteOrg_WithScopes_Returns409_RowsUnchanged()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "cascade-org1");
        SetBearer(client, token);

        // CreateOrg auto-provisions the org scope, so org is non-empty
        var createResp = await client.PostAsync("/api/v1/orgs",
            Json(new { name = "cascade-org-nonempty" }));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var deleteResp = await client.DeleteAsync("/api/v1/orgs/cascade-org-nonempty");
        Assert.Equal(HttpStatusCode.Conflict, deleteResp.StatusCode);
        string body = await deleteResp.Content.ReadAsStringAsync();
        Assert.Contains("OrgNotEmpty", body);

        // Org row must still exist
        var getResp = await client.GetAsync("/api/v1/orgs/cascade-org-nonempty");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
    }

    [Fact]
    public async Task DeleteOrg_AfterDeletingAllChildren_Returns204()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "cascade-org2");
        SetBearer(client, token);

        var createResp = await client.PostAsync("/api/v1/orgs",
            Json(new { name = "cascade-org-empty" }));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        string createBody = await createResp.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createBody);
        _ = createDoc.RootElement.GetProperty("id").GetString()!;

        // Remove the auto-provisioned scope via DB seeding helper
        using (var svc = factory.Services.CreateScope())
        {
            var db = svc.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            await db.DeleteScopeAsync("cascade-org-empty");
        }

        var deleteResp = await client.DeleteAsync("/api/v1/orgs/cascade-org-empty");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    // ── Both deletes: after removing children, parents delete successfully ────

    [Fact]
    public async Task CascadeFlow_ScopeThenOrg_BothSucceed()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "cascade-flow1");
        SetBearer(client, token);

        // Create org (auto-provisions scope)
        var createResp = await client.PostAsync("/api/v1/orgs",
            Json(new { name = "cascade-flow-org" }));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        // Try delete — should fail (scope still exists)
        var del1 = await client.DeleteAsync("/api/v1/orgs/cascade-flow-org");
        Assert.Equal(HttpStatusCode.Conflict, del1.StatusCode);

        // Delete the scope via DB
        using (var svc = factory.Services.CreateScope())
        {
            var db = svc.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            await db.DeleteScopeAsync("cascade-flow-org");
        }

        // Now org delete should succeed
        var del2 = await client.DeleteAsync("/api/v1/orgs/cascade-flow-org");
        Assert.Equal(HttpStatusCode.NoContent, del2.StatusCode);
    }
}
