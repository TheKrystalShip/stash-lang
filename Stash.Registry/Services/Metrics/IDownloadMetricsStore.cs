using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Registry.Contracts;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Services.Metrics;

/// <summary>
/// Persistence abstraction for download metrics.  Wraps the EF Core layer so
/// <see cref="MetricsBackgroundService"/> can write events without holding a direct
/// reference to a scoped <c>DbContext</c>.
/// </summary>
/// <remarks>
/// Implementations are <b>scoped</b>: a new instance is resolved inside a per-batch
/// DI scope created by <see cref="MetricsBackgroundService"/>.  Never inject this
/// service into a singleton — the underlying <c>DbContext</c> is scoped and will be
/// disposed between scope lifetimes.
/// </remarks>
public interface IDownloadMetricsStore
{
    /// <summary>
    /// Persists a batch of <see cref="DownloadEventRecord"/> rows to the
    /// <c>download_events</c> table.
    /// </summary>
    /// <param name="events">The records to insert.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task InsertEventsAsync(
        IEnumerable<DownloadEventRecord> events,
        CancellationToken cancellationToken = default);

    // ── Read model (M5) ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns aggregate download counts for every version of <paramref name="packageName"/>
    /// across four rolling windows (total, 24 h, 7 d, 30 d).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read-model contract (D8 brief).</b>
    /// Closed hourly-rollup buckets are authoritative.  The current open bucket (the in-progress
    /// hour whose <c>bucket_start = truncate(now, hour)</c>) is computed on-demand from the
    /// raw <c>download_events</c> table and added to the closed-bucket sum to avoid
    /// double-counting (M4's rollup job deliberately excludes the open bucket, so raw is its
    /// only source).  Only <c>status = 'success'</c> raw events are counted.
    /// </para>
    /// </remarks>
    /// <param name="packageName">The fully-qualified package name (e.g. <c>@scope/name</c>).</param>
    /// <param name="now">
    /// The reference "current" time used to compute window cutoffs and the open-bucket boundary.
    /// Injected rather than derived from <see cref="DateTime.UtcNow"/> so tests can
    /// control time without wall-clock dependency.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A dictionary keyed by <c>version</c> string, each value containing
    /// <see cref="DownloadWindowCounts"/> for that version.  Versions with no downloads
    /// are absent from the dictionary.
    /// </returns>
    Task<Dictionary<string, DownloadWindowCounts>> GetPackageDownloadsAsync(
        string packageName,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns top packages ordered by download count over a rolling window of
    /// <paramref name="windowDays"/> days, with pagination.
    /// </summary>
    /// <remarks>
    /// Uses the same read-model contract as <see cref="GetPackageDownloadsAsync"/>:
    /// closed hourly rollups are authoritative; the current open bucket is computed
    /// from raw events and added.  Only <c>status = 'success'</c> raw events are counted.
    /// </remarks>
    /// <param name="windowDays">
    /// The rolling window in days.  Must be positive (≥ 1).  The window starts at
    /// <c>now - windowDays days</c> and ends at <c>now</c>.
    /// </param>
    /// <param name="page">The 1-based page index.</param>
    /// <param name="pageSize">The number of entries per page.</param>
    /// <param name="now">
    /// The reference "current" time used to compute the window cutoff and open-bucket boundary.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A tuple of (<c>entries</c>, <c>totalCount</c>): <c>entries</c> is the page of
    /// <see cref="TopPackageDownloadsEntry"/> items ordered by <c>downloads</c> descending;
    /// <c>totalCount</c> is the total number of distinct packages with downloads in the window.
    /// </returns>
    Task<(List<TopPackageDownloadsEntry> Entries, int TotalCount)> GetTopPackagesAsync(
        int windowDays,
        int page,
        int pageSize,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregates raw <c>download_events</c> rows whose timestamps fall in fully-closed
    /// hourly and daily buckets (i.e. buckets whose window has completely elapsed before
    /// <paramref name="now"/>) into <c>download_rollup_hourly</c> and
    /// <c>download_rollup_daily</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Closed-bucket only.</b>  Only events with <c>ts &lt; currentHourStart</c>
    /// (resp. <c>ts &lt; currentDayStart</c>) are included.  The current open bucket is
    /// excluded to prevent partial counts from being written and later double-counted.
    /// </para>
    /// <para>
    /// <b>Idempotent (skip-already-rolled).</b>  For each closed bucket key
    /// <c>(package_name, version, bucket_start)</c>, if a row already exists in the
    /// rollup table it is left untouched — only absent buckets are inserted.  Running
    /// this method twice over the same data set produces the same result as running it
    /// once.  This is safe under retention: once a bucket is rolled up, the raw events
    /// for that bucket can later be deleted without causing the rollup to be corrupted
    /// on a re-run.
    /// </para>
    /// <para>
    /// Only events with status <see cref="DownloadEventStatus.Success"/> are counted.
    /// </para>
    /// </remarks>
    /// <param name="now">
    /// The reference "current" time used to determine which buckets are closed.  The
    /// current open hour bucket is <c>truncate(<paramref name="now"/>, hour)</c>;
    /// events earlier than this are eligible for rollup.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of new rollup rows inserted (across both tables).</returns>
    Task<int> RollupAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes raw <c>download_events</c> rows whose timestamp is older than
    /// <paramref name="retentionDays"/> days before <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <paramref name="retentionDays"/> is <c>0</c> or negative, this method is a
    /// no-op (returns <c>0</c>) — <c>0</c> means raw capture is disabled, not "delete
    /// everything".  Never computes a cutoff of <c>now - 0d</c>, which would delete
    /// all historical rows.
    /// </para>
    /// <para>
    /// Only raw <c>download_events</c> rows are deleted — the rollup tables are
    /// permanent and are never touched by this sweep.
    /// </para>
    /// </remarks>
    /// <param name="retentionDays">
    /// How many days of raw events to keep.  <c>0</c> or negative ⇒ no-op.
    /// </param>
    /// <param name="now">
    /// The reference "current" time used to compute the deletion cutoff
    /// (<c>now - retentionDays</c>).
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of rows deleted, or <c>0</c> for the no-op case.</returns>
    Task<int> SweepRetentionAsync(int retentionDays, DateTime now, CancellationToken cancellationToken = default);
}
