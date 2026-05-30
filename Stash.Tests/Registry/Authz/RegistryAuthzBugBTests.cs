using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Regression tests for Bug B: controller authorizes the route name, service writes the manifest name.
/// </summary>
public sealed class RegistryAuthzBugBTests : RegistryAuthzTestBase
{
    [Fact]
    public async Task BugB_ManifestNameMismatch_Returns400ManifestRouteMismatch()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-bb");
        string malloryToken = await RegisterAndGetTokenAsync(client, "mallory-bb");

        await SeedScopeAsync(factory, "alice-bb", "alice-bb");
        await SeedScopeAsync(factory, "mallory-bb", "mallory-bb");
        await SeedPackageAsync(factory, "@alice-bb/lib", "alice-bb");

        SetBearer(client, malloryToken);

        // Mallory PUTs to her own route but ships manifest naming @alice-bb/lib
        byte[] maliciousTarball = CreateTarball("@alice-bb/lib", "9.9.9");
        var resp = await client.PutAsync("/api/v1/packages/mallory-bb/harmless", TarballContent(maliciousTarball));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ManifestRouteMismatch", body);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Stash.Registry.Database.IRegistryDatabase>();
        Assert.False(await db.VersionExistsAsync("@alice-bb/lib", "9.9.9"),
            "@alice-bb/lib@9.9.9 must NOT be created.");
    }

    [Fact]
    public async Task BugB_ManifestNameMatchesRoute_Succeeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice2-bb");
        await SeedScopeAsync(factory, "alice2-bb", "alice2-bb");
        SetBearer(client, token);

        byte[] tarball = CreateTarball("@alice2-bb/widget", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice2-bb/widget", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task BugB_VersionMismatch_Returns400ManifestRouteMismatch()
    {
        // Q5: manifest version must match X-Package-Version header when supplied.
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice3-bb");
        await SeedScopeAsync(factory, "alice3-bb", "alice3-bb");
        SetBearer(client, token);

        // Manifest says 1.0.0 but caller signals they want 2.0.0
        byte[] tarball = CreateTarball("@alice3-bb/pkg", "1.0.0");
        var content = TarballContent(tarball);
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/packages/alice3-bb/pkg")
        {
            Content = content
        };
        request.Headers.Add("X-Package-Version", "2.0.0");
        var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ManifestRouteMismatch", body);
        Assert.Contains("version", body.ToLowerInvariant());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Stash.Registry.Database.IRegistryDatabase>();
        Assert.False(await db.VersionExistsAsync("@alice3-bb/pkg", "1.0.0"));
        Assert.False(await db.VersionExistsAsync("@alice3-bb/pkg", "2.0.0"));
    }

    [Fact]
    public async Task BugB_VersionMatchesHeader_Succeeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice4-bb");
        await SeedScopeAsync(factory, "alice4-bb", "alice4-bb");
        SetBearer(client, token);

        byte[] tarball = CreateTarball("@alice4-bb/pkg", "1.2.3");
        var content = TarballContent(tarball);
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/packages/alice4-bb/pkg")
        {
            Content = content
        };
        request.Headers.Add("X-Package-Version", "1.2.3");
        var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task BugB_NoXPackageVersionHeader_ManifestVersionIsAccepted()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice5-bb");
        await SeedScopeAsync(factory, "alice5-bb", "alice5-bb");
        SetBearer(client, token);

        byte[] tarball = CreateTarball("@alice5-bb/pkg", "3.0.0-beta.1");
        var resp = await client.PutAsync("/api/v1/packages/alice5-bb/pkg", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }
}
