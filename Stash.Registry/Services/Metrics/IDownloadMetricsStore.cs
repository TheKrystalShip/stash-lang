using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
