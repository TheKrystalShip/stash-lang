using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Services.Metrics;

/// <summary>
/// EF Core-backed implementation of <see cref="IDownloadMetricsStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// This service is <b>scoped</b> — it uses the per-scope <see cref="RegistryDbContext"/>
/// and must therefore be resolved inside a DI scope created by
/// <see cref="MetricsBackgroundService"/> (never from a singleton scope).
/// </para>
/// </remarks>
public sealed class DownloadMetricsStore : IDownloadMetricsStore
{
    private readonly RegistryDbContext _db;

    /// <summary>
    /// Initialises the store with an EF Core context.
    /// </summary>
    /// <param name="db">The scoped <see cref="RegistryDbContext"/>.</param>
    public DownloadMetricsStore(RegistryDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task InsertEventsAsync(
        IEnumerable<DownloadEventRecord> events,
        CancellationToken cancellationToken = default)
    {
        await _db.DownloadEvents.AddRangeAsync(events, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> RollupAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        // Compute the start of the current (open) hour and day buckets.
        // Events with ts < currentHourStart are in closed hourly buckets.
        // Events with ts < currentDayStart are in closed daily buckets.
        var currentHourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var currentDayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);

        int inserted = 0;

        // Pull closed events from DB to memory for grouping.
        // Only success-status events contribute to rollup counts.
        var closedEvents = await _db.DownloadEvents
            .AsNoTracking()
            .Where(e => e.Ts < currentHourStart && e.Status == DownloadEventStatus.Success)
            .ToListAsync(cancellationToken);

        if (closedEvents.Count == 0)
            return 0;

        // ── Hourly rollup ──────────────────────────────────────────────────────
        // Group closed events by (package, version, hour bucket).
        var hourlyBuckets = closedEvents
            .GroupBy(e => new
            {
                e.PackageName,
                e.Version,
                BucketStart = new DateTime(e.Ts.Year, e.Ts.Month, e.Ts.Day, e.Ts.Hour, 0, 0, DateTimeKind.Utc),
            })
            .Select(g => new
            {
                g.Key.PackageName,
                g.Key.Version,
                g.Key.BucketStart,
                Downloads = (long)g.Count(),
                BytesServed = g.Sum(e => e.BytesServed),
            })
            .ToList();

        // Load the set of already-rolled hourly keys to implement skip-already-rolled idempotency.
        var existingHourlyKeys = await _db.DownloadRollupHourly
            .AsNoTracking()
            .Select(r => new { r.PackageName, r.Version, r.BucketStart })
            .ToListAsync(cancellationToken);
        var existingHourlySet = existingHourlyKeys
            .Select(k => (k.PackageName, k.Version, k.BucketStart))
            .ToHashSet();

        var newHourly = hourlyBuckets
            .Where(b => !existingHourlySet.Contains((b.PackageName, b.Version, b.BucketStart)))
            .Select(b => new DownloadRollupHourlyRecord
            {
                PackageName = b.PackageName,
                Version = b.Version,
                BucketStart = b.BucketStart,
                Downloads = b.Downloads,
                BytesServed = b.BytesServed,
            })
            .ToList();

        if (newHourly.Count > 0)
        {
            await _db.DownloadRollupHourly.AddRangeAsync(newHourly, cancellationToken);
            inserted += newHourly.Count;
        }

        // ── Daily rollup ───────────────────────────────────────────────────────
        // Include all closed events (ts < currentDayStart) in the daily rollup.
        // Events in closed hours but today (ts >= currentDayStart) are NOT yet
        // in a closed day bucket; skip them for daily rollup now.
        var closedDailyEvents = closedEvents
            .Where(e => e.Ts < currentDayStart)
            .ToList();

        if (closedDailyEvents.Count > 0)
        {
            var dailyBuckets = closedDailyEvents
                .GroupBy(e => new
                {
                    e.PackageName,
                    e.Version,
                    BucketStart = new DateTime(e.Ts.Year, e.Ts.Month, e.Ts.Day, 0, 0, 0, DateTimeKind.Utc),
                })
                .Select(g => new
                {
                    g.Key.PackageName,
                    g.Key.Version,
                    g.Key.BucketStart,
                    Downloads = (long)g.Count(),
                    BytesServed = g.Sum(e => e.BytesServed),
                })
                .ToList();

            var existingDailyKeys = await _db.DownloadRollupDaily
                .AsNoTracking()
                .Select(r => new { r.PackageName, r.Version, r.BucketStart })
                .ToListAsync(cancellationToken);
            var existingDailySet = existingDailyKeys
                .Select(k => (k.PackageName, k.Version, k.BucketStart))
                .ToHashSet();

            var newDaily = dailyBuckets
                .Where(b => !existingDailySet.Contains((b.PackageName, b.Version, b.BucketStart)))
                .Select(b => new DownloadRollupDailyRecord
                {
                    PackageName = b.PackageName,
                    Version = b.Version,
                    BucketStart = b.BucketStart,
                    Downloads = b.Downloads,
                    BytesServed = b.BytesServed,
                })
                .ToList();

            if (newDaily.Count > 0)
            {
                await _db.DownloadRollupDaily.AddRangeAsync(newDaily, cancellationToken);
                inserted += newDaily.Count;
            }
        }

        if (inserted > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return inserted;
    }

    /// <inheritdoc/>
    public async Task<int> SweepRetentionAsync(
        int retentionDays,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        // RetentionDays=0 means raw capture is disabled — no-op.
        // A naive cutoff of "now - 0 days" would delete ALL raw events, which is wrong.
        if (retentionDays <= 0)
            return 0;

        var cutoff = now - TimeSpan.FromDays(retentionDays);

        // Load candidates into memory then remove — EF Core on SQLite does not translate
        // a bulk-delete without ExecuteDeleteAsync (EF Core 7+), and we want the same
        // in-memory SQLite path the other methods use for testability.
        var stale = await _db.DownloadEvents
            .Where(e => e.Ts < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
            return 0;

        _db.DownloadEvents.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}
