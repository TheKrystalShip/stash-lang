using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Tests the role-matrix wiring for write endpoints (P3).
/// Verifies that publisher/maintainer/reader roles are live (not hard-coded to "owner").
/// </summary>
public sealed class RegistryAuthzRoleMatrixWriteTests : RegistryAuthzTestBase
{
    [Fact]
    public async Task RoleMatrix_Publisher_CanPublishNewVersion()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-rm");
        string bobToken = await RegisterAndGetTokenAsync(client, "bob-rm");

        await SeedScopeAsync(factory, "alice-rm", "alice-rm");
        await SeedPackageAsync(factory, "@alice-rm/widgets", "alice-rm");
        await SeedPackageRoleAsync(factory, "@alice-rm/widgets", "user", "bob-rm", "publisher");

        SetBearer(client, bobToken);
        byte[] tarball = CreateTarball("@alice-rm/widgets", "2.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-rm/widgets", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task RoleMatrix_Maintainer_CanDeleteVersion()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-rm2");
        string carolToken = await RegisterAndGetTokenAsync(client, "carol-rm2");

        await SeedScopeAsync(factory, "alice-rm2", "alice-rm2");
        await SeedPackageAsync(factory, "@alice-rm2/lib", "alice-rm2", version: "1.0.0");
        await SeedPackageRoleAsync(factory, "@alice-rm2/lib", "user", "carol-rm2", "maintainer");

        // Seed version record
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            await db.AddVersionAsync("@alice-rm2/lib", new VersionRecord
            {
                PackageName = "@alice-rm2/lib",
                Version = "1.0.0",
                Integrity = "sha256-test",
                PublishedAt = DateTime.UtcNow,
                PublishedBy = "alice-rm2"
            });
        }

        SetBearer(client, carolToken);
        var resp = await client.DeleteAsync("/api/v1/packages/alice-rm2/lib/1.0.0");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task RoleMatrix_Maintainer_CanDeprecatePackage()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-rm3");
        string carolToken = await RegisterAndGetTokenAsync(client, "carol-rm3");

        await SeedScopeAsync(factory, "alice-rm3", "alice-rm3");
        await SeedPackageAsync(factory, "@alice-rm3/lib", "alice-rm3");
        await SeedPackageRoleAsync(factory, "@alice-rm3/lib", "user", "carol-rm3", "maintainer");

        SetBearer(client, carolToken);
        var resp = await client.PatchAsync("/api/v1/packages/alice-rm3/lib/deprecate",
            Json(new { message = "use alice-rm3/lib2 instead" }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task RoleMatrix_Reader_CanGetPrivatePackage()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-rm4");
        string danToken = await RegisterAndGetTokenAsync(client, "dan-rm4");

        await SeedScopeAsync(factory, "alice-rm4", "alice-rm4");
        await SeedPackageAsync(factory, "@alice-rm4/private", "alice-rm4", visibility: "private");
        await SeedPackageRoleAsync(factory, "@alice-rm4/private", "user", "dan-rm4", "reader");

        SetBearer(client, danToken);
        var resp = await client.GetAsync("/api/v1/packages/alice-rm4/private");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task RoleMatrix_Publisher_CannotChangeVisibility()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-rm5");
        string bobToken = await RegisterAndGetTokenAsync(client, "bob-rm5");

        await SeedScopeAsync(factory, "alice-rm5", "alice-rm5");
        await SeedPackageAsync(factory, "@alice-rm5/widgets", "alice-rm5");
        await SeedPackageRoleAsync(factory, "@alice-rm5/widgets", "user", "bob-rm5", "publisher");

        SetBearer(client, bobToken);
        var resp = await client.PatchAsync("/api/v1/packages/alice-rm5/widgets/visibility",
            Json(new { visibility = "private" }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("PackageRoleInsufficient", body);
    }

    [Fact]
    public async Task RoleMatrix_Reader_CannotPublishVersion()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-rm6");
        string danToken = await RegisterAndGetTokenAsync(client, "dan-rm6");

        await SeedScopeAsync(factory, "alice-rm6", "alice-rm6");
        await SeedPackageAsync(factory, "@alice-rm6/lib", "alice-rm6");
        await SeedPackageRoleAsync(factory, "@alice-rm6/lib", "user", "dan-rm6", "reader");

        SetBearer(client, danToken);
        byte[] tarball = CreateTarball("@alice-rm6/lib", "2.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-rm6/lib", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("PackageRoleInsufficient", body);
    }

    [Fact]
    public async Task RoleMatrix_UnrelatedUser_PrivatePackage_Gets404NotForbidden()
    {
        // eve has NO role on @alice-rm7/private; she should receive 404 (not 403).
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-rm7");
        string eveToken = await RegisterAndGetTokenAsync(client, "eve-rm7");

        await SeedScopeAsync(factory, "alice-rm7", "alice-rm7");
        await SeedPackageAsync(factory, "@alice-rm7/private", "alice-rm7", visibility: "private");

        SetBearer(client, eveToken);
        var resp = await client.GetAsync("/api/v1/packages/alice-rm7/private");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
