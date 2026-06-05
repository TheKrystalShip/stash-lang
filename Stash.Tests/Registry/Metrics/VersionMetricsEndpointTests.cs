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
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services.Metrics;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Registry.Metrics;

/// <summary>
/// Integration tests for <c>GET /api/v1/packages/{scope}/{name}/{version}/metrics</c>
/// (PackagesController.GetVersionMetrics).
///
/// Covers:
/// <list type="bullet">
///   <item>200 with correct per-version download counts (rollups + open-bucket raw).</item>
///   <item>404 for non-existent package or version.</item>
/// </list>
/// Visibility behavior tests live in <see cref="MetricsVisibilityBehaviorTests"/>.
/// </summary>
public sealed class VersionMetricsEndpointTests
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

    private static async Task<string> RegisterAndGetPublishTokenAsync(HttpClient client, string username)
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
        return tokenDoc.RootElement.GetProperty("token").GetString()!;
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

    private static async Task SeedRawEventAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        string packageName, string version, DateTime ts)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
        db.DownloadEvents.Add(new DownloadEventRecord
        {
            PackageName = packageName,
            Version     = version,
            Ts          = ts,
            Status      = DownloadEventStatus.Success,
            BytesServed = 512,
        });
        await db.SaveChangesAsync();
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVersionMetrics_ExistingVersionNoDownloads_Returns200WithZeroCounts()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string username = "vme-zero";
        string publishToken = await RegisterAndGetPublishTokenAsync(client, username);
        await PublishAsync(client, publishToken, username, "emptyverpkg", "1.0.0");

        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/emptyverpkg/1.0.0/metrics");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal($"@{username}/emptyverpkg", root.GetProperty("package").GetString());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("downloads").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task GetVersionMetrics_WithRollup_ReturnsRollupCount()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string username = "vme-rollup";
        string publishToken = await RegisterAndGetPublishTokenAsync(client, username);
        await PublishAsync(client, publishToken, username, "rollupverpkg", "1.0.0");
        await PublishAsync(client, publishToken, username, "rollupverpkg", "2.0.0");

        var twoHoursAgo = DateTime.UtcNow.AddHours(-2);
        var bucket = new DateTime(twoHoursAgo.Year, twoHoursAgo.Month, twoHoursAgo.Day, twoHoursAgo.Hour, 0, 0, DateTimeKind.Utc);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{username}/rollupverpkg", "1.0.0", bucket, 3);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{username}/rollupverpkg", "2.0.0", bucket, 7);

        // Query version 1.0.0 — should only see 3, not 7
        var resp1 = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/rollupverpkg/1.0.0/metrics");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        using var doc1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
        Assert.Equal(3, doc1.RootElement.GetProperty("downloads").GetProperty("total").GetInt64());

        // Query version 2.0.0 — should only see 7
        var resp2 = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/rollupverpkg/2.0.0/metrics");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        using var doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());
        Assert.Equal(7, doc2.RootElement.GetProperty("downloads").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task GetVersionMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string username = "vme-openbk";
        string publishToken = await RegisterAndGetPublishTokenAsync(client, username);
        await PublishAsync(client, publishToken, username, "openbkverpkg", "1.0.0");

        // Seed a raw event in the current open bucket
        await SeedRawEventAsync(ctx.Factory, $"@{username}/openbkverpkg", "1.0.0", DateTime.UtcNow.AddMinutes(-3));

        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/openbkverpkg/1.0.0/metrics");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(1, doc.RootElement.GetProperty("downloads").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task GetVersionMetrics_NonExistentPackage_Returns404()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        var resp = await client.GetAsync("/api/v1/packages/nobody/ghostpkg/1.0.0/metrics");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetVersionMetrics_NonExistentVersion_Returns404()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string username = "vme-noversion";
        string publishToken = await RegisterAndGetPublishTokenAsync(client, username);
        await PublishAsync(client, publishToken, username, "existspkg", "1.0.0");

        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/existspkg/9.9.9/metrics");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetVersionMetrics_ResponseShape_ContainsAllFields()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string username = "vme-shape";
        string publishToken = await RegisterAndGetPublishTokenAsync(client, username);
        await PublishAsync(client, publishToken, username, "shapepkg", "1.0.0");

        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/shapepkg/1.0.0/metrics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("package", out _), "missing 'package' field");
        Assert.True(root.TryGetProperty("version", out _), "missing 'version' field");
        Assert.True(root.TryGetProperty("downloads", out var dl), "missing 'downloads' field");
        Assert.True(root.TryGetProperty("generatedAt", out _), "missing 'generatedAt' field");
        Assert.True(dl.TryGetProperty("total", out _), "downloads missing 'total'");
        Assert.True(dl.TryGetProperty("last24h", out _), "downloads missing 'last24h'");
        Assert.True(dl.TryGetProperty("last7d", out _), "downloads missing 'last7d'");
        Assert.True(dl.TryGetProperty("last30d", out _), "downloads missing 'last30d'");
    }
}
