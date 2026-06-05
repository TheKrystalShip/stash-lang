using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services.Metrics;
using Xunit;

namespace Stash.Tests.Registry.Metrics;

/// <summary>
/// Unit tests for <see cref="DownloadMetricsStore.SweepRetentionAsync"/>.
/// </summary>
/// <remarks>
/// Each test seeds raw <see cref="DownloadEventRecord"/> rows at fixed timestamps
/// and invokes the store method directly with an explicit <c>now</c> so the
/// tests are fully deterministic.
/// </remarks>
public sealed class RetentionSweepTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly RegistryDbContext _db;

    public RetentionSweepTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(_conn)
            .Options;
        _db = new RegistryDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private DownloadMetricsStore Store() => new(_db);

    private async Task SeedEventAsync(DateTime ts)
    {
        _db.DownloadEvents.Add(new DownloadEventRecord
        {
            PackageName = "@a/foo",
            Version = "1.0.0",
            Ts = ts,
            Status = DownloadEventStatus.Success,
            BytesServed = 512,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedRollupHourlyAsync(DateTime bucketStart, long downloads = 10)
    {
        _db.DownloadRollupHourly.Add(new DownloadRollupHourlyRecord
        {
            PackageName = "@a/foo",
            Version = "1.0.0",
            BucketStart = bucketStart,
            Downloads = downloads,
            BytesServed = 1024,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedRollupDailyAsync(DateTime bucketStart, long downloads = 100)
    {
        _db.DownloadRollupDaily.Add(new DownloadRollupDailyRecord
        {
            PackageName = "@a/foo",
            Version = "1.0.0",
            BucketStart = bucketStart,
            Downloads = downloads,
            BytesServed = 10240,
        });
        await _db.SaveChangesAsync();
    }

    // ── RetentionDays = 0 no-op ────────────────────────────────────────────────

    /// <summary>
    /// When <c>RetentionDays = 0</c>, the sweep is a no-op — no rows are deleted
    /// even though all events are in the past.
    /// </summary>
    [Fact]
    public async Task SweepRetentionAsync_RetentionDaysZero_IsNoOp()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        // Seed events that are all older than "now".
        await SeedEventAsync(now - TimeSpan.FromDays(60));
        await SeedEventAsync(now - TimeSpan.FromDays(45));
        await SeedEventAsync(now - TimeSpan.FromDays(1));

        var deleted = await Store().SweepRetentionAsync(retentionDays: 0, now: now);

        Assert.Equal(0, deleted);
        Assert.Equal(3, await _db.DownloadEvents.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// A negative <c>RetentionDays</c> value is also treated as a no-op (defensive guard).
    /// </summary>
    [Fact]
    public async Task SweepRetentionAsync_NegativeRetentionDays_IsNoOp()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
        await SeedEventAsync(now - TimeSpan.FromDays(365));

        var deleted = await Store().SweepRetentionAsync(retentionDays: -1, now: now);

        Assert.Equal(0, deleted);
        Assert.Equal(1, await _db.DownloadEvents.AsNoTracking().CountAsync());
    }

    // ── Stale rows deleted ─────────────────────────────────────────────────────

    /// <summary>
    /// Events older than <c>RetentionDays</c> are deleted; events within the window are kept.
    /// </summary>
    [Fact]
    public async Task SweepRetentionAsync_StaleRows_AreDeleted()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        // Stale: older than 30 days.
        await SeedEventAsync(now - TimeSpan.FromDays(31));
        await SeedEventAsync(now - TimeSpan.FromDays(45));

        // Fresh: within the retention window.
        await SeedEventAsync(now - TimeSpan.FromDays(10));
        await SeedEventAsync(now - TimeSpan.FromDays(29));

        var deleted = await Store().SweepRetentionAsync(retentionDays: 30, now: now);

        Assert.Equal(2, deleted);
        Assert.Equal(2, await _db.DownloadEvents.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// An event whose timestamp is exactly at the retention boundary
    /// (<c>now - retentionDays</c>) is NOT deleted (strict <c>&lt;</c> comparison).
    /// </summary>
    [Fact]
    public async Task SweepRetentionAsync_ExactlyAtBoundary_NotDeleted()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
        var cutoff = now - TimeSpan.FromDays(30); // exactly at boundary

        await SeedEventAsync(cutoff);          // at boundary — keep
        await SeedEventAsync(cutoff - TimeSpan.FromSeconds(1)); // just before — delete

        var deleted = await Store().SweepRetentionAsync(retentionDays: 30, now: now);

        Assert.Equal(1, deleted);
        var remaining = await _db.DownloadEvents.AsNoTracking().SingleAsync();
        Assert.Equal(cutoff, remaining.Ts);
    }

    /// <summary>
    /// No rows are deleted when all events are within the retention window.
    /// </summary>
    [Fact]
    public async Task SweepRetentionAsync_AllFresh_NothingDeleted()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        await SeedEventAsync(now - TimeSpan.FromDays(5));
        await SeedEventAsync(now - TimeSpan.FromDays(15));
        await SeedEventAsync(now - TimeSpan.FromDays(29));

        var deleted = await Store().SweepRetentionAsync(retentionDays: 30, now: now);

        Assert.Equal(0, deleted);
        Assert.Equal(3, await _db.DownloadEvents.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// Returns 0 when there are no rows at all.
    /// </summary>
    [Fact]
    public async Task SweepRetentionAsync_EmptyTable_ReturnsZero()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        var deleted = await Store().SweepRetentionAsync(retentionDays: 30, now: now);

        Assert.Equal(0, deleted);
    }

    // ── Rollup tables are never touched ───────────────────────────────────────

    /// <summary>
    /// The retention sweep only deletes from <c>download_events</c>.  Rollup rows
    /// in <c>download_rollup_hourly</c> and <c>download_rollup_daily</c> are permanent
    /// and must never be deleted.
    /// </summary>
    [Fact]
    public async Task SweepRetentionAsync_RollupTables_AreUntouched()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        // Old raw event — will be swept.
        await SeedEventAsync(now - TimeSpan.FromDays(60));

        // Rollup rows for old buckets — must survive the sweep.
        await SeedRollupHourlyAsync(now - TimeSpan.FromDays(90));
        await SeedRollupDailyAsync(now - TimeSpan.FromDays(90));

        var deleted = await Store().SweepRetentionAsync(retentionDays: 30, now: now);

        Assert.Equal(1, deleted);
        Assert.Equal(0, await _db.DownloadEvents.AsNoTracking().CountAsync());
        Assert.Equal(1, await _db.DownloadRollupHourly.AsNoTracking().CountAsync());
        Assert.Equal(1, await _db.DownloadRollupDaily.AsNoTracking().CountAsync());
    }

    // ── Configurable retention window ─────────────────────────────────────────

    /// <summary>
    /// A short retention window (e.g. 1 day) deletes events older than 1 day.
    /// </summary>
    [Fact]
    public async Task SweepRetentionAsync_ShortRetentionWindow_DeletesCorrectRows()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        await SeedEventAsync(now - TimeSpan.FromHours(25)); // stale (> 1d)
        await SeedEventAsync(now - TimeSpan.FromHours(12)); // fresh (< 1d)

        var deleted = await Store().SweepRetentionAsync(retentionDays: 1, now: now);

        Assert.Equal(1, deleted);
    }

    /// <summary>
    /// A long retention window (e.g. 90 days) preserves events within 90 days.
    /// </summary>
    [Fact]
    public async Task SweepRetentionAsync_LongRetentionWindow_PreservesRecentRows()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        await SeedEventAsync(now - TimeSpan.FromDays(89)); // fresh (< 90d)
        await SeedEventAsync(now - TimeSpan.FromDays(91)); // stale (> 90d)

        var deleted = await Store().SweepRetentionAsync(retentionDays: 90, now: now);

        Assert.Equal(1, deleted);
        var kept = await _db.DownloadEvents.AsNoTracking().SingleAsync();
        Assert.Equal(now - TimeSpan.FromDays(89), kept.Ts);
    }
}
