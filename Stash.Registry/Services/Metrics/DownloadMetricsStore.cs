using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Contracts;
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
    public async Task<(long Total, long Last24h)> GetRegistryDownloadTotalsAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var cutoff24h        = now - TimeSpan.FromHours(24);
        var currentHourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

        // ── Closed-bucket totals from hourly rollups ──────────────────────────
        var closedRollups = await _db.DownloadRollupHourly
            .AsNoTracking()
            .Where(r => r.BucketStart < currentHourStart)
            .ToListAsync(cancellationToken);

        // ── Open-bucket raw events (current hour) ─────────────────────────────
        var openBucketEvents = await _db.DownloadEvents
            .AsNoTracking()
            .Where(e => e.Ts >= currentHourStart && e.Status == DownloadEventStatus.Success)
            .ToListAsync(cancellationToken);

        long total  = closedRollups.Sum(r => r.Downloads)
                    + openBucketEvents.Count;

        long last24h = closedRollups
                           .Where(r => r.BucketStart >= cutoff24h)
                           .Sum(r => r.Downloads)
                     + openBucketEvents
                           .Count(e => e.Ts >= cutoff24h);

        return (total, last24h);
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
        // NOTE: daily rollup reads directly from raw closed-bucket events — NOT
        // from hourly rows. Ordering invariant: rollup always runs before the
        // retention sweep so raw events exist when this query executes (safe with
        // the default RetentionDays=30; operators setting a very small RetentionDays
        // should ensure the rollup pass runs before the sweep on the same day).
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

    // ── Read model (M5) ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Dictionary<string, DownloadWindowCounts>> GetPackageDownloadsAsync(
        string packageName,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        // Window cutoffs (rolling, ending at now).
        var cutoff24h = now - TimeSpan.FromHours(24);
        var cutoff7d  = now - TimeSpan.FromDays(7);
        var cutoff30d = now - TimeSpan.FromDays(30);

        // The current open hourly bucket starts at the most recent full-hour boundary.
        var currentHourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

        // ── Closed buckets from hourly rollups ────────────────────────────────
        // Only rows with BucketStart < currentHourStart are in closed buckets.
        // We do NOT use daily rollups — hourly already covers all closed-day buckets
        // (daily is a subset), so mixing both would double-count closed-day hours.
        var closedRollups = await _db.DownloadRollupHourly
            .AsNoTracking()
            .Where(r => r.PackageName == packageName && r.BucketStart < currentHourStart)
            .ToListAsync(cancellationToken);

        // ── Current open bucket from raw events ──────────────────────────────
        // Events in [currentHourStart, now) are in the open bucket and not yet rolled up.
        // Mirror M4's status filter: only success events contribute.
        var openBucketEvents = await _db.DownloadEvents
            .AsNoTracking()
            .Where(e => e.PackageName == packageName
                        && e.Ts >= currentHourStart
                        && e.Status == DownloadEventStatus.Success)
            .ToListAsync(cancellationToken);

        // Merge both sources into per-version, per-bucket-start entries for counting.
        // For rollups, BucketStart is the hour boundary.
        // For raw open events, we use Ts as the effective timestamp.

        var result = new Dictionary<string, DownloadWindowCounts>(StringComparer.Ordinal);

        // Accumulate from closed hourly rollups.
        foreach (var r in closedRollups)
        {
            if (!result.TryGetValue(r.Version, out var counts))
            {
                counts = new DownloadWindowCounts();
                result[r.Version] = counts;
            }
            counts.Total += r.Downloads;
            // For windowed counts, use the bucket start as the representative timestamp.
            if (r.BucketStart >= cutoff24h) counts.Last24h += r.Downloads;
            if (r.BucketStart >= cutoff7d)  counts.Last7d  += r.Downloads;
            if (r.BucketStart >= cutoff30d) counts.Last30d += r.Downloads;
        }

        // Accumulate from open-bucket raw events.
        foreach (var e in openBucketEvents)
        {
            if (!result.TryGetValue(e.Version, out var counts))
            {
                counts = new DownloadWindowCounts();
                result[e.Version] = counts;
            }
            counts.Total += 1;
            if (e.Ts >= cutoff24h) counts.Last24h += 1;
            if (e.Ts >= cutoff7d)  counts.Last7d  += 1;
            if (e.Ts >= cutoff30d) counts.Last30d += 1;
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<(List<TopPackageDownloadsEntry> Entries, int TotalCount)> GetTopPackagesAsync(
        int windowDays,
        int page,
        int pageSize,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var windowStart   = now - TimeSpan.FromDays(windowDays);
        var currentHourStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

        // ── Closed-bucket sums (hourly rollups) ───────────────────────────────
        // Rows where BucketStart is in the window [windowStart, currentHourStart).
        var closedRollups = await _db.DownloadRollupHourly
            .AsNoTracking()
            .Where(r => r.BucketStart >= windowStart && r.BucketStart < currentHourStart)
            .ToListAsync(cancellationToken);

        // ── Open-bucket raw events (current hour, within the window) ─────────
        var openBucketEvents = await _db.DownloadEvents
            .AsNoTracking()
            .Where(e => e.Ts >= currentHourStart
                        && e.Ts >= windowStart
                        && e.Status == DownloadEventStatus.Success)
            .ToListAsync(cancellationToken);

        // Aggregate into per-package totals.
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var r in closedRollups)
        {
            totals.TryGetValue(r.PackageName, out long existing);
            totals[r.PackageName] = existing + r.Downloads;
        }
        foreach (var e in openBucketEvents)
        {
            totals.TryGetValue(e.PackageName, out long existing);
            totals[e.PackageName] = existing + 1;
        }

        // Sort descending by total, then page.
        var sorted = totals
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key) // deterministic secondary order
            .ToList();

        int totalCount = sorted.Count;
        var pageEntries = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(kv => new TopPackageDownloadsEntry
            {
                Package   = kv.Key,
                Downloads = kv.Value,
                WindowDays = windowDays,
            })
            .ToList();

        return (pageEntries, totalCount);
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
