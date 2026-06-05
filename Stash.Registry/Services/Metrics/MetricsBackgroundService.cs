using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stash.Registry.Configuration;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Services.Metrics;

/// <summary>
/// Hosted background service that:
/// <list type="bullet">
///   <item><description>Drains <see cref="IDownloadEventQueue"/> and persists raw events to <c>download_events</c> in batches.</description></item>
///   <item><description>Runs a periodic hourly-rollup pass aggregating closed buckets into <c>download_rollup_hourly</c> and <c>download_rollup_daily</c>.</description></item>
///   <item><description>Runs a nightly retention sweep deleting raw <c>download_events</c> rows older than <c>Raw.RetentionDays</c>.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// This is the registry's first <see cref="BackgroundService"/> and serves as the
/// canonical pattern for off-request async work.
/// </para>
/// <para>
/// <b>Scope safety.</b> This service is a singleton (hosted services are always
/// singletons in ASP.NET Core). It therefore holds an <see cref="IServiceScopeFactory"/>
/// — never a scoped <see cref="IDownloadMetricsStore"/> directly. A fresh DI scope is
/// created per pass so each pass gets its own <c>DbContext</c> and the scope
/// is properly disposed afterward. Never use a request's scoped <c>DbContext</c>
/// (it is disposed when the request ends).
/// </para>
/// <para>
/// <b>Drain loop.</b> The service reads available events from the channel using
/// <see cref="System.Threading.Channels.ChannelReader{T}.TryRead"/> until the channel
/// is empty or the batch ceiling is reached, then flushes to the DB in one
/// <see cref="IDownloadMetricsStore.InsertEventsAsync"/> call. The rollup and retention
/// passes run on a separate schedule tracked by <see cref="_nextRollupAt"/> and
/// <see cref="_nextRetentionAt"/>; they do NOT fire on every 5-second drain tick.
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
    private readonly RegistryConfig _config;
    private readonly ILogger<MetricsBackgroundService> _logger;

    // Next scheduled run times — initialised to UtcNow so the first pass runs promptly.
    private DateTime _nextRollupAt;
    private DateTime _nextRetentionAt;

    /// <summary>
    /// Initialises the background service.
    /// </summary>
    /// <param name="queue">The singleton channel that receives download events from request threads.</param>
    /// <param name="scopeFactory">
    /// Used to create a fresh DI scope per pass, ensuring the scoped
    /// <see cref="DownloadMetricsStore"/> / <c>DbContext</c> is properly isolated.
    /// </param>
    /// <param name="config">
    /// The registry configuration singleton; provides <c>Metrics.Rollup.IntervalMinutes</c>
    /// and <c>Metrics.Raw.RetentionDays</c> without requiring a separate <see cref="MetricsConfig"/>
    /// injection (the type is already registered via <see cref="RegistryConfig"/>).
    /// </param>
    /// <param name="logger">Logger for drain activity and error reporting.</param>
    public MetricsBackgroundService(
        IDownloadEventQueue queue,
        IServiceScopeFactory scopeFactory,
        RegistryConfig config,
        ILogger<MetricsBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;

        // Schedule the first passes to fire after one interval, NOT immediately.
        // The dotnet-getdocument OpenAPI tool starts the full host briefly at build time;
        // firing synchronously on startup would attempt a DB query against a non-existent
        // schema, breaking `dotnet build`. Deferring is also operationally correct —
        // rollup and retention passes run on their configured interval, not at cold-start.
        var now = DateTime.UtcNow;
        _nextRollupAt = now + TimeSpan.FromMinutes(_config.Metrics.Rollup.IntervalMinutes);
        _nextRetentionAt = now + TimeSpan.FromDays(1);
    }

    /// <summary>
    /// The main loop. Runs until <paramref name="stoppingToken"/> is cancelled
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
                await RunPeriodicPassesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — exit the loop.
                break;
            }
            catch (Exception ex)
            {
                // Non-terminal error: log and continue so the service stays alive.
                _logger.LogError(ex, "MetricsBackgroundService: error during pass — will retry.");
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

    /// <summary>
    /// Fires the rollup and retention passes when their respective schedule windows have
    /// elapsed.  Each pass opens its own DI scope.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the DB operations.</param>
    internal async Task RunPeriodicPassesAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        if (now >= _nextRollupAt)
        {
            await RunRollupPassAsync(now, cancellationToken);
            _nextRollupAt = now + TimeSpan.FromMinutes(_config.Metrics.Rollup.IntervalMinutes);
        }

        if (now >= _nextRetentionAt)
        {
            await RunRetentionPassAsync(now, cancellationToken);
            // Retention sweeps run once per day (nightly).
            _nextRetentionAt = now + TimeSpan.FromDays(1);
        }
    }

    /// <summary>
    /// Runs the hourly-rollup pass inside a fresh DI scope.
    /// </summary>
    private async Task RunRollupPassAsync(DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IDownloadMetricsStore>();
            var inserted = await store.RollupAsync(now, cancellationToken);
            if (inserted > 0)
                _logger.LogInformation("MetricsBackgroundService: rollup pass inserted {Count} new rollup row(s).", inserted);
            else
                _logger.LogDebug("MetricsBackgroundService: rollup pass — no new closed buckets to roll up.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MetricsBackgroundService: error during rollup pass — will retry next interval.");
        }
    }

    /// <summary>
    /// Runs the nightly retention sweep inside a fresh DI scope.
    /// </summary>
    private async Task RunRetentionPassAsync(DateTime now, CancellationToken cancellationToken)
    {
        int retentionDays = _config.Metrics.Raw.RetentionDays;
        if (retentionDays <= 0)
        {
            _logger.LogDebug("MetricsBackgroundService: retention sweep skipped — RetentionDays={Days} (raw capture disabled).", retentionDays);
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IDownloadMetricsStore>();
            var deleted = await store.SweepRetentionAsync(retentionDays, now, cancellationToken);
            if (deleted > 0)
                _logger.LogInformation("MetricsBackgroundService: retention sweep deleted {Count} raw event row(s) older than {Days}d.", deleted, retentionDays);
            else
                _logger.LogDebug("MetricsBackgroundService: retention sweep — no stale rows found.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MetricsBackgroundService: error during retention sweep — will retry next day.");
        }
    }
}
