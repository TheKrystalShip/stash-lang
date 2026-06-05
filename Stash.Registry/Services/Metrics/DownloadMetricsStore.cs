using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
}
