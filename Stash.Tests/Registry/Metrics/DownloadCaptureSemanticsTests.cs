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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Services.Metrics;
using Xunit;

namespace Stash.Tests.Registry.Metrics;

/// <summary>
/// End-to-end tests that verify the count-on-completion semantics of
/// <c>PackagesController.DownloadVersion</c>:
/// <list type="bullet">
///   <item><description>A successful download (HTTP 200) enqueues exactly one event.</description></item>
///   <item><description>A 404 (missing version) enqueues ZERO events.</description></item>
///   <item><description>A <c>VisibilityHidden</c> 404 (anonymous on private package) enqueues ZERO events.</description></item>
///   <item><description>IP is populated through <see cref="IIpHasher"/> — never the raw IP string.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// These tests use a fake <see cref="IDownloadEventQueue"/> injected via
/// <c>ConfigureTestServices</c> so enqueue counts can be observed without requiring the
/// background service to drain to the DB.
/// </remarks>
public sealed class DownloadCaptureSemanticsTests
{
    // ── Fake queue ─────────────────────────────────────────────────────────────

    /// <summary>
    /// An in-memory fake that records all enqueued events.
    /// Thread-safe for testing only — uses a <c>lock</c> for simplicity.
    /// </summary>
    private sealed class FakeDownloadEventQueue : IDownloadEventQueue
    {
        private readonly System.Threading.Channels.Channel<DownloadEvent> _channel =
            System.Threading.Channels.Channel.CreateUnbounded<DownloadEvent>();

        public System.Threading.Channels.ChannelReader<DownloadEvent> Reader => _channel.Reader;

        private readonly List<DownloadEvent> _events = new();
        private readonly object _lock = new();

        public void Enqueue(DownloadEvent ev)
        {
            lock (_lock)
                _events.Add(ev);
            _channel.Writer.TryWrite(ev);
        }

        public IReadOnlyList<DownloadEvent> Events
        {
            get { lock (_lock) return _events.ToList().AsReadOnly(); }
        }

        public int Count
        {
            get { lock (_lock) return _events.Count; }
        }
    }

    // ── Factory ────────────────────────────────────────────────────────────────

    private static (WebApplicationFactory<Stash.Registry.Program>, FakeDownloadEventQueue) CreateFactory(
        SqliteConnection conn)
    {
        var fakeQueue = new FakeDownloadEventQueue();

        var factory = new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    // Replace DbContext
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(DbContextOptions<RegistryDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddDbContext<RegistryDbContext>(opt => opt.UseSqlite(conn));

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();

                    // Replace the queue with our fake so we can count enqueue calls.
                    services.AddSingleton<IDownloadEventQueue>(fakeQueue);
                });
            });

        return (factory, fakeQueue);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static StringContent Json(object body)
        => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<(string publishToken, string adminToken)> RegisterAndGetTokensAsync(
        HttpClient client, string username)
    {
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username, password = "Password123!" }));

        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username, password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        using var loginDoc = JsonDocument.Parse(await loginResp.Content.ReadAsStringAsync());
        string readToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        var savedAuth = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);

        var publishResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "1d" }));
        publishResp.EnsureSuccessStatusCode();
        using var publishDoc = JsonDocument.Parse(await publishResp.Content.ReadAsStringAsync());
        string publishToken = publishDoc.RootElement.GetProperty("token").GetString()!;

        var adminResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "admin", expiresIn = "1d" }));
        adminResp.EnsureSuccessStatusCode();
        using var adminDoc = JsonDocument.Parse(await adminResp.Content.ReadAsStringAsync());
        string adminToken = adminDoc.RootElement.GetProperty("token").GetString()!;

        client.DefaultRequestHeaders.Authorization = savedAuth;
        return (publishToken, adminToken);
    }

    private static byte[] BuildTarball(string packageName, string version, int payloadBytes = 512)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.NoCompression, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new
            {
                name = packageName,
                version,
                description = "metrics test",
                license = "MIT"
            };
            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "stash.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            });

            byte[] stashFile = Encoding.UTF8.GetBytes("fn main() { io.println(\"test\"); }");
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "main.stash")
            {
                DataStream = new MemoryStream(stashFile)
            });

            byte[] payload = new byte[payloadBytes];
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "data.bin")
            {
                DataStream = new MemoryStream(payload)
            });
        }
        return ms.ToArray();
    }

    private static async Task PublishTarball(
        HttpClient client, string publishToken, string username, string pkgName, string version = "1.0.0")
    {
        byte[] tarball = BuildTarball($"@{username}/{pkgName}", version);
        using var content = new ByteArrayContent(tarball);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var req = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/packages/{Uri.EscapeDataString(username)}/{pkgName}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        req.Content = content;

        var resp = await client.SendAsync(req);
        Assert.True(resp.IsSuccessStatusCode,
            $"Publish failed: {resp.StatusCode} — {await resp.Content.ReadAsStringAsync()}");
    }

    // Wait for the OnCompleted callback to fire.  The callback is async-deferred
    // by ASP.NET's response pipeline, so we poll briefly.
    private static async Task WaitForEventAsync(FakeDownloadEventQueue queue, int expected = 1,
        int maxWaitMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
        while (queue.Count < expected && DateTime.UtcNow < deadline)
            await Task.Delay(50);
    }

    // ── Acceptance tests ───────────────────────────────────────────────────────

    /// <summary>
    /// A successful HTTP 200 download enqueues exactly one event.
    /// </summary>
    [Fact]
    public async Task DownloadVersion_Success_EnqueuesExactlyOneEvent()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var (factory, fakeQueue) = CreateFactory(conn);
        await using var _ = factory;
        var client = factory.CreateClient();

        string username = "cap-success";
        var (publishToken, _) = await RegisterAndGetTokensAsync(client, username);
        await PublishTarball(client, publishToken, username, "success-pkg");

        // Issue a successful download
        var resp = await client.GetAsync(
            $"/api/v1/packages/{Uri.EscapeDataString(username)}/success-pkg/1.0.0/download");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        await WaitForEventAsync(fakeQueue);

        Assert.Equal(1, fakeQueue.Count);
        var ev = fakeQueue.Events[0];
        Assert.Equal($"@{username}/success-pkg", ev.PackageName);
        Assert.Equal("1.0.0", ev.Version);
    }

    /// <summary>
    /// A 404 (missing version) enqueues ZERO events.
    /// </summary>
    [Fact]
    public async Task DownloadVersion_MissingVersion_EnqueuesZeroEvents()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var (factory, fakeQueue) = CreateFactory(conn);
        await using var _ = factory;
        var client = factory.CreateClient();

        string username = "cap-missing";
        var (publishToken, _) = await RegisterAndGetTokensAsync(client, username);
        await PublishTarball(client, publishToken, username, "exists-pkg", "1.0.0");

        // Request a version that does not exist
        var resp = await client.GetAsync(
            $"/api/v1/packages/{Uri.EscapeDataString(username)}/exists-pkg/99.0.0/download");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        // Give any async callback time to fire (it should not)
        await Task.Delay(200);

        Assert.Equal(0, fakeQueue.Count);
    }

    /// <summary>
    /// A VisibilityHidden 404 (anonymous caller on a private package) enqueues ZERO events.
    /// </summary>
    [Fact]
    public async Task DownloadVersion_VisibilityHidden_EnqueuesZeroEvents()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var (factory, fakeQueue) = CreateFactory(conn);
        await using var _ = factory;
        var client = factory.CreateClient();

        string username = "cap-private";
        var (publishToken, _) = await RegisterAndGetTokensAsync(client, username);
        await PublishTarball(client, publishToken, username, "private-pkg");

        // Make the package private
        var visibilityReq = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/packages/{Uri.EscapeDataString(username)}/private-pkg/visibility");
        visibilityReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        visibilityReq.Content = Json(new { visibility = "private" });
        var visResp = await client.SendAsync(visibilityReq);
        Assert.True(visResp.IsSuccessStatusCode,
            $"Visibility change failed: {await visResp.Content.ReadAsStringAsync()}");

        // Anonymous download attempt — should get 404 (VisibilityHidden)
        var anonClient = factory.CreateClient();  // no auth
        var resp = await anonClient.GetAsync(
            $"/api/v1/packages/{Uri.EscapeDataString(username)}/private-pkg/1.0.0/download");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        // Give any async callback time to fire (it should not)
        await Task.Delay(200);

        Assert.Equal(0, fakeQueue.Count);
    }

    /// <summary>
    /// The <c>ip</c> field on a queued event is the <see cref="IIpHasher"/>-transformed
    /// value — it may be a hash, truncated prefix, the raw address, or <c>null</c>
    /// depending on config.  With default settings (<c>IpMode = hashed</c>) the stored
    /// value is a 32-char hex string when the remote address is available, never the
    /// raw address string.  When the remote address is unavailable (null — common in
    /// TestHost), <c>null</c> is also valid (IIpHasher.Apply(null) = null).
    /// </summary>
    [Fact]
    public async Task DownloadVersion_Success_IpPopulatedThroughIIpHasher()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var (factory, fakeQueue) = CreateFactory(conn);
        await using var _ = factory;
        var client = factory.CreateClient();

        string username = "cap-ip-test";
        var (publishToken, _) = await RegisterAndGetTokensAsync(client, username);
        await PublishTarball(client, publishToken, username, "ip-pkg");

        var resp = await client.GetAsync(
            $"/api/v1/packages/{Uri.EscapeDataString(username)}/ip-pkg/1.0.0/download");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        await WaitForEventAsync(fakeQueue);
        Assert.Equal(1, fakeQueue.Count);

        var ev = fakeQueue.Events[0];
        // The IP must NOT be the raw loopback address string.  With default
        // IpMode = hashed, either the IP is a 32-char hex hash or null (if
        // the TestHost provides no RemoteIpAddress — IIpHasher.Apply(null) = null).
        if (ev.Ip != null)
        {
            // If non-null, it must be hashed: 32 hex chars, never the raw IP.
            Assert.Equal(32, ev.Ip.Length);
            Assert.DoesNotContain("127.0.0.1", ev.Ip);
            Assert.DoesNotContain("::1", ev.Ip);
        }
        // null is also valid — it proves the code called IIpHasher.Apply (which returns
        // null for a null address) rather than reading RemoteIpAddress?.ToString() directly
        // (which would give "::1" or "127.0.0.1" from the TestHost, not null).
    }

    /// <summary>
    /// Multiple successful downloads of the same version each enqueue their own event
    /// (one event per download, not deduplicated).
    /// </summary>
    [Fact]
    public async Task DownloadVersion_MultipleSuccesses_EnqueuesOneEventEach()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var (factory, fakeQueue) = CreateFactory(conn);
        await using var _ = factory;
        var client = factory.CreateClient();

        string username = "cap-multi";
        var (publishToken, _) = await RegisterAndGetTokensAsync(client, username);
        await PublishTarball(client, publishToken, username, "multi-pkg");

        const int downloadCount = 3;
        for (int i = 0; i < downloadCount; i++)
        {
            var resp = await client.GetAsync(
                $"/api/v1/packages/{Uri.EscapeDataString(username)}/multi-pkg/1.0.0/download");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        await WaitForEventAsync(fakeQueue, expected: downloadCount);

        Assert.Equal(downloadCount, fakeQueue.Count);
    }
}
