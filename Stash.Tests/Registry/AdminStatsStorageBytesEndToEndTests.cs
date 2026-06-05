using System.Formats.Tar;
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
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// End-to-end tests verifying that <c>GET /api/v1/admin/stats</c> reports
/// <c>storageBytes &gt;= M</c> after a tarball of known size <c>M</c> is published
/// (M2 acceptance criterion — write path + read path must both be wired in the same phase).
/// </summary>
public sealed class AdminStatsStorageBytesEndToEndTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private static WebApplicationFactory<Stash.Registry.Program> CreateFactory(SqliteConnection conn)
    {
        return new WebApplicationFactory<Stash.Registry.Program>()
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

                    services.AddDbContext<RegistryDbContext>(options =>
                        options.UseSqlite(conn));

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });
    }

    private static StringContent Json(object body)
        => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    /// <summary>
    /// Registers the first user (who receives the admin role), logs in, and issues
    /// both a publish-ceiling token and an admin-ceiling token.
    /// Login returns a read-ceiling token; we explicitly upgrade to each ceiling.
    /// </summary>
    private static async Task<(string publishToken, string adminToken)> RegisterAndGetTokensAsync(
        HttpClient client, string username)
    {
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username, password = "Password123!" }));

        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username, password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        string loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string readToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        var savedAuth = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);

        // Upgrade to publish-ceiling
        var publishResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "1d" }));
        publishResp.EnsureSuccessStatusCode();
        using var publishDoc = JsonDocument.Parse(await publishResp.Content.ReadAsStringAsync());
        string publishToken = publishDoc.RootElement.GetProperty("token").GetString()!;

        // Upgrade to admin-ceiling
        var adminResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "admin", expiresIn = "1d" }));
        adminResp.EnsureSuccessStatusCode();
        using var adminDoc = JsonDocument.Parse(await adminResp.Content.ReadAsStringAsync());
        string adminToken = adminDoc.RootElement.GetProperty("token").GetString()!;

        client.DefaultRequestHeaders.Authorization = savedAuth;
        return (publishToken, adminToken);
    }

    /// <summary>
    /// Builds a minimal valid .tar.gz tarball for the given package name + version,
    /// with a configurable payload to drive the tarball size.
    /// </summary>
    private static byte[] BuildTarball(string packageName, string version, int payloadBytes = 1024)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.NoCompression, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            // stash.json manifest
            var manifest = new
            {
                name = packageName,
                version,
                description = "E2E test package",
                license = "MIT"
            };
            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "stash.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            });

            // A .stash file (required by PackageService validation)
            byte[] stashBytes = Encoding.UTF8.GetBytes("fn main() { io.println(\"hello\"); }");
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "main.stash")
            {
                DataStream = new MemoryStream(stashBytes)
            });

            // Extra payload to make the tarball size predictable and observable
            byte[] payload = new byte[payloadBytes];
            Array.Fill(payload, (byte)0x42);
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "data.bin")
            {
                DataStream = new MemoryStream(payload)
            });
        }

        return ms.ToArray();
    }

    // ── Acceptance tests ──────────────────────────────────────────────────────

    /// <summary>
    /// Publish a tarball of known size <c>M</c>, then
    /// <c>GET /api/v1/admin/stats</c> reports <c>storageBytes &gt;= M</c>.
    /// This is the load-bearing M2 end-to-end check: both the publish-path write
    /// (<c>PackageService.PublishAsync</c> sets <c>VersionRecord.StorageBytes</c>)
    /// and the admin-stats read (<c>AdminController.GetStats</c> returns SUM from DB)
    /// must be wired correctly for this test to pass.
    /// </summary>
    [Fact]
    public async Task GetStats_AfterPublish_ReportsStorageBytesAtLeastTarballSize()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        // Register first user → receives admin role automatically
        string username = "admin-stats-e2e";
        var (publishToken, adminToken) = await RegisterAndGetTokensAsync(client, username);

        // Build a tarball of known minimum size
        const int payloadBytes = 4096;
        byte[] tarball = BuildTarball("@" + username + "/pkg-e2e", "1.0.0", payloadBytes);
        long tarballSize = tarball.LongLength;

        // Publish via the packages endpoint with the publish-ceiling token
        await PublishTarball(client, publishToken, username, "pkg-e2e", tarball);

        // GET /api/v1/admin/stats as admin
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var statsResp = await client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);

        string statsBody = await statsResp.Content.ReadAsStringAsync();
        using var statsDoc = JsonDocument.Parse(statsBody);

        Assert.True(statsDoc.RootElement.TryGetProperty("storageBytes", out var storageBytesElem),
            $"StatsResponse must contain 'storageBytes'. Body: {statsBody}");

        long reportedBytes = storageBytesElem.GetInt64();
        Assert.True(reportedBytes >= tarballSize,
            $"storageBytes ({reportedBytes}) must be >= published tarball size ({tarballSize}). Body: {statsBody}");
    }

    /// <summary>
    /// Before any packages are published, <c>GET /api/v1/admin/stats</c> reports
    /// <c>storageBytes = 0</c> (not an error, not a missing field).
    /// Guards the NULL-safe SUM pattern on an empty DB.
    /// </summary>
    [Fact]
    public async Task GetStats_NoPackages_ReportsStorageBytesZero()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var (_, adminToken) = await RegisterAndGetTokensAsync(client, "admin-stats-empty");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var statsResp = await client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);

        string statsBody = await statsResp.Content.ReadAsStringAsync();
        using var statsDoc = JsonDocument.Parse(statsBody);

        Assert.True(statsDoc.RootElement.TryGetProperty("storageBytes", out var storageBytesElem),
            $"StatsResponse must contain 'storageBytes' even on empty DB. Body: {statsBody}");

        Assert.Equal(0L, storageBytesElem.GetInt64());
    }

    /// <summary>
    /// After publishing multiple versions, <c>storageBytes</c> is the cumulative
    /// sum of all tarball sizes.
    /// </summary>
    [Fact]
    public async Task GetStats_MultipleVersions_StorageBytesIsSum()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string username = "admin-stats-sum";
        var (publishToken, adminToken) = await RegisterAndGetTokensAsync(client, username);

        // Publish version 1.0.0
        byte[] tarball1 = BuildTarball("@" + username + "/sum-pkg", "1.0.0", 2048);
        await PublishTarball(client, publishToken, username, "sum-pkg", tarball1);

        // Publish version 2.0.0
        byte[] tarball2 = BuildTarball("@" + username + "/sum-pkg", "2.0.0", 3072);
        await PublishTarball(client, publishToken, username, "sum-pkg", tarball2);

        long expectedMin = tarball1.LongLength + tarball2.LongLength;

        // GET stats
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var statsResp = await client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);

        string statsBody = await statsResp.Content.ReadAsStringAsync();
        using var statsDoc = JsonDocument.Parse(statsBody);

        Assert.True(statsDoc.RootElement.TryGetProperty("storageBytes", out var storageBytesElem),
            $"StatsResponse must contain 'storageBytes'. Body: {statsBody}");

        long reportedBytes = storageBytesElem.GetInt64();
        Assert.True(reportedBytes >= expectedMin,
            $"storageBytes ({reportedBytes}) must be >= sum of tarball sizes ({expectedMin}). Body: {statsBody}");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static async Task PublishTarball(
        HttpClient client,
        string publishToken,
        string username,
        string packageLocalName,
        byte[] tarball)
    {
        using var content = new ByteArrayContent(tarball);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var req = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/packages/{Uri.EscapeDataString(username)}/{packageLocalName}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        req.Content = content;

        var resp = await client.SendAsync(req);
        Assert.True(resp.IsSuccessStatusCode,
            $"Publish failed: {resp.StatusCode} — {await resp.Content.ReadAsStringAsync()}");
    }
}
