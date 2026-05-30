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
/// Tests for verified-mode scope claim challenge shape and the 501 verify stub.
/// </summary>
public sealed class ScopeClaimVerifiedFlowTests : RegistryAuthzTestBase
{
    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Stash.Registry.Program>
        CreateVerifiedPolicyFactory(SqliteConnection conn)
    {
        return new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(Microsoft.EntityFrameworkCore.DbContextOptions<RegistryDbContext>));
                    if (descriptor != null) services.Remove(descriptor);
                    services.AddDbContext<RegistryDbContext>(options => options.UseSqlite(conn));

                    var configDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(RegistryConfig));
                    if (configDescriptor != null) services.Remove(configDescriptor);
                    var cfg = new RegistryConfig();
                    cfg.Security.ScopeOwnershipPolicy = ScopeOwnershipPolicyKind.Verified;
                    cfg.Auth.RegistrationEnabled = true;
                    services.AddSingleton(cfg);

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    scope.ServiceProvider.GetRequiredService<IRegistryDatabase>().Initialize();
                });
            });
    }

    // ── POST /api/v1/scopes under Verified policy returns 201 with challenge ──

    [Fact]
    public async Task ClaimScope_VerifiedMode_Returns201WithChallenge()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        try
        {
            using var factory = CreateVerifiedPolicyFactory(conn);
            using var client = factory.CreateClient();

            string token = await RegisterAndGetTokenAsync(client, "verified-clm1");
            SetBearer(client, token);

            var resp = await client.PostAsync("/api/v1/scopes",
                Json(new { scope = "acme-verified", owner_type = "user", owner = "verified-clm1",
                           verification_method = "dns-txt" }));

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            string body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.Equal("pending", doc.RootElement.GetProperty("state").GetString());

            var challenge = doc.RootElement.GetProperty("challenge");
            Assert.Equal("dns-txt", challenge.GetProperty("method").GetString());
            Assert.StartsWith("_stash-challenge.", challenge.GetProperty("record_name").GetString());
            Assert.StartsWith("stash-verify=", challenge.GetProperty("record_value").GetString());
            Assert.False(string.IsNullOrEmpty(challenge.GetProperty("expires_at").GetString()));
        }
        finally
        {
            conn.Dispose();
        }
    }

    // ── POST /api/v1/scopes/{scope}/verify returns 501 ────────────────────────

    [Fact]
    public async Task VerifyScope_AlwaysReturns501NotImplemented()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        try
        {
            using var factory = CreateVerifiedPolicyFactory(conn);
            using var client = factory.CreateClient();

            string token = await RegisterAndGetTokenAsync(client, "verified-clm2");
            SetBearer(client, token);

            // Claim the scope first so it exists
            var claimResp = await client.PostAsync("/api/v1/scopes",
                Json(new { scope = "verify-stub", owner_type = "user", owner = "verified-clm2",
                           verification_method = "dns-txt" }));
            Assert.Equal(HttpStatusCode.Created, claimResp.StatusCode);

            // Verify endpoint should return 501
            var verifyResp = await client.PostAsync("/api/v1/scopes/verify-stub/verify",
                Json(new { }));

            Assert.Equal(HttpStatusCode.NotImplemented, verifyResp.StatusCode);
            string body = await verifyResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("verify-stub", doc.RootElement.GetProperty("scope").GetString());
            Assert.Equal("dns-txt", doc.RootElement.GetProperty("method").GetString());
        }
        finally
        {
            conn.Dispose();
        }
    }

    // ── Pending scope is treated as unowned for CreatePackage ──────────────────

    [Fact]
    public async Task ScopeOwnershipPolicy_Verified_PendingScope_DeniesCreatePackage()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        try
        {
            using var factory = CreateVerifiedPolicyFactory(conn);
            using var client = factory.CreateClient();

            string token = await RegisterAndGetTokenAsync(client, "verified-pending1");
            SetBearer(client, token);

            // Claim creates a pending scope
            var claimResp = await client.PostAsync("/api/v1/scopes",
                Json(new { scope = "pending-corp", owner_type = "user", owner = "verified-pending1",
                           verification_method = "dns-txt" }));
            Assert.Equal(HttpStatusCode.Created, claimResp.StatusCode);

            // Publishing into a pending scope should be denied
            byte[] tarball = CreateTarball("@pending-corp/lib", "1.0.0");
            var pubResp = await client.PutAsync("/api/v1/packages/pending-corp/lib", TarballContent(tarball));

            Assert.Equal(HttpStatusCode.Forbidden, pubResp.StatusCode);
            string body = await pubResp.Content.ReadAsStringAsync();
            Assert.Contains("ScopeNotOwned", body);
            Assert.Contains("verify", body);
            Assert.False(await PackageExistsAsync(factory, "@pending-corp/lib"));
        }
        finally
        {
            conn.Dispose();
        }
    }

    // ── GET /scopes/{scope} reflects state=pending for pending scopes ──────────

    [Fact]
    public async Task GetScope_VerifiedMode_PendingScopeShowsPendingState()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        try
        {
            using var factory = CreateVerifiedPolicyFactory(conn);
            using var client = factory.CreateClient();

            string token = await RegisterAndGetTokenAsync(client, "verified-get1");
            SetBearer(client, token);

            await client.PostAsync("/api/v1/scopes",
                Json(new { scope = "get-pending-corp", owner_type = "user", owner = "verified-get1",
                           verification_method = "dns-txt" }));

            var resp = await client.GetAsync("/api/v1/scopes/get-pending-corp");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            string body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("pending", doc.RootElement.GetProperty("state").GetString());
        }
        finally
        {
            conn.Dispose();
        }
    }
}
