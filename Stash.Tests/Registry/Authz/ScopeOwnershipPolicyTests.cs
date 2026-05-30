using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Xunit;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Tests for <see cref="ScopeOwnershipPolicyKind"/> wiring into <c>SecurityConfig</c>
/// and PDP <c>CreatePackage</c> behavior under Open / Claim / Verified policies.
/// </summary>
public sealed class ScopeOwnershipPolicyTests : RegistryAuthzTestBase
{
    // ── SecurityConfig deserialization ────────────────────────────────────────

    [Fact]
    public void SecurityConfig_DefaultScopeOwnershipPolicy_IsClaim()
    {
        var cfg = new SecurityConfig();
        Assert.Equal(ScopeOwnershipPolicyKind.Claim, cfg.ScopeOwnershipPolicy);
    }

    // ── Claim policy (default) ─────────────────────────────────────────────────

    [Fact]
    public async Task ScopeOwnershipPolicy_Claim_UnownedScope_Returns403ScopeNotOwned()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "claim-user1");
        SetBearer(client, token);

        // "unowned-corp" scope does not exist — under Claim policy: deny
        byte[] tarball = CreateTarball("@unowned-corp/lib", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/unowned-corp/lib", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ScopeNotOwned", body);
        Assert.Contains("claim", body);
        Assert.False(await PackageExistsAsync(factory, "@unowned-corp/lib"));
    }

    [Fact]
    public async Task ScopeOwnershipPolicy_Claim_OwnedScope_Succeeds()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        string token = await RegisterAndGetTokenAsync(client, "claim-user2");
        await SeedScopeAsync(factory, "claim-user2", "claim-user2");
        SetBearer(client, token);

        byte[] tarball = CreateTarball("@claim-user2/lib", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/claim-user2/lib", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.True(await PackageExistsAsync(factory, "@claim-user2/lib"));
    }

    [Fact]
    public async Task ScopeOwnershipPolicy_Claim_ScopeOwnedByOther_Returns403()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        var factory = ctx.Factory;
        using var client = factory.CreateClient();

        await RegisterAndGetTokenAsync(client, "alice-clm");
        string malloryToken = await RegisterAndGetTokenAsync(client, "mallory-clm");
        await SeedScopeAsync(factory, "alice-clm", "alice-clm");
        SetBearer(client, malloryToken);

        byte[] tarball = CreateTarball("@alice-clm/lib", "1.0.0");
        var resp = await client.PutAsync("/api/v1/packages/alice-clm/lib", TarballContent(tarball));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Contains("ScopeNotOwned", await resp.Content.ReadAsStringAsync());
    }

    // ── Open policy ────────────────────────────────────────────────────────────

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Stash.Registry.Program>
        CreateOpenPolicyFactory(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        return new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    // Replace DbContext
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(Microsoft.EntityFrameworkCore.DbContextOptions<Stash.Registry.Database.RegistryDbContext>));
                    if (descriptor != null) services.Remove(descriptor);
                    services.AddDbContext<Stash.Registry.Database.RegistryDbContext>(options =>
                        options.UseSqlite(conn));

                    // Override RegistryConfig singleton to use Open policy
                    var configDescriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(RegistryConfig));
                    if (configDescriptor != null) services.Remove(configDescriptor);
                    var cfg = new RegistryConfig();
                    cfg.Security.ScopeOwnershipPolicy = ScopeOwnershipPolicyKind.Open;
                    cfg.Auth.RegistrationEnabled = true;
                    services.AddSingleton(cfg);

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });
    }

    [Fact]
    public async Task ScopeOwnershipPolicy_Open_UnownedScope_AutoClaimsAndSucceeds()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        try
        {
            using var factory = CreateOpenPolicyFactory(conn);
            using var client = factory.CreateClient();

            string token = await RegisterAndGetTokenAsync(client, "open-user1");
            SetBearer(client, token);

            byte[] tarball = CreateTarball("@fresh-scope/lib", "1.0.0");
            var resp = await client.PutAsync("/api/v1/packages/fresh-scope/lib", TarballContent(tarball));

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            Assert.True(await PackageExistsAsync(factory, "@fresh-scope/lib"));

            // Verify scope was auto-claimed
            var scopeResp = await client.GetAsync("/api/v1/scopes/fresh-scope");
            Assert.Equal(HttpStatusCode.OK, scopeResp.StatusCode);
            string scopeBody = await scopeResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(scopeBody);
            Assert.Equal("open-user1", doc.RootElement.GetProperty("owner").GetString());
        }
        finally
        {
            conn.Dispose();
        }
    }

    [Fact]
    public async Task ScopeOwnershipPolicy_Open_ScopeOwnedByOther_Returns403()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        try
        {
            using var factory = CreateOpenPolicyFactory(conn);
            using var client = factory.CreateClient();

            await RegisterAndGetTokenAsync(client, "alice-opn");
            string malloryToken = await RegisterAndGetTokenAsync(client, "mallory-opn");

            // Alice claims the scope directly
            await SeedScopeAsync(factory, "alice-corp", "alice-opn");
            SetBearer(client, malloryToken);

            byte[] tarball = CreateTarball("@alice-corp/lib", "1.0.0");
            var resp = await client.PutAsync("/api/v1/packages/alice-corp/lib", TarballContent(tarball));

            // Someone else owns it — always denied regardless of policy
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.Contains("ScopeNotOwned", await resp.Content.ReadAsStringAsync());
        }
        finally
        {
            conn.Dispose();
        }
    }

    // ── Verified policy ────────────────────────────────────────────────────────

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Stash.Registry.Program>
        CreateVerifiedPolicyFactory(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        return new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(Microsoft.EntityFrameworkCore.DbContextOptions<Stash.Registry.Database.RegistryDbContext>));
                    if (descriptor != null) services.Remove(descriptor);
                    services.AddDbContext<Stash.Registry.Database.RegistryDbContext>(options =>
                        options.UseSqlite(conn));

                    var configDescriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(RegistryConfig));
                    if (configDescriptor != null) services.Remove(configDescriptor);
                    var cfg = new RegistryConfig();
                    cfg.Security.ScopeOwnershipPolicy = ScopeOwnershipPolicyKind.Verified;
                    cfg.Auth.RegistrationEnabled = true;
                    services.AddSingleton(cfg);

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });
    }

    [Fact]
    public async Task ScopeOwnershipPolicy_Verified_UnownedScope_Returns403WithVerifyMessage()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        conn.Open();
        try
        {
            using var factory = CreateVerifiedPolicyFactory(conn);
            using var client = factory.CreateClient();

            string token = await RegisterAndGetTokenAsync(client, "verified-user1");
            SetBearer(client, token);

            byte[] tarball = CreateTarball("@verify-corp/lib", "1.0.0");
            var resp = await client.PutAsync("/api/v1/packages/verify-corp/lib", TarballContent(tarball));

            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            string body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("ScopeNotOwned", body);
            Assert.Contains("verify", body);
            Assert.False(await PackageExistsAsync(factory, "@verify-corp/lib"));
        }
        finally
        {
            conn.Dispose();
        }
    }
}
