using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Tests for audit logging of authorized mutations and authenticated denials (D19 / P3).
/// </summary>
public sealed class RegistryAuthzAuditMutationTests : RegistryAuthzTestBase
{
    [Fact]
    public async Task Audit_CreatePackage_EmitsExactlyOneAllowEntry()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-am");
        await SeedScopeAsync(factory, "alice-am", "alice-am");
        SetBearer(client, token);

        byte[] tarball = CreateTarball("@alice-am/widget", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-am/widget", TarballContent(tarball));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var entries = await GetAuditEntriesAsync(factory);
        var packageEntries = entries
            .Where(e => e.Package == "@alice-am/widget" && e.Decision == "allow")
            .ToList();

        Assert.Single(packageEntries, e =>
            (e.Action == "package.create" || e.Action == "package.publish") &&
            e.User == "alice-am");
    }

    [Fact]
    public async Task Audit_PublishVersion_EmitsExactlyOneAllowEntry()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "alice-am2");
        await SeedScopeAsync(factory, "alice-am2", "alice-am2");
        await SeedPackageAsync(factory, "@alice-am2/pkg", "alice-am2");
        SetBearer(client, token);

        byte[] tarball = CreateTarball("@alice-am2/pkg", "2.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-am2/pkg", TarballContent(tarball));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var entries = await GetAuditEntriesAsync(factory);
        var publishEntries = entries
            .Where(e => e.Package == "@alice-am2/pkg" && e.Action == "package.publish" && e.Decision == "allow")
            .ToList();

        Assert.Single(publishEntries);
    }

    [Fact]
    public async Task Audit_AssignPackageRole_EmitsExactlyOneAllowEntry()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-am3");
        await RegisterAndGetTokenAsync(client, "bob-am3");
        await SeedScopeAsync(factory, "alice-am3", "alice-am3");
        await SeedPackageAsync(factory, "@alice-am3/pkg", "alice-am3");
        SetBearer(client, aliceToken);

        var resp = await client.PutAsync("/api/v1/packages/alice-am3/pkg/roles",
            Json(new { principal_type = "user", principal_id = "bob-am3", role = "publisher" }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var entries = await GetAuditEntriesAsync(factory);
        var roleEntries = entries
            .Where(e => e.Package == "@alice-am3/pkg" && e.Action == "role.assign" && e.Decision == "allow")
            .ToList();

        Assert.Single(roleEntries);
        Assert.Equal("alice-am3", roleEntries[0].User);
        Assert.Equal("bob-am3", roleEntries[0].Target);
    }

    [Fact]
    public async Task Audit_RevokePackageRole_EmitsExactlyOneAllowEntry()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndGetTokenAsync(client, "alice-am4");
        await RegisterAndGetTokenAsync(client, "bob-am4");
        await SeedScopeAsync(factory, "alice-am4", "alice-am4");
        await SeedPackageAsync(factory, "@alice-am4/pkg", "alice-am4");
        await SeedPackageRoleAsync(factory, "@alice-am4/pkg", "user", "bob-am4", "publisher");

        SetBearer(client, aliceToken);
        var deleteReq = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/packages/alice-am4/pkg/roles")
        {
            Content = new StringContent(JsonSerializer.Serialize(
                new { principal_type = "user", principal_id = "bob-am4" }),
                Encoding.UTF8, "application/json")
        };
        var resp = await client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var entries = await GetAuditEntriesAsync(factory);
        var revokeEntries = entries
            .Where(e => e.Package == "@alice-am4/pkg" && e.Action == "role.revoke" && e.Decision == "allow")
            .ToList();

        Assert.Single(revokeEntries);
    }

    [Fact]
    public async Task Audit_AuthenticatedDeny_EmitsExactlyOneDenyEntry()
    {
        // bob has no role on @alice-am5/pkg; his PUT gets 403 and emits one deny entry.
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-am5");
        string bobToken = await RegisterAndGetTokenAsync(client, "bob-am5");
        await SeedScopeAsync(factory, "alice-am5", "alice-am5");
        await SeedPackageAsync(factory, "@alice-am5/pkg", "alice-am5");

        SetBearer(client, bobToken);
        byte[] tarball = CreateTarball("@alice-am5/pkg", "9.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-am5/pkg", TarballContent(tarball));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        var entries = await GetAuditEntriesAsync(factory);
        var denyEntries = entries
            .Where(e => e.Package == "@alice-am5/pkg" && e.Decision == "deny" && e.User == "bob-am5")
            .ToList();

        Assert.Single(denyEntries);
        Assert.Equal("PackageRoleInsufficient", denyEntries[0].DenyReason);
    }

    [Fact]
    public async Task Audit_AnonymousDenyOnPrivatePackage_ZeroAuditEntries()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-am6");
        await SeedScopeAsync(factory, "alice-am6", "alice-am6");
        await SeedPackageAsync(factory, "@alice-am6/private", "alice-am6", visibility: "private");

        // Anonymous GET (no bearer token)
        using var anonClient = factory.CreateClient();
        var resp = await anonClient.GetAsync("/api/v1/packages/alice-am6/private");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        // Zero audit entries for this package
        var entries = await GetAuditEntriesAsync(factory);
        var pkgEntries = entries
            .Where(e => e.Package == "@alice-am6/private")
            .ToList();

        Assert.Empty(pkgEntries);
    }
}
