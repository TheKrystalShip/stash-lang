using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stash.Registry.Configuration;
using Stash.Registry.Database;

namespace Stash.Registry.Services;

/// <summary>
/// Hosted background service that runs a nightly retention sweep deleting
/// <c>audit_log</c> entries older than <c>Audit.RetentionDays</c> days.
/// </summary>
/// <remarks>
/// <para>
/// When <c>RetentionDays = 0</c> (the default) the sweep is permanently disabled —
/// audit entries accumulate indefinitely. This is the safe default for a compliance
/// log whose retention obligation is unknown at install time.
/// </para>
/// <para>
/// <b>Separate knob.</b> This sweep is independent of
/// <see cref="MetricsBackgroundService"/>'s raw-event sweep.  Two sweeps; two
/// config keys; two independent cadences.
/// </para>
/// <para>
/// <b>Scope safety.</b> This service is a singleton (hosted services are always
/// singletons in ASP.NET Core). It holds an <see cref="IServiceScopeFactory"/>
/// rather than a scoped <see cref="IRegistryDatabase"/> directly. A fresh DI scope
/// is created per sweep cycle so each cycle gets its own <c>DbContext</c> and the
/// scope is properly disposed afterward.
/// </para>
/// <para>
/// <b>Deferred first sweep.</b> The first pass is scheduled one full day after
/// startup (not on the first tick). The dotnet-getdocument OpenAPI tool starts the
/// full host briefly at build time; an immediate DB query against a schema that may
/// not yet exist would break <c>dotnet build</c>. Deferring is also operationally
/// correct — nightly sweeps run once per day, not at cold-start.
/// </para>
/// </remarks>
public sealed class AuditBackgroundService : BackgroundService
{
    /// <summary>How long the service loop waits between checks.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RegistryConfig _config;
    private readonly ILogger<AuditBackgroundService> _logger;

    // Next scheduled run time — set to one day from now so the first sweep fires
    // tomorrow, not at startup (mirrors MetricsBackgroundService._nextRetentionAt).
    private DateTime _nextSweepAt;

    /// <summary>
    /// Initialises the background service.
    /// </summary>
    /// <param name="scopeFactory">
    /// Used to create a fresh DI scope per sweep cycle, ensuring the scoped
    /// <see cref="IRegistryDatabase"/> / <c>DbContext</c> is properly isolated.
    /// </param>
    /// <param name="config">
    /// The registry configuration singleton; provides
    /// <c>Audit.RetentionDays</c> without a separate config injection.
    /// </param>
    /// <param name="logger">Logger for sweep activity and error reporting.</param>
    public AuditBackgroundService(
        IServiceScopeFactory scopeFactory,
        RegistryConfig config,
        ILogger<AuditBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;

        // Defer the first sweep by one day — matching MetricsBackgroundService's
        // pattern.  This avoids a DB hit during dotnet-getdocument build-time probing
        // and is operationally correct (nightly, not startup).
        _nextSweepAt = DateTime.UtcNow + TimeSpan.FromDays(1);
    }

    /// <summary>
    /// The main loop.  Runs until <paramref name="stoppingToken"/> is cancelled.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuditBackgroundService started — nightly audit-log retention sweep enabled.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPeriodicSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuditBackgroundService: error during pass — will retry.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("AuditBackgroundService stopped.");
    }

    /// <summary>
    /// Fires the retention sweep when its schedule window has elapsed.
    /// Opens its own DI scope so the scoped <see cref="IRegistryDatabase"/> is
    /// properly isolated from any request scope.
    /// </summary>
    internal async Task RunPeriodicSweepAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        if (now >= _nextSweepAt)
        {
            await RunRetentionSweepAsync(now, cancellationToken);
            // Reschedule for the next day.
            _nextSweepAt = now + TimeSpan.FromDays(1);
        }
    }

    /// <summary>
    /// Runs the nightly audit-entry retention sweep inside a fresh DI scope.
    /// </summary>
    /// <param name="now">The reference "current time" for computing the cutoff.</param>
    /// <param name="cancellationToken">A token to cancel the DB operation.</param>
    internal async Task RunRetentionSweepAsync(DateTime now, CancellationToken cancellationToken)
    {
        int retentionDays = _config.Audit.RetentionDays;
        if (retentionDays <= 0)
        {
            _logger.LogDebug(
                "AuditBackgroundService: retention sweep skipped — RetentionDays={Days} (0 = never delete).",
                retentionDays);
            return;
        }

        try
        {
            var cutoff = now - TimeSpan.FromDays(retentionDays);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            var deleted = await db.DeleteAuditEntriesOlderThanAsync(cutoff);

            if (deleted > 0)
                _logger.LogInformation(
                    "AuditBackgroundService: retention sweep deleted {Count} audit entry(ies) older than {Days}d.",
                    deleted, retentionDays);
            else
                _logger.LogDebug("AuditBackgroundService: retention sweep — no stale audit entries found.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AuditBackgroundService: error during retention sweep — will retry next day.");
        }
    }
}
