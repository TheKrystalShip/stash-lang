using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services.Metrics;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Registry.Metrics;

/// <summary>
/// Integration tests for <c>GET /api/v1/admin/metrics/downloads</c>
/// (AdminController.GetDownloadsMetrics).
///
/// Covers:
/// <list type="bullet">
///   <item>Packages ordered descending by download count over the window.</item>
///   <item>Pagination (page/pageSize respected, totalCount correct).</item>
///   <item>windowDays parameter filters correctly.</item>
///   <item>Requires admin ceiling — non-admin returns 403.</item>
/// </list>
/// </summary>
public sealed class AdminTopPackagesEndpointTests
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

    /// <summary>Registers user, returns (publishToken, adminToken). First-registered user gets admin role.</summary>
    private static async Task<(string publishToken, string adminToken)> RegisterAndGetTokensAsync(
        HttpClient client, string username)
    {
        await client.PostAsync("/api/v1/auth/register", Json(new { username, password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login", Json(new { username, password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await loginResp.Content.ReadAsStringAsync());
        string readToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        var savedAuth = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);

        var publishResp = await client.PostAsync("/api/v1/auth/tokens", Json(new { ceiling = "publish", expiresIn = "1d" }));
        publishResp.EnsureSuccessStatusCode();
        using var publishDoc = JsonDocument.Parse(await publishResp.Content.ReadAsStringAsync());
        string publishToken = publishDoc.RootElement.GetProperty("token").GetString()!;

        var adminResp = await client.PostAsync("/api/v1/auth/tokens", Json(new { ceiling = "admin", expiresIn = "1d" }));
        adminResp.EnsureSuccessStatusCode();
        using var adminDoc = JsonDocument.Parse(await adminResp.Content.ReadAsStringAsync());
        string adminToken = adminDoc.RootElement.GetProperty("token").GetString()!;

        client.DefaultRequestHeaders.Authorization = savedAuth;
        return (publishToken, adminToken);
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
        Assert.True(resp.IsSuccessStatusCode, $"Publish failed: {resp.StatusCode} — {await resp.Content.ReadAsStringAsync()}");
    }

    private static async Task SeedHourlyRollupAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        string packageName, string version, DateTime bucketStart, long downloads)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
        db.DownloadRollupHourly.Add(new DownloadRollupHourlyRecord
        {
            PackageName = packageName,
            Version     = version,
            BucketStart = bucketStart,
            Downloads   = downloads,
            BytesServed = downloads * 512,
        });
        await db.SaveChangesAsync();
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDownloadsMetrics_TopPackagesSortedDescending_FooBeforeBar()
    {
        // Acceptance criterion 5: @a/foo outranks @b/bar.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        // First registered = admin
        string adminUser = "atp-admin";
        var (publishToken, adminToken) = await RegisterAndGetTokensAsync(client, adminUser);

        await PublishAsync(client, publishToken, adminUser, "foo", "1.0.0");
        await PublishAsync(client, publishToken, adminUser, "bar", "1.0.0");

        // Seed: foo = 10 downloads, bar = 3 downloads (within last 7 days)
        var threeHoursAgo = DateTime.UtcNow.AddHours(-3);
        var bucket = new DateTime(threeHoursAgo.Year, threeHoursAgo.Month, threeHoursAgo.Day, threeHoursAgo.Hour, 0, 0, DateTimeKind.Utc);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{adminUser}/foo", "1.0.0", bucket, 10);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{adminUser}/bar", "1.0.0", bucket, 3);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var resp = await client.GetAsync("/api/v1/admin/metrics/downloads?page=1&pageSize=10&windowDays=7");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var items = root.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 2, $"Expected at least 2 items, body: {body}");

        string firstPkg  = items[0].GetProperty("package").GetString()!;
        string secondPkg = items[1].GetProperty("package").GetString()!;

        Assert.Equal($"@{adminUser}/foo", firstPkg);
        Assert.Equal($"@{adminUser}/bar", secondPkg);

        long fooDownloads = items[0].GetProperty("downloads").GetInt64();
        Assert.Equal(10, fooDownloads);
        Assert.Equal(7, items[0].GetProperty("windowDays").GetInt32());
    }

    [Fact]
    public async Task GetDownloadsMetrics_Pagination_RespectsPageAndPageSize()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string adminUser = "atp-pag";
        var (publishToken, adminToken) = await RegisterAndGetTokensAsync(client, adminUser);

        // Publish 3 packages and seed different download counts
        await PublishAsync(client, publishToken, adminUser, "pkga", "1.0.0");
        await PublishAsync(client, publishToken, adminUser, "pkgb", "1.0.0");
        await PublishAsync(client, publishToken, adminUser, "pkgc", "1.0.0");

        var bucket = new DateTime(DateTime.UtcNow.AddHours(-2).Ticks, DateTimeKind.Utc);
        bucket = new DateTime(bucket.Year, bucket.Month, bucket.Day, bucket.Hour, 0, 0, DateTimeKind.Utc);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{adminUser}/pkga", "1.0.0", bucket, 30);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{adminUser}/pkgb", "1.0.0", bucket, 20);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{adminUser}/pkgc", "1.0.0", bucket, 10);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Page 1, pageSize 1 → first item (pkga with 30)
        var resp1 = await client.GetAsync("/api/v1/admin/metrics/downloads?page=1&pageSize=1&windowDays=7");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        using var doc1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
        Assert.Equal(3, doc1.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, doc1.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal($"@{adminUser}/pkga", doc1.RootElement.GetProperty("items")[0].GetProperty("package").GetString());

        // Page 2, pageSize 1 → second item (pkgb with 20)
        var resp2 = await client.GetAsync("/api/v1/admin/metrics/downloads?page=2&pageSize=1&windowDays=7");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        using var doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());
        Assert.Equal($"@{adminUser}/pkgb", doc2.RootElement.GetProperty("items")[0].GetProperty("package").GetString());
    }

    [Fact]
    public async Task GetDownloadsMetrics_WindowDaysFiltering_ExcludesOldDownloads()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string adminUser = "atp-window";
        var (publishToken, adminToken) = await RegisterAndGetTokensAsync(client, adminUser);

        await PublishAsync(client, publishToken, adminUser, "windowed", "1.0.0");

        // Seed 5 downloads 6 days ago (within windowDays=7) and 10 downloads 10 days ago (outside).
        var sixDaysAgo = DateTime.UtcNow.AddDays(-6);
        var tenDaysAgo = DateTime.UtcNow.AddDays(-10);
        var bucket6 = new DateTime(sixDaysAgo.Year, sixDaysAgo.Month, sixDaysAgo.Day, sixDaysAgo.Hour, 0, 0, DateTimeKind.Utc);
        var bucket10 = new DateTime(tenDaysAgo.Year, tenDaysAgo.Month, tenDaysAgo.Day, tenDaysAgo.Hour, 0, 0, DateTimeKind.Utc);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{adminUser}/windowed", "1.0.0", bucket6, 5);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{adminUser}/windowed", "1.0.0", bucket10, 10);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // windowDays=7 → only the 5 recent downloads
        var resp = await client.GetAsync("/api/v1/admin/metrics/downloads?windowDays=7");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(5, items[0].GetProperty("downloads").GetInt64());
    }

    [Fact]
    public async Task GetDownloadsMetrics_AnonymousRequest_Returns401()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        // No auth header
        var resp = await client.GetAsync("/api/v1/admin/metrics/downloads");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetDownloadsMetrics_NonAdminToken_Returns403()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        // Register a regular user (first user = admin, so use second)
        await client.PostAsync("/api/v1/auth/register", Json(new { username = "atp-root", password = "Password123!" }));
        await client.PostAsync("/api/v1/auth/register", Json(new { username = "atp-user", password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login", Json(new { username = "atp-user", password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await loginResp.Content.ReadAsStringAsync());
        string readToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);
        var resp = await client.GetAsync("/api/v1/admin/metrics/downloads");
        // A read-ceiling token cannot reach admin endpoints
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetDownloadsMetrics_EmptyRegistry_ReturnsEmptyPage()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string adminUser = "atp-empty";
        var (_, adminToken) = await RegisterAndGetTokensAsync(client, adminUser);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var resp = await client.GetAsync("/api/v1/admin/metrics/downloads");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task GetDownloadsMetrics_DefaultWindow_Is7Days()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string adminUser = "atp-defwin";
        var (publishToken, adminToken) = await RegisterAndGetTokensAsync(client, adminUser);
        await PublishAsync(client, publishToken, adminUser, "defwinpkg", "1.0.0");

        var bucket = new DateTime(DateTime.UtcNow.AddHours(-2).Ticks, DateTimeKind.Utc);
        bucket = new DateTime(bucket.Year, bucket.Month, bucket.Day, bucket.Hour, 0, 0, DateTimeKind.Utc);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{adminUser}/defwinpkg", "1.0.0", bucket, 4);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        // No windowDays param → defaults to 7
        var resp = await client.GetAsync("/api/v1/admin/metrics/downloads?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        Assert.Equal(7, items[0].GetProperty("windowDays").GetInt32());
    }
}
