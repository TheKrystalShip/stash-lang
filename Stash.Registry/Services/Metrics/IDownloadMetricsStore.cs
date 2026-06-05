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
}
