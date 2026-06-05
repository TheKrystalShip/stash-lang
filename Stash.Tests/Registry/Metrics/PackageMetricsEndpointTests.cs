using System;
using System.Collections.Generic;
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
/// Integration tests for <c>GET /api/v1/packages/{scope}/{name}/metrics</c>
/// (PackagesController.GetPackageMetrics).
///
/// Covers:
/// <list type="bullet">
///   <item>200 with correct totals (rollups + open-bucket raw, no double-count).</item>
///   <item>perVersion array populated for all known versions.</item>
///   <item>404 for non-existent packages.</item>
/// </list>
/// Visibility behavior tests live in <see cref="MetricsVisibilityBehaviorTests"/>.
/// </summary>
public sealed class PackageMetricsEndpointTests
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

    /// <summary>Seeds rollup rows directly via DI scope for time-travel testing.</summary>
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

    /// <summary>Seeds daily rollup rows (to verify they are NOT double-counted).</summary>
    private static async Task SeedDailyRollupAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        string packageName, string version, DateTime bucketStart, long downloads)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
        db.DownloadRollupDaily.Add(new DownloadRollupDailyRecord
        {
            PackageName = packageName,
            Version     = version,
            BucketStart = bucketStart,
            Downloads   = downloads,
            BytesServed = downloads * 512,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a raw download event directly (open-bucket proxy for time-travel).</summary>
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
    public async Task GetPackageMetrics_ExistingPackageNoDownloads_Returns200WithZeroCounts()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string username = "pme-zero";
        string publishToken = await RegisterAndGetPublishTokenAsync(client, username);
        await PublishAsync(client, publishToken, username, "emptypkg", "1.0.0");

        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/emptypkg/metrics");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal($"@{username}/emptypkg", root.GetProperty("package").GetString());
        Assert.Equal(0, root.GetProperty("downloads").GetProperty("total").GetInt64());
        Assert.Equal(0, root.GetProperty("downloads").GetProperty("last24h").GetInt64());
        Assert.True(root.GetProperty("perVersion").GetArrayLength() >= 1, "perVersion should include the published version");
    }

    [Fact]
    public async Task GetPackageMetrics_WithClosedHourlyRollup_ReturnsCorrectTotal()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string username = "pme-rollup";
        string publishToken = await RegisterAndGetPublishTokenAsync(client, username);
        await PublishAsync(client, publishToken, username, "rolluppkg", "1.0.0");

        // Seed a closed hourly rollup (2 hours ago)
        var twoHoursAgo = DateTime.UtcNow.AddHours(-2);
        var bucketStart = new DateTime(twoHoursAgo.Year, twoHoursAgo.Month, twoHoursAgo.Day, twoHoursAgo.Hour, 0, 0, DateTimeKind.Utc);
        await SeedHourlyRollupAsync(ctx.Factory, $"@{username}/rolluppkg", "1.0.0", bucketStart, 5);

        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/rolluppkg/metrics");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var dl = doc.RootElement.GetProperty("downloads");
        Assert.Equal(5, dl.GetProperty("total").GetInt64());
        Assert.Equal(5, dl.GetProperty("last24h").GetInt64());
        Assert.Equal(5, dl.GetProperty("last7d").GetInt64());
    }

    [Fact]
    public async Task GetPackageMetrics_DailyRollupNotDoubleCountedWithHourly_TotalIsCorrect()
    {
        // The read model must NOT read daily rollups — only hourly + open-bucket raw.
        // Seed both hourly (5 downloads) and daily (5 downloads for the same period).
        // If both are summed, total would be 10 — that's the bug we prevent here.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string username = "pme-nodbl";
        string publishToken = await RegisterAndGetPublishTokenAsync(client, username);
        await PublishAsync(client, publishToken, username, "nodoublepkg", "1.0.0");

        var twoHoursAgo = DateTime.UtcNow.AddHours(-2);
        var hourBucket = new DateTime(twoHoursAgo.Year, twoHoursAgo.Month, twoHoursAgo.Day, twoHoursAgo.Hour, 0, 0, DateTimeKind.Utc);
        var dayBucket  = new DateTime(twoHoursAgo.Year, twoHoursAgo.Month, twoHoursAgo.Day, 0, 0, 0, DateTimeKind.Utc);

        await SeedHourlyRollupAsync(ctx.Factory, $"@{username}/nodoublepkg", "1.0.0", hourBucket, 5);
        await SeedDailyRollupAsync(ctx.Factory, $"@{username}/nodoublepkg", "1.0.0", dayBucket, 5);

        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/nodoublepkg/metrics");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        // Must be 5, not 10 (daily rollup not double-counted).
        Assert.Equal(5, doc.RootElement.GetProperty("downloads").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task GetPackageMetrics_OpenBucketRawAdded_TotalIncludesCurrentHour()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string username = "pme-openbk";
        string publishToken = await RegisterAndGetPublishTokenAsync(client, username);
        await PublishAsync(client, publishToken, username, "openbkpkg", "1.0.0");

        // Seed a raw event in the CURRENT open bucket (now — 5 min)
        await SeedRawEventAsync(ctx.Factory, $"@{username}/openbkpkg", "1.0.0", DateTime.UtcNow.AddMinutes(-5));

        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/openbkpkg/metrics");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(1, doc.RootElement.GetProperty("downloads").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task GetPackageMetrics_MultipleVersions_PerVersionArrayContainsAll()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        string username = "pme-multiv";
        string publishToken = await RegisterAndGetPublishTokenAsync(client, username);
        await PublishAsync(client, publishToken, username, "multivpkg", "1.0.0");
        await PublishAsync(client, publishToken, username, "multivpkg", "2.0.0");

        var resp = await client.GetAsync($"/api/v1/packages/{Uri.EscapeDataString(username)}/multivpkg/metrics");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var perVersion = doc.RootElement.GetProperty("perVersion");
        Assert.Equal(2, perVersion.GetArrayLength());
    }

    [Fact]
    public async Task GetPackageMetrics_NotExistentPackage_Returns404()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var ctx = CreateFactory(conn);
        var client = ctx.Factory.CreateClient();

        // Don't register or publish anything
        var resp = await client.GetAsync("/api/v1/packages/nobody/ghost/metrics");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
