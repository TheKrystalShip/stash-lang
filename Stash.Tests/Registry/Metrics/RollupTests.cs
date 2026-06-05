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
/// Unit tests for <see cref="DownloadMetricsStore.RollupAsync"/>.
/// </summary>
/// <remarks>
/// Each test seeds raw <see cref="DownloadEventRecord"/> rows at fixed timestamps
/// relative to a known <c>now</c>, then invokes the store method directly so timing
/// is fully deterministic without running the background service loop.
/// </remarks>
public sealed class RollupTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly RegistryDbContext _db;

    public RollupTests()
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

    /// <summary>
    /// Inserts a raw download event at the given UTC timestamp with success status.
    /// </summary>
    private async Task SeedEventAsync(string pkg, string ver, DateTime ts, long bytes = 1024)
    {
        _db.DownloadEvents.Add(new DownloadEventRecord
        {
            PackageName = pkg,
            Version = ver,
            Ts = ts,
            Status = DownloadEventStatus.Success,
            BytesServed = bytes,
        });
        await _db.SaveChangesAsync();
    }

    // ── Closed-bucket aggregation ──────────────────────────────────────────────

    /// <summary>
    /// Events in fully-closed hourly buckets are aggregated into
    /// <c>download_rollup_hourly</c>.  The download count matches the raw row count.
    /// </summary>
    [Fact]
    public async Task RollupAsync_ClosedHourlyBuckets_AreAggregated()
    {
        // "now" is at 2026-06-05 15:30 UTC.  Closed hours are everything before 15:00.
        var now = new DateTime(2026, 6, 5, 15, 30, 0, DateTimeKind.Utc);

        // 3 events in the closed 13:xx bucket.
        var h13 = new DateTime(2026, 6, 5, 13, 0, 0, DateTimeKind.Utc);
        await SeedEventAsync("@a/foo", "1.0.0", h13 + TimeSpan.FromMinutes(10));
        await SeedEventAsync("@a/foo", "1.0.0", h13 + TimeSpan.FromMinutes(25));
        await SeedEventAsync("@a/foo", "1.0.0", h13 + TimeSpan.FromMinutes(50));

        // 2 events in the closed 14:xx bucket.
        var h14 = new DateTime(2026, 6, 5, 14, 0, 0, DateTimeKind.Utc);
        await SeedEventAsync("@a/foo", "1.0.0", h14 + TimeSpan.FromMinutes(5));
        await SeedEventAsync("@a/foo", "1.0.0", h14 + TimeSpan.FromMinutes(45));

        var inserted = await Store().RollupAsync(now);

        // Two closed-hour rows should have been inserted.
        Assert.Equal(2, inserted);

        var hourly = await _db.DownloadRollupHourly.AsNoTracking()
            .OrderBy(r => r.BucketStart)
            .ToListAsync();
        Assert.Equal(2, hourly.Count);
        Assert.Equal(h13, hourly[0].BucketStart);
        Assert.Equal(3, hourly[0].Downloads);
        Assert.Equal(h14, hourly[1].BucketStart);
        Assert.Equal(2, hourly[1].Downloads);
    }

    /// <summary>
    /// SUM(rollup.downloads) across ALL hourly rollup rows equals the total count of
    /// raw events in closed buckets — the canonical acceptance SUM-equality check.
    /// </summary>
    [Fact]
    public async Task RollupAsync_SumOfHourlyDownloads_EqualsClosedBucketRawCount()
    {
        var now = new DateTime(2026, 6, 5, 16, 0, 0, DateTimeKind.Utc);

        // Seed 5 events spread across 3 closed hours, 1 package.
        var closedBase = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);
        for (int h = 0; h < 3; h++)
            for (int i = 0; i < (h + 1); i++) // 1 + 2 + 3 = 6 events total
                await SeedEventAsync("@a/foo", "1.0.0", closedBase + TimeSpan.FromHours(h) + TimeSpan.FromMinutes(i));

        int closedRawCount = 6;

        await Store().RollupAsync(now);

        long hourlySum = await _db.DownloadRollupHourly.AsNoTracking().SumAsync(r => r.Downloads);
        Assert.Equal(closedRawCount, hourlySum);
    }

    // ── Open-bucket exclusion ──────────────────────────────────────────────────

    /// <summary>
    /// Events whose timestamp falls within the CURRENT open hour are NOT included in
    /// the rollup — the open bucket must be excluded.
    /// </summary>
    [Fact]
    public async Task RollupAsync_OpenHourBucket_IsExcluded()
    {
        // "now" is at 15:45.  Current open hour starts at 15:00.
        var now = new DateTime(2026, 6, 5, 15, 45, 0, DateTimeKind.Utc);

        // One closed event (14:30).
        await SeedEventAsync("@a/foo", "1.0.0", new DateTime(2026, 6, 5, 14, 30, 0, DateTimeKind.Utc));

        // Two open-bucket events (15:10 and 15:30 — same hour as now).
        await SeedEventAsync("@a/foo", "1.0.0", new DateTime(2026, 6, 5, 15, 10, 0, DateTimeKind.Utc));
        await SeedEventAsync("@a/foo", "1.0.0", new DateTime(2026, 6, 5, 15, 30, 0, DateTimeKind.Utc));

        await Store().RollupAsync(now);

        // Only one hourly rollup row for the closed 14:xx bucket.
        var hourly = await _db.DownloadRollupHourly.AsNoTracking().ToListAsync();
        Assert.Single(hourly);
        Assert.Equal(new DateTime(2026, 6, 5, 14, 0, 0, DateTimeKind.Utc), hourly[0].BucketStart);
        Assert.Equal(1, hourly[0].Downloads);
    }

    /// <summary>
    /// No rollup rows are written when the only events are in the currently-open bucket.
    /// </summary>
    [Fact]
    public async Task RollupAsync_OnlyOpenBucketEvents_WritesNothing()
    {
        var now = new DateTime(2026, 6, 5, 10, 30, 0, DateTimeKind.Utc);

        // Both events are in the current open hour (10:xx).
        await SeedEventAsync("@a/foo", "1.0.0", new DateTime(2026, 6, 5, 10, 5, 0, DateTimeKind.Utc));
        await SeedEventAsync("@a/foo", "1.0.0", new DateTime(2026, 6, 5, 10, 20, 0, DateTimeKind.Utc));

        var inserted = await Store().RollupAsync(now);

        Assert.Equal(0, inserted);
        Assert.Equal(0, await _db.DownloadRollupHourly.AsNoTracking().CountAsync());
        Assert.Equal(0, await _db.DownloadRollupDaily.AsNoTracking().CountAsync());
    }

    // ── Daily rollup ───────────────────────────────────────────────────────────

    /// <summary>
    /// Events in fully-closed days are aggregated into <c>download_rollup_daily</c>.
    /// Events from today (even in closed hours) are NOT included in the daily rollup.
    /// </summary>
    [Fact]
    public async Task RollupAsync_ClosedDailyBuckets_AreAggregated()
    {
        // "now" is 2026-06-05 08:00.  Yesterday (2026-06-04) is fully closed.
        var now = new DateTime(2026, 6, 5, 8, 0, 0, DateTimeKind.Utc);

        // 3 events yesterday.
        var yesterday = new DateTime(2026, 6, 4, 10, 0, 0, DateTimeKind.Utc);
        await SeedEventAsync("@a/foo", "1.0.0", yesterday);
        await SeedEventAsync("@a/foo", "1.0.0", yesterday + TimeSpan.FromHours(2));
        await SeedEventAsync("@a/foo", "1.0.0", yesterday + TimeSpan.FromHours(5));

        // 2 events today (closed hours but today — should NOT be in daily rollup yet).
        var todayEarly = new DateTime(2026, 6, 5, 5, 0, 0, DateTimeKind.Utc);
        await SeedEventAsync("@a/foo", "1.0.0", todayEarly);
        await SeedEventAsync("@a/foo", "1.0.0", todayEarly + TimeSpan.FromHours(1));

        await Store().RollupAsync(now);

        // Only one daily row for yesterday.
        var daily = await _db.DownloadRollupDaily.AsNoTracking().ToListAsync();
        Assert.Single(daily);
        Assert.Equal(new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc), daily[0].BucketStart);
        Assert.Equal(3, daily[0].Downloads);
    }

    /// <summary>
    /// SUM(rollup.downloads) across ALL daily rollup rows equals the count of raw events
    /// in fully-closed day buckets (not today's events, even if in a closed hour).
    /// </summary>
    [Fact]
    public async Task RollupAsync_SumOfDailyDownloads_EqualsFullyClosedDayRawCount()
    {
        // "now" is 2026-06-05 12:00.  Days fully closed: 2026-06-03 and 2026-06-04.
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        // 4 events on 2026-06-03.
        var day1 = new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 4; i++)
            await SeedEventAsync("@a/foo", "1.0.0", day1 + TimeSpan.FromHours(i));

        // 3 events on 2026-06-04.
        var day2 = new DateTime(2026, 6, 4, 14, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 3; i++)
            await SeedEventAsync("@a/foo", "1.0.0", day2 + TimeSpan.FromHours(i));

        // 2 events today (should not appear in daily rollup yet).
        await SeedEventAsync("@a/foo", "1.0.0", new DateTime(2026, 6, 5, 8, 0, 0, DateTimeKind.Utc));
        await SeedEventAsync("@a/foo", "1.0.0", new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));

        await Store().RollupAsync(now);

        long dailySum = await _db.DownloadRollupDaily.AsNoTracking().SumAsync(r => r.Downloads);
        Assert.Equal(7, dailySum); // 4 + 3, NOT 9
    }

    // ── Idempotency ────────────────────────────────────────────────────────────

    /// <summary>
    /// Running the rollup pass twice over the same data set does NOT double-write rows.
    /// The second run inserts nothing for already-rolled buckets
    /// (skip-already-rolled behaviour).
    /// </summary>
    [Fact]
    public async Task RollupAsync_RunTwice_DoesNotDoubleWrite()
    {
        var now = new DateTime(2026, 6, 5, 14, 0, 0, DateTimeKind.Utc);
        var closed = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        await SeedEventAsync("@a/foo", "1.0.0", closed + TimeSpan.FromMinutes(10));
        await SeedEventAsync("@a/foo", "1.0.0", closed + TimeSpan.FromMinutes(30));

        var store = Store();

        int first = await store.RollupAsync(now);
        int second = await store.RollupAsync(now);

        // Second run inserts nothing.
        Assert.True(first > 0, "First run should insert at least one row.");
        Assert.Equal(0, second);

        // Total download count across hourly rollups is still 2 (not doubled).
        long hourlyTotal = await _db.DownloadRollupHourly.AsNoTracking().SumAsync(r => r.Downloads);
        Assert.Equal(2, hourlyTotal);
    }

    /// <summary>
    /// Skip-already-rolled is idempotent: after retention deletes the raw rows for a
    /// bucket, a subsequent rollup pass does NOT re-compute and re-insert a zero-count row
    /// for that bucket (the existing rollup row is left untouched).
    /// </summary>
    [Fact]
    public async Task RollupAsync_AfterRetention_ExistingRollupNotOverwritten()
    {
        var now = new DateTime(2026, 6, 5, 14, 0, 0, DateTimeKind.Utc);
        var closedBucketStart = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        // Seed and roll up.
        await SeedEventAsync("@a/foo", "1.0.0", closedBucketStart + TimeSpan.FromMinutes(5));
        await SeedEventAsync("@a/foo", "1.0.0", closedBucketStart + TimeSpan.FromMinutes(15));
        await Store().RollupAsync(now);

        // Simulate retention deleting the raw events for that bucket.
        var raw = await _db.DownloadEvents.ToListAsync();
        _db.DownloadEvents.RemoveRange(raw);
        await _db.SaveChangesAsync();

        // Second rollup pass — raw events gone, but rollup row already exists.
        await Store().RollupAsync(now);

        // The existing rollup row still reports 2 downloads (NOT overwritten with 0).
        var row = await _db.DownloadRollupHourly.AsNoTracking().SingleAsync();
        Assert.Equal(closedBucketStart, row.BucketStart);
        Assert.Equal(2, row.Downloads);
    }

    // ── Multi-package/version ──────────────────────────────────────────────────

    /// <summary>
    /// Multiple (package, version) pairs each produce their own rollup rows with
    /// independent download counts.
    /// </summary>
    [Fact]
    public async Task RollupAsync_MultiplePackagesAndVersions_AggregatedSeparately()
    {
        var now = new DateTime(2026, 6, 5, 15, 0, 0, DateTimeKind.Utc);
        var bucket = new DateTime(2026, 6, 5, 13, 0, 0, DateTimeKind.Utc);

        // @a/foo@1.0.0 — 3 downloads
        await SeedEventAsync("@a/foo", "1.0.0", bucket + TimeSpan.FromMinutes(5));
        await SeedEventAsync("@a/foo", "1.0.0", bucket + TimeSpan.FromMinutes(10));
        await SeedEventAsync("@a/foo", "1.0.0", bucket + TimeSpan.FromMinutes(15));
        // @a/foo@2.0.0 — 1 download
        await SeedEventAsync("@a/foo", "2.0.0", bucket + TimeSpan.FromMinutes(20));
        // @b/bar@1.0.0 — 2 downloads
        await SeedEventAsync("@b/bar", "1.0.0", bucket + TimeSpan.FromMinutes(25));
        await SeedEventAsync("@b/bar", "1.0.0", bucket + TimeSpan.FromMinutes(30));

        await Store().RollupAsync(now);

        var hourly = await _db.DownloadRollupHourly.AsNoTracking()
            .OrderBy(r => r.PackageName).ThenBy(r => r.Version)
            .ToListAsync();

        Assert.Equal(3, hourly.Count);
        Assert.Equal(("@a/foo", "1.0.0", 3L), (hourly[0].PackageName, hourly[0].Version, hourly[0].Downloads));
        Assert.Equal(("@a/foo", "2.0.0", 1L), (hourly[1].PackageName, hourly[1].Version, hourly[1].Downloads));
        Assert.Equal(("@b/bar", "1.0.0", 2L), (hourly[2].PackageName, hourly[2].Version, hourly[2].Downloads));
    }

    // ── Non-success events not counted ────────────────────────────────────────

    /// <summary>
    /// Events with a non-success status are excluded from rollup counts.
    /// Only <see cref="DownloadEventStatus.Success"/> events are counted.
    /// </summary>
    [Fact]
    public async Task RollupAsync_NonSuccessEvents_NotCounted()
    {
        var now = new DateTime(2026, 6, 5, 15, 0, 0, DateTimeKind.Utc);
        var bucket = new DateTime(2026, 6, 5, 13, 10, 0, DateTimeKind.Utc);

        // One success event.
        await SeedEventAsync("@a/foo", "1.0.0", bucket);

        // One non-success event (e.g. a future "incomplete" status code).
        _db.DownloadEvents.Add(new DownloadEventRecord
        {
            PackageName = "@a/foo",
            Version = "1.0.0",
            Ts = bucket + TimeSpan.FromMinutes(5),
            Status = "incomplete",
            BytesServed = 0,
        });
        await _db.SaveChangesAsync();

        await Store().RollupAsync(now);

        // Only the success event is counted.
        var row = await _db.DownloadRollupHourly.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.Downloads);
    }

    // ── BytesServed aggregation ────────────────────────────────────────────────

    /// <summary>
    /// The <c>bytes_served</c> column in rollup rows equals the sum of <c>bytes_served</c>
    /// across all raw events in that bucket.
    /// </summary>
    [Fact]
    public async Task RollupAsync_BytesServed_Summed()
    {
        var now = new DateTime(2026, 6, 5, 14, 0, 0, DateTimeKind.Utc);
        var bucket = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        await SeedEventAsync("@a/foo", "1.0.0", bucket + TimeSpan.FromMinutes(5), bytes: 1000);
        await SeedEventAsync("@a/foo", "1.0.0", bucket + TimeSpan.FromMinutes(10), bytes: 2000);
        await SeedEventAsync("@a/foo", "1.0.0", bucket + TimeSpan.FromMinutes(15), bytes: 3000);

        await Store().RollupAsync(now);

        var row = await _db.DownloadRollupHourly.AsNoTracking().SingleAsync();
        Assert.Equal(6000L, row.BytesServed);
    }
}
