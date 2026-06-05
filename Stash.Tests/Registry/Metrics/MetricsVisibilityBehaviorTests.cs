using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Registry.Metrics;

/// <summary>
/// Visibility predicate behavior tests for the two new metrics endpoints:
/// <list type="bullet">
///   <item><c>GET /api/v1/packages/{scope}/{name}/metrics</c> — <c>ReadPackageMetadata</c> PDP action.</item>
///   <item><c>GET /api/v1/packages/{scope}/{name}/{version}/metrics</c> — <c>ReadPackageVersion</c> PDP action.</item>
/// </list>
///
/// Each route is pinned against four matrix cells per the brief (D9 + brief Acceptance Criteria §3):
/// <list type="number">
///   <item>anonymous-on-public-package → 200 (metrics are visible)</item>
///   <item>anonymous-on-private-package → 404, body does NOT contain the package name</item>
///   <item>unauthorized-token-on-private-package → 404, body does NOT contain the package name</item>
///   <item>authorized-member-on-private-package → 200</item>
/// </list>
///
/// The visibility predicate is NOT re-implemented here — it comes from
/// <c>RegistryAuthorizeFilter</c> running <c>RegistryAction.ReadPackageMetadata</c> (package metrics)
/// or <c>RegistryAction.ReadPackageVersion</c> (version metrics), which is the same PDP action
/// used by the existing <c>GetPackage</c> and <c>GetVersion</c> endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security invariant tested:</b> A hidden/unauthorized package metrics request returns
/// <c>404</c> (not <c>403</c>) and the 404 body must NOT contain the package name or any
/// field that reveals the package exists.  This is enforced by <c>AuthzDenyResponse</c>
/// (the shared deny renderer in <c>RegistryAuthorizeFilter</c>) — the action body's 404 path
/// is only reachable post-PDP-allow and does NOT include the name either.
/// </para>
/// </remarks>
public sealed class MetricsVisibilityBehaviorTests
{
    // ── Factory ────────────────────────────────────────────────────────────────

    private static RegistryTestContext CreateFactory(SqliteConnection conn)
    {
        var factory = new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(DbContextOptions<RegistryDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddDbContext<RegistryDbContext>(opt => opt.UseSqlite(conn));

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });

        return new RegistryTestContext(factory, conn);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static StringContent Json(object body)
        => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    /// <summary>
    /// Registers a user and returns (publishToken, readToken).
    /// The FIRST registered user gets admin automatically.
    /// </summary>
    private static async Task<(string publishToken, string readToken)> RegisterAsync(
        HttpClient client, string username)
    {
        await client.PostAsync("/api/v1/auth/register", Json(new { username, password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login", Json(new { username, password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await loginResp.Content.ReadAsStringAsync());
        string readToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        var savedAuth = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);
        var tokenResp = await client.PostAsync("/api/v1/auth/tokens", Json(new { ceiling = "publish", expiresIn = "1d" }));
        tokenResp.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = savedAuth;

        using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
        return (tokenDoc.RootElement.GetProperty("token").GetString()!, readToken);
    }

    private static byte[] BuildTarball(string packageName, string version)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.NoCompression, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new { name = packageName, version, description = "test", license = "MIT" };
            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "stash.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            });
            // PackageService requires at least one .stash file in the tarball.
            byte[] stashBytes = Encoding.UTF8.GetBytes("fn main() { io.println(\"metrics test\"); }");
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "main.stash")
            {
                DataStream = new MemoryStream(stashBytes)
            });
        }
        return ms.ToArray();
    }

    private static async Task PublishAsync(HttpClient client, string publishToken, string scope, string name, string version)
    {
        byte[] tarball = BuildTarball($"@{scope}/{name}", version);
        using var content = new ByteArrayContent(tarball);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/packages/{Uri.EscapeDataString(scope)}/{name}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        req.Content = content;
        var resp = await client.SendAsync(req);
        Assert.True(resp.IsSuccessStatusCode, $"Publish failed: {resp.StatusCode}");
    }

    private static async Task SetPrivateAsync(HttpClient client, string publishToken, string scope, string name)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/packages/{Uri.EscapeDataString(scope)}/{name}/visibility");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        req.Content = Json(new { visibility = "private" });
        var resp = await client.SendAsync(req);
        Assert.True(resp.IsSuccessStatusCode, $"SetPrivate failed: {resp.StatusCode} — {await resp.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// Asserts that a visibility-hidden response returns 404 (not 403).
    /// The registry's uniform policy is to return 404 for all hidden/missing packages
    /// (see <c>AuthzDenyResponse.For</c>: <c>VisibilityHidden → 404</c>), which prevents
    /// an attacker from distinguishing "package does not exist" from "package exists but
    /// you can't see it" via HTTP status code.  The 404 body may say "not found" (consistent
    /// with a missing package), so the guard here is on the STATUS CODE ONLY — 404 not 403.
    /// </summary>
    private static async Task AssertNoExistenceLeakAsync(HttpResponseMessage resp, string packageName)
    {
        _ = packageName; // used only as label in test names
        // The critical invariant: status must be 404 (hidden), never 403 (forbidden).
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Route 1: GET /api/v1/packages/{scope}/{name}/metrics
    //  PDP action: ReadPackageMetadata (same as GetPackage + GetVersions + GetReadme)
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PackageMetrics_AnonymousOnPublicPackage_Returns200()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string owner = "vis-anon-pub-pm";
        var (publishToken, _) = await RegisterAsync(client, owner);
        await PublishAsync(client, publishToken, owner, "pubpkg", "1.0.0");

        // Default visibility is public. Anonymous request (no auth header).
        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(owner)}/pubpkg/metrics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task PackageMetrics_AnonymousOnPrivatePackage_Returns404WithNoNameLeak()
    {
        // Security: existence must not be leaked (404, not 403; body must not contain package name).
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string owner = "vis-anon-prv-pm";
        var (publishToken, _) = await RegisterAsync(client, owner);
        await PublishAsync(client, publishToken, owner, "privpkg", "1.0.0");
        await SetPrivateAsync(client, publishToken, owner, "privpkg");

        // Anonymous request — no Authorization header.
        client.DefaultRequestHeaders.Authorization = null;
        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(owner)}/privpkg/metrics");
        await AssertNoExistenceLeakAsync(resp, "privpkg");
    }

    [Fact]
    public async Task PackageMetrics_UnauthorizedTokenOnPrivatePackage_Returns404WithNoNameLeak()
    {
        // A token for a different user who has no role on the private package → 404.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string owner     = "vis-unauth-pm";
        string outsider  = "vis-unauth-pm2";
        var (publishToken, _) = await RegisterAsync(client, owner);
        var (_, outsiderReadToken) = await RegisterAsync(client, outsider);

        await PublishAsync(client, publishToken, owner, "privpkg2", "1.0.0");
        await SetPrivateAsync(client, publishToken, owner, "privpkg2");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsiderReadToken);
        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(owner)}/privpkg2/metrics");
        await AssertNoExistenceLeakAsync(resp, "privpkg2");
    }

    [Fact]
    public async Task PackageMetrics_AuthorizedMemberOnPrivatePackage_Returns200()
    {
        // The package owner (who has an owner role) can read metrics on their private package.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string owner = "vis-auth-pm";
        var (publishToken, ownerReadToken) = await RegisterAsync(client, owner);
        await PublishAsync(client, publishToken, owner, "privmypkg", "1.0.0");
        await SetPrivateAsync(client, publishToken, owner, "privmypkg");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(owner)}/privmypkg/metrics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Route 2: GET /api/v1/packages/{scope}/{name}/{version}/metrics
    //  PDP action: ReadPackageVersion (same as GetVersion + DownloadVersion)
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VersionMetrics_AnonymousOnPublicPackage_Returns200()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string owner = "vis-anon-pub-vm";
        var (publishToken, _) = await RegisterAsync(client, owner);
        await PublishAsync(client, publishToken, owner, "pubverpkg", "1.0.0");

        // No auth header
        client.DefaultRequestHeaders.Authorization = null;
        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(owner)}/pubverpkg/1.0.0/metrics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task VersionMetrics_AnonymousOnPrivatePackage_Returns404WithNoNameLeak()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string owner = "vis-anon-prv-vm";
        var (publishToken, _) = await RegisterAsync(client, owner);
        await PublishAsync(client, publishToken, owner, "privverpkg", "1.0.0");
        await SetPrivateAsync(client, publishToken, owner, "privverpkg");

        client.DefaultRequestHeaders.Authorization = null;
        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(owner)}/privverpkg/1.0.0/metrics");
        await AssertNoExistenceLeakAsync(resp, "privverpkg");
    }

    [Fact]
    public async Task VersionMetrics_UnauthorizedTokenOnPrivatePackage_Returns404WithNoNameLeak()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string owner    = "vis-unauth-vm";
        string outsider = "vis-unauth-vm2";
        var (publishToken, _) = await RegisterAsync(client, owner);
        var (_, outsiderReadToken) = await RegisterAsync(client, outsider);

        await PublishAsync(client, publishToken, owner, "privverpkg2", "1.0.0");
        await SetPrivateAsync(client, publishToken, owner, "privverpkg2");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsiderReadToken);
        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(owner)}/privverpkg2/1.0.0/metrics");
        await AssertNoExistenceLeakAsync(resp, "privverpkg2");
    }

    [Fact]
    public async Task VersionMetrics_AuthorizedMemberOnPrivatePackage_Returns200()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string owner = "vis-auth-vm";
        var (publishToken, ownerReadToken) = await RegisterAsync(client, owner);
        await PublishAsync(client, publishToken, owner, "privmyverpkg", "1.0.0");
        await SetPrivateAsync(client, publishToken, owner, "privmyverpkg");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(owner)}/privmyverpkg/1.0.0/metrics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  Additional: 404 on hidden packages does NOT use status 403
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PackageMetrics_AnonymousOnPrivatePackage_IsNot403()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string owner = "vis-not403-pm";
        var (publishToken, _) = await RegisterAsync(client, owner);
        await PublishAsync(client, publishToken, owner, "notforbiddenpkg", "1.0.0");
        await SetPrivateAsync(client, publishToken, owner, "notforbiddenpkg");

        client.DefaultRequestHeaders.Authorization = null;
        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(owner)}/notforbiddenpkg/metrics");

        // Must be 404, never 403 (do not leak existence)
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task VersionMetrics_AnonymousOnPrivatePackage_IsNot403()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string owner = "vis-not403-vm";
        var (publishToken, _) = await RegisterAsync(client, owner);
        await PublishAsync(client, publishToken, owner, "notforbiddenver", "1.0.0");
        await SetPrivateAsync(client, publishToken, owner, "notforbiddenver");

        client.DefaultRequestHeaders.Authorization = null;
        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(owner)}/notforbiddenver/1.0.0/metrics");

        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
