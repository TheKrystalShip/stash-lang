using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Regression tests for Bug A: no scope-ownership check on new-package publish.
/// </summary>
public sealed class RegistryAuthzBugATests : RegistryAuthzTestBase
{
    [Fact]
    public async Task BugA_PublishIntoOwnedScope_Succeeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-ba");
        await SeedScopeAsync(factory, "alice-ba", "alice-ba");
        SetBearer(client, token);

        byte[] tarball = CreateTarball("@alice-ba/widget", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-ba/widget", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.True(await PackageExistsAsync(factory, "@alice-ba/widget"));
    }

    [Fact]
    public async Task BugA_PublishIntoUnownedScope_Claim_Returns403ScopeNotOwned()
    {
        // Under claim policy (default), publishing into unowned scope returns 403.
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "mallory-ba");
        SetBearer(client, token);

        // "unowned-corp" scope does not exist in DB
        byte[] tarball = CreateTarball("@unowned-corp/evil", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/unowned-corp/evil", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ScopeNotOwned", body);
        Assert.False(await PackageExistsAsync(factory, "@unowned-corp/evil"),
            "Package must NOT be created on denied publish.");
    }

    [Fact]
    public async Task BugA_PublishIntoScopeOwnedByAnotherUser_Returns403ScopeNotOwned()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice2-ba");
        string malloryToken = await RegisterAndGetTokenAsync(client, "mallory2-ba");
        await SeedScopeAsync(factory, "bigcorp", "alice2-ba");
        SetBearer(client, malloryToken);

        byte[] tarball = CreateTarball("@bigcorp/utils", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/bigcorp/utils", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ScopeNotOwned", body);
        Assert.False(await PackageExistsAsync(factory, "@bigcorp/utils"));
    }

    [Fact]
    public async Task BugA_PublishIntoReservedScopeStash_Returns403ScopeReserved()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "anyone-ba");
        SetBearer(client, token);

        byte[] tarball = CreateTarball("@stash/anything", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/stash/anything", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ScopeReserved", body);
    }

    [Fact]
    public async Task BugA_PublishIntoReservedScopeAdmin_Returns403ScopeReserved()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "anyone2-ba");
        SetBearer(client, token);

        byte[] tarball = CreateTarball("@admin/anything", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/admin/anything", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ScopeReserved", body);
    }

    [Fact]
    public async Task BugA_AdminRoleUser_PublishIntoReservedScopeStash_Returns403ScopeReserved()
    {
        // Even a registry admin cannot publish into @stash — reserved scopes are absolute.
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        // First registered user gets admin role
        string adminToken = await RegisterAndGetTokenAsync(client, "admin-ba");
        await SeedScopeAsync(factory, "admin-ba", "admin-ba");
        SetBearer(client, adminToken);

        byte[] tarball = CreateTarball("@stash/noway", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/stash/noway", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ScopeReserved", body);
    }

    [Fact]
    public async Task BugA_PublishNewVersionIntoExistingOwnedPackage_Succeeds()
    {
        // PublishVersion (package exists) is gated on package role, not scope ownership.
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice3-ba");
        await SeedScopeAsync(factory, "alice3-ba", "alice3-ba");
        SetBearer(client, token);

        byte[] tarball1 = CreateTarball("@alice3-ba/lib", "1.0.0");
        var resp1 = await client.PutAsync("/api/v1/packages/alice3-ba/lib", TarballContent(tarball1));
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);

        byte[] tarball2 = CreateTarball("@alice3-ba/lib", "2.0.0");
        var resp2 = await client.PutAsync("/api/v1/packages/alice3-ba/lib", TarballContent(tarball2));
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
    }
}
