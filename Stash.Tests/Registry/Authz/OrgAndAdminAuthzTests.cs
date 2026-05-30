using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Tests for org and admin endpoint authorization via the PDP
/// (OrganizationsController and AdminController migration).
/// </summary>
public sealed class OrgAndAdminAuthzTests : RegistryAuthzTestBase
{
    // ── OrganizationsController: AddMember requires org owner ────────────────

    [Fact]
    public async Task AddOrgMember_NonOwner_Returns403()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-org1");
        string bobToken = await RegisterAndGetTokenAsync(client, "bob-org1");

        // Alice creates the org
        SetBearer(client, aliceToken);
        var createResp = await client.PostAsync("/api/v1/orgs",
            Json(new { name = "test-org-member1" }));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        // Bob (not an owner) tries to add a member
        SetBearer(client, bobToken);
        var addResp = await client.PostAsync("/api/v1/orgs/test-org-member1/members",
            Json(new { username = "alice-org1", org_role = "member" }));

        Assert.Equal(HttpStatusCode.Forbidden, addResp.StatusCode);
    }

    [Fact]
    public async Task AddOrgMember_Owner_Succeeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-org2");
        await RegisterAndGetTokenAsync(client, "bob-org2");

        SetBearer(client, aliceToken);
        var createResp = await client.PostAsync("/api/v1/orgs",
            Json(new { name = "test-org-owner1" }));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var addResp = await client.PostAsync("/api/v1/orgs/test-org-owner1/members",
            Json(new { username = "bob-org2", org_role = "member" }));
        Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
    }

    // ── OrganizationsController: CreateTeam requires org owner ───────────────

    [Fact]
    public async Task CreateTeam_NonOwner_Returns403()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-team1");
        string bobToken = await RegisterAndGetTokenAsync(client, "bob-team1");

        SetBearer(client, aliceToken);
        await client.PostAsync("/api/v1/orgs", Json(new { name = "org-for-team1" }));

        // Bob is not owner of the org
        SetBearer(client, bobToken);
        var resp = await client.PostAsync("/api/v1/orgs/org-for-team1/teams",
            Json(new { name = "devs" }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── OrganizationsController: DELETE /orgs/{org} cascade refusal ──────────

    [Fact]
    public async Task DeleteOrg_OrgWithScopes_Returns409OrgNotEmpty()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-delorgs");
        SetBearer(client, aliceToken);

        // Create an org (auto-provisions the @org-name scope)
        var createResp = await client.PostAsync("/api/v1/orgs",
            Json(new { name = "del-org-nonempty" }));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        // Attempt to delete while the org's auto-scope still exists
        var deleteResp = await client.DeleteAsync("/api/v1/orgs/del-org-nonempty");
        // The org still owns the auto-provisioned scope → 409
        Assert.Equal(HttpStatusCode.Conflict, deleteResp.StatusCode);
        string body = await deleteResp.Content.ReadAsStringAsync();
        Assert.Contains("OrgNotEmpty", body);
    }

    [Fact]
    public async Task DeleteOrg_EmptyOrg_Returns204()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-delorgempty");
        SetBearer(client, aliceToken);

        var createResp = await client.PostAsync("/api/v1/orgs",
            Json(new { name = "del-org-empty" }));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        string createBody = await createResp.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createBody);
        string orgId = createDoc.RootElement.GetProperty("id").GetString()!;

        // Manually remove the auto-provisioned scope
        await SeedScopeDeleteAsync(factory, "del-org-empty");

        var deleteResp = await client.DeleteAsync("/api/v1/orgs/del-org-empty");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    // ── ScopesController: DELETE /scopes/{scope} cascade refusal ─────────────

    [Fact]
    public async Task DeleteScope_ScopeWithPackages_Returns409ScopeNotEmpty()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-delscope");
        await SeedScopeAsync(factory, "alice-delscope", "alice-delscope");
        await SeedPackageAsync(factory, "@alice-delscope/lib", "alice-delscope");
        SetBearer(client, token);

        var deleteResp = await client.DeleteAsync("/api/v1/scopes/alice-delscope");
        Assert.Equal(HttpStatusCode.Conflict, deleteResp.StatusCode);
        string body = await deleteResp.Content.ReadAsStringAsync();
        Assert.Contains("ScopeNotEmpty", body);
    }

    [Fact]
    public async Task DeleteScope_EmptyScope_Returns204()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-delscopeempty");
        await SeedScopeAsync(factory, "empty-scope-del", "alice-delscopeempty");
        SetBearer(client, token);

        var deleteResp = await client.DeleteAsync("/api/v1/scopes/empty-scope-del");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    [Fact]
    public async Task DeleteScope_NonOwner_Returns403()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-delperm");
        string bobToken = await RegisterAndGetTokenAsync(client, "bob-delperm");
        await SeedScopeAsync(factory, "alice-scope-perm", "alice-delperm");
        SetBearer(client, bobToken);

        var deleteResp = await client.DeleteAsync("/api/v1/scopes/alice-scope-perm");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResp.StatusCode);
    }

    // ── AdminController: admin endpoint requires admin ceiling ───────────────

    [Fact]
    public async Task GetAdminStats_WithAdminToken_Returns200()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-stats-user");
        SetBearer(client, adminToken);

        var resp = await client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetAdminStats_WithPublishToken_Returns403()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string publishToken = await RegisterAndGetTokenAsync(client, "admin-stats-pub");
        SetBearer(client, publishToken);

        var resp = await client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Search: private packages hidden for anonymous callers ────────────────

    [Fact]
    public async Task Search_Anonymous_PrivatePackageNotReturned()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-srch");
        await SeedScopeAsync(factory, "alice-srch", "alice-srch");
        await SeedPackageAsync(factory, "@alice-srch/secret", "alice-srch", visibility: "private");

        // Anonymous search — no auth header
        client.DefaultRequestHeaders.Authorization = null;
        var resp = await client.GetAsync("/api/v1/search?q=secret");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var packages = doc.RootElement.GetProperty("packages");
        Assert.Equal(0, packages.GetArrayLength());
    }

    [Fact]
    public async Task Search_Anonymous_PublicPackageReturned()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-srchpub");
        await SeedScopeAsync(factory, "alice-srchpub", "alice-srchpub");
        await SeedPackageAsync(factory, "@alice-srchpub/public-lib", "alice-srchpub", visibility: "public");

        client.DefaultRequestHeaders.Authorization = null;
        var resp = await client.GetAsync("/api/v1/search?q=public-lib");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var packages = doc.RootElement.GetProperty("packages");
        Assert.True(packages.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Search_AuthenticatedOwner_PrivatePackageReturned()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-srchauth");
        await SeedScopeAsync(factory, "alice-srchauth", "alice-srchauth");
        await SeedPackageAsync(factory, "@alice-srchauth/priv-lib", "alice-srchauth", visibility: "private");

        SetBearer(client, token);
        var resp = await client.GetAsync("/api/v1/search?q=priv-lib");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var packages = doc.RootElement.GetProperty("packages");
        Assert.True(packages.GetArrayLength() >= 1);
    }

    // ── Helper to delete a scope directly ────────────────────────────────────

    private static async System.Threading.Tasks.Task SeedScopeDeleteAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Stash.Registry.Program> factory,
        string scopeName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Stash.Registry.Database.IRegistryDatabase>();
        await db.DeleteScopeAsync(scopeName);
    }
}
