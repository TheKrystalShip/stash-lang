using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services.Metrics;
using Xunit;

namespace Stash.Tests.Registry.Metrics;

/// <summary>
/// Unit and integration tests for <see cref="MetricsBackgroundService"/>.
/// Verifies the drain loop, scope isolation, status constants, and shutdown behaviour.
/// </summary>
public sealed class MetricsBackgroundServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly RegistryDbContext _dbContext;
    private readonly DownloadEventQueue _queue;

    public MetricsBackgroundServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(_conn)
            .Options;
        _dbContext = new RegistryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _queue = new DownloadEventQueue();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _conn.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a DI scope factory backed by the shared in-memory SQLite connection,
    /// with <see cref="IDownloadMetricsStore"/> registered as scoped.
    /// </summary>
    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<RegistryDbContext>(opt => opt.UseSqlite(_conn));
        services.AddScoped<IDownloadMetricsStore, DownloadMetricsStore>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static DownloadEvent MakeEvent(string pkg = "@a/foo", string ver = "1.0.0") => new()
    {
        PackageName = pkg,
        Version = ver,
        Ts = DateTime.UtcNow,
        Ip = "aabbccddeeff00112233445566778899",
        UserAgent = "stash-cli/1.0",
        BytesServed = 2048,
        RequesterUser = null,
    };

    /// <summary>Returns a default <see cref="RegistryConfig"/> suitable for unit tests.</summary>
    private static RegistryConfig DefaultConfig() => new();

    // ── DrainBatchAsync ────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="MetricsBackgroundService.DrainBatchAsync"/> writes queued events to the
    /// <c>download_events</c> table using its OWN DI scope (scoped store / DbContext).
    /// </summary>
    [Fact]
    public async Task DrainBatchAsync_WritesEventsToDatabase()
    {
        _queue.Enqueue(MakeEvent());
        _queue.Enqueue(MakeEvent("@b/bar", "2.0.0"));

        var svc = new MetricsBackgroundService(
            _queue,
            BuildScopeFactory(),
            DefaultConfig(),
            NullLogger<MetricsBackgroundService>.Instance);

        await svc.DrainBatchAsync(CancellationToken.None);

        // Re-query via the shared context
        var events = await _dbContext.DownloadEvents.AsNoTracking().ToListAsync();
        Assert.Equal(2, events.Count);
    }

    /// <summary>
    /// Drained events carry the <see cref="DownloadEventStatus.Success"/> status constant.
    /// </summary>
    [Fact]
    public async Task DrainBatchAsync_PersistsSuccessStatus()
    {
        _queue.Enqueue(MakeEvent());

        var svc = new MetricsBackgroundService(
            _queue,
            BuildScopeFactory(),
            DefaultConfig(),
            NullLogger<MetricsBackgroundService>.Instance);

        await svc.DrainBatchAsync(CancellationToken.None);

        var row = await _dbContext.DownloadEvents.AsNoTracking().SingleAsync();
        Assert.Equal(DownloadEventStatus.Success, row.Status);
    }

    /// <summary>
    /// Drained events preserve all fields passed from the capture path.
    /// </summary>
    [Fact]
    public async Task DrainBatchAsync_PreservesAllEventFields()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
        var ev = new DownloadEvent
        {
            PackageName = "@scope/pkg",
            Version = "3.1.4",
            Ts = now,
            Ip = "deadbeef00112233445566778899aabb",
            UserAgent = "stash-cli/1.0",
            BytesServed = 4096,
            RequesterUser = "alice",
        };
        _queue.Enqueue(ev);

        var svc = new MetricsBackgroundService(
            _queue,
            BuildScopeFactory(),
            DefaultConfig(),
            NullLogger<MetricsBackgroundService>.Instance);

        await svc.DrainBatchAsync(CancellationToken.None);

        var row = await _dbContext.DownloadEvents.AsNoTracking().SingleAsync();
        Assert.Equal("@scope/pkg", row.PackageName);
        Assert.Equal("3.1.4", row.Version);
        Assert.Equal(now, row.Ts);
        Assert.Equal("deadbeef00112233445566778899aabb", row.Ip);
        Assert.Equal("stash-cli/1.0", row.UserAgent);
        Assert.Equal(4096L, row.BytesServed);
        Assert.Equal("alice", row.RequesterUser);
        Assert.Equal(DownloadEventStatus.Success, row.Status);
    }

    /// <summary>
    /// <see cref="MetricsBackgroundService.DrainBatchAsync"/> is a no-op when the queue
    /// is empty — no DB round-trip is made.
    /// </summary>
    [Fact]
    public async Task DrainBatchAsync_EmptyQueue_NoRowsInserted()
    {
        // Queue is empty — nothing enqueued.
        var svc = new MetricsBackgroundService(
            _queue,
            BuildScopeFactory(),
            DefaultConfig(),
            NullLogger<MetricsBackgroundService>.Instance);

        await svc.DrainBatchAsync(CancellationToken.None);

        var count = await _dbContext.DownloadEvents.AsNoTracking().CountAsync();
        Assert.Equal(0, count);
    }

    /// <summary>
    /// Batches larger than <see cref="MetricsBackgroundService.BatchSize"/> are split:
    /// the first drain reads only <c>BatchSize</c> events, leaving the rest for later.
    /// </summary>
    [Fact]
    public async Task DrainBatchAsync_LargerThanBatchSize_LeavesRemainder()
    {
        int total = MetricsBackgroundService.BatchSize + 3;
        for (int i = 0; i < total; i++)
            _queue.Enqueue(MakeEvent("@a/foo", $"1.0.{i}"));

        var svc = new MetricsBackgroundService(
            _queue,
            BuildScopeFactory(),
            DefaultConfig(),
            NullLogger<MetricsBackgroundService>.Instance);

        await svc.DrainBatchAsync(CancellationToken.None);

        // First drain wrote exactly BatchSize rows.
        int written = await _dbContext.DownloadEvents.AsNoTracking().CountAsync();
        Assert.Equal(MetricsBackgroundService.BatchSize, written);

        // Three events remain in the channel.
        int remaining = 0;
        while (_queue.Reader.TryRead(out _))
            remaining++;
        Assert.Equal(3, remaining);
    }

    // ── Scope isolation ────────────────────────────────────────────────────────

    /// <summary>
    /// The background service opens its OWN scope per batch; the
    /// <see cref="IDownloadMetricsStore"/> is resolved inside that scope, not injected
    /// as a constructor parameter (which would capture a disposed scoped DbContext).
    /// Proved by verifying the service is constructible without a scoped dependency.
    /// </summary>
    [Fact]
    public void MetricsBackgroundService_DoesNotTakeScopedStoreInConstructor()
    {
        // If the service incorrectly accepted IDownloadMetricsStore directly, it would
        // need to be constructed here with a store instance.  The constructor only takes
        // IDownloadEventQueue (singleton), IServiceScopeFactory (singleton), and ILogger.
        var svc = new MetricsBackgroundService(
            _queue,
            BuildScopeFactory(),
            DefaultConfig(),
            NullLogger<MetricsBackgroundService>.Instance);

        Assert.NotNull(svc);
    }

    // ── IP is never raw ───────────────────────────────────────────────────────

    /// <summary>
    /// The background service persists whatever IP string is in the event record.
    /// With the default <c>IpMode = hashed</c>, the controller stores a 32-char hex
    /// HMAC hash (never the raw IP).  This test asserts the drain does not alter or
    /// re-read the IP — it is stored verbatim from the event.
    /// </summary>
    [Fact]
    public async Task DrainBatchAsync_PersistsIpVerbatimFromEvent()
    {
        const string expectedIp = "aabbccddeeff00112233445566778899"; // 32-char hash
        _queue.Enqueue(new DownloadEvent
        {
            PackageName = "@x/ip-test",
            Version = "1.0.0",
            Ts = DateTime.UtcNow,
            Ip = expectedIp,
            BytesServed = 512,
        });

        var svc = new MetricsBackgroundService(
            _queue,
            BuildScopeFactory(),
            DefaultConfig(),
            NullLogger<MetricsBackgroundService>.Instance);

        await svc.DrainBatchAsync(CancellationToken.None);

        var row = await _dbContext.DownloadEvents.AsNoTracking().SingleAsync();
        Assert.Equal(expectedIp, row.Ip);
    }

    /// <summary>
    /// When <c>IpMode = off</c>, the event's <c>Ip</c> is <c>null</c> and the drain
    /// persists <c>null</c> to <c>download_events.ip</c>.
    /// </summary>
    [Fact]
    public async Task DrainBatchAsync_NullIp_PersistedAsNull()
    {
        _queue.Enqueue(new DownloadEvent
        {
            PackageName = "@x/null-ip",
            Version = "1.0.0",
            Ts = DateTime.UtcNow,
            Ip = null,
            BytesServed = 256,
        });

        var svc = new MetricsBackgroundService(
            _queue,
            BuildScopeFactory(),
            DefaultConfig(),
            NullLogger<MetricsBackgroundService>.Instance);

        await svc.DrainBatchAsync(CancellationToken.None);

        var row = await _dbContext.DownloadEvents.AsNoTracking().SingleAsync();
        Assert.Null(row.Ip);
    }
}
