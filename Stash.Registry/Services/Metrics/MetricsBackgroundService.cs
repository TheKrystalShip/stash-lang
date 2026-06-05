using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Services.Metrics;

/// <summary>
/// Hosted background service that drains <see cref="IDownloadEventQueue"/> and persists
/// events to the <c>download_events</c> table in batches.
/// </summary>
/// <remarks>
/// <para>
/// This is the registry's first <see cref="BackgroundService"/> and serves as the
/// canonical pattern for off-request async work. It is designed for future extension
/// to host the M4 hourly-rollup pass and nightly-retention sweep in the same loop.
/// </para>
/// <para>
/// <b>Scope safety.</b> This service is a singleton (hosted services are always
/// singletons in ASP.NET Core). It therefore holds an <see cref="IServiceScopeFactory"/>
/// — never a scoped <see cref="IDownloadMetricsStore"/> directly. A fresh DI scope is
/// created per drain batch so each batch gets its own <c>DbContext</c> and the scope
/// is properly disposed afterward. Never use a request's scoped <c>DbContext</c>
/// (it is disposed when the request ends).
/// </para>
/// <para>
/// <b>Drain loop.</b> The service reads available events from the channel using
/// <see cref="System.Threading.Channels.ChannelReader{T}.TryRead"/> until the channel
/// is empty or the batch ceiling is reached, then flushes to the DB in one
/// <see cref="IDownloadMetricsStore.InsertEventsAsync"/> call. It then waits briefly
/// before the next drain pass.
/// </para>
/// </remarks>
public sealed class MetricsBackgroundService : BackgroundService
{
    /// <summary>Maximum number of events flushed to the DB in a single batch.</summary>
    public const int BatchSize = 100;

    /// <summary>How long the drain loop waits between passes when the channel is empty.</summary>
    public static readonly TimeSpan DrainInterval = TimeSpan.FromSeconds(5);

    private readonly IDownloadEventQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetricsBackgroundService> _logger;

    /// <summary>
    /// Initialises the background service.
    /// </summary>
    /// <param name="queue">The singleton channel that receives download events from request threads.</param>
    /// <param name="scopeFactory">
    /// Used to create a fresh DI scope per drain batch, ensuring the scoped
    /// <see cref="DownloadMetricsStore"/> / <c>DbContext</c> is properly isolated.
    /// </param>
    /// <param name="logger">Logger for drain activity and error reporting.</param>
    public MetricsBackgroundService(
        IDownloadEventQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<MetricsBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// The main drain loop. Runs until <paramref name="stoppingToken"/> is cancelled
    /// (i.e. when the host is shutting down).
    /// </summary>
    /// <param name="stoppingToken">
    /// Signals that the host is stopping; the loop exits cleanly on cancellation.
    /// </param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MetricsBackgroundService started — draining download-event queue.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — exit the loop.
                break;
            }
            catch (Exception ex)
            {
                // Non-terminal error: log and continue so the service stays alive.
                _logger.LogError(ex, "MetricsBackgroundService: error during drain batch — will retry.");
            }

            // Wait before next pass, yielding CPU when the channel is idle.
            try
            {
                await Task.Delay(DrainInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        // Final drain on shutdown — attempt to flush remaining events before exiting.
        try
        {
            await DrainBatchAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MetricsBackgroundService: final drain on shutdown encountered an error.");
        }

        _logger.LogInformation("MetricsBackgroundService stopped.");
    }

    /// <summary>
    /// Reads up to <see cref="BatchSize"/> events from the channel and writes them to
    /// the DB inside a fresh DI scope.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the DB write.</param>
    internal async Task DrainBatchAsync(CancellationToken cancellationToken)
    {
        var batch = new List<DownloadEvent>(BatchSize);

        while (batch.Count < BatchSize && _queue.Reader.TryRead(out var ev))
        {
            batch.Add(ev);
        }

        if (batch.Count == 0)
            return;

        var records = new List<DownloadEventRecord>(batch.Count);
        foreach (var ev in batch)
        {
            records.Add(new DownloadEventRecord
            {
                PackageName = ev.PackageName,
                Version = ev.Version,
                Ts = ev.Ts,
                Ip = ev.Ip,
                UserAgent = ev.UserAgent,
                Status = DownloadEventStatus.Success,
                BytesServed = ev.BytesServed,
                RequesterUser = ev.RequesterUser,
            });
        }

        // Open a fresh DI scope so the scoped DownloadMetricsStore / DbContext is
        // properly isolated from any request scope (which may already be disposed).
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDownloadMetricsStore>();
        await store.InsertEventsAsync(records, cancellationToken);

        _logger.LogDebug("MetricsBackgroundService: persisted {Count} download event(s).", records.Count);
    }
}
