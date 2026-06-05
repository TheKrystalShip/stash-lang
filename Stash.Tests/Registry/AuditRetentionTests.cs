using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Unit tests for the audit-log retention sweep:
/// <list type="bullet">
///   <item><description><see cref="IRegistryDatabase.DeleteAuditEntriesOlderThanAsync"/> — the DB-layer deletion method.</description></item>
///   <item><description><see cref="AuditBackgroundService.RunRetentionSweepAsync"/> — the service-level policy (no-op when RetentionDays=0, deletes stale entries when RetentionDays&gt;0).</description></item>
/// </list>
/// </summary>
public sealed class AuditRetentionTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly RegistryDbContext _db;

    public AuditRetentionTests()
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

    private StashRegistryDatabase Database() => new(_db);

    private async Task SeedAuditEntryAsync(DateTime timestamp)
    {
        _db.AuditLog.Add(new AuditEntry
        {
            Action = "package.publish",
            Package = "@a/foo",
            User = "alice",
            Decision = "allow",
            Timestamp = timestamp,
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Builds an <see cref="AuditBackgroundService"/> that uses the test's in-memory
    /// DbContext via a fake scope factory.
    /// </summary>
    private AuditBackgroundService BuildService(int retentionDays)
    {
        var config = new RegistryConfig
        {
            Audit = new AuditConfig { RetentionDays = retentionDays }
        };

        // Wrap the already-open DbContext in a scope factory so the service's
        // CreateAsyncScope() call resolves IRegistryDatabase against it.
        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddScoped<IRegistryDatabase>(_ => new StashRegistryDatabase(_db));
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new AuditBackgroundService(
            scopeFactory,
            config,
            NullLogger<AuditBackgroundService>.Instance);
    }

    // ── DeleteAuditEntriesOlderThanAsync — DB method ───────────────────────────

    [Fact]
    public async Task DeleteAuditEntriesOlderThanAsync_NoCutoffMatch_ReturnsZero()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
        var cutoff = now - TimeSpan.FromDays(30);

        // All entries are fresh (within the retention window).
        await SeedAuditEntryAsync(now - TimeSpan.FromDays(10));
        await SeedAuditEntryAsync(now - TimeSpan.FromDays(29));

        var deleted = await Database().DeleteAuditEntriesOlderThanAsync(cutoff);

        Assert.Equal(0, deleted);
        Assert.Equal(2, await _db.AuditLog.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task DeleteAuditEntriesOlderThanAsync_StaleEntries_AreDeleted()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
        var cutoff = now - TimeSpan.FromDays(30);

        // Stale (before cutoff).
        await SeedAuditEntryAsync(now - TimeSpan.FromDays(40));
        await SeedAuditEntryAsync(now - TimeSpan.FromDays(35));

        // Fresh (at or after cutoff).
        await SeedAuditEntryAsync(cutoff);
        await SeedAuditEntryAsync(now - TimeSpan.FromDays(5));

        var deleted = await Database().DeleteAuditEntriesOlderThanAsync(cutoff);

        Assert.Equal(2, deleted);
        Assert.Equal(2, await _db.AuditLog.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task DeleteAuditEntriesOlderThanAsync_ExactlyAtCutoff_NotDeleted()
    {
        // Strict less-than: Timestamp < cutoff. Exactly-at-cutoff is kept.
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
        var cutoff = now - TimeSpan.FromDays(30);

        await SeedAuditEntryAsync(cutoff);                               // at boundary — keep
        await SeedAuditEntryAsync(cutoff - TimeSpan.FromSeconds(1));     // just before — delete

        var deleted = await Database().DeleteAuditEntriesOlderThanAsync(cutoff);

        Assert.Equal(1, deleted);
        var remaining = await _db.AuditLog.AsNoTracking().SingleAsync();
        Assert.Equal(cutoff, remaining.Timestamp);
    }

    [Fact]
    public async Task DeleteAuditEntriesOlderThanAsync_EmptyTable_ReturnsZero()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromDays(30);
        var deleted = await Database().DeleteAuditEntriesOlderThanAsync(cutoff);
        Assert.Equal(0, deleted);
    }

    // ── AuditBackgroundService.RunRetentionSweepAsync — policy layer ───────────

    [Fact]
    public async Task RetentionSweep_RetentionDaysZero_IsNoOp()
    {
        // RetentionDays=0 → never delete; all entries must survive.
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        await SeedAuditEntryAsync(now - TimeSpan.FromDays(40));
        await SeedAuditEntryAsync(now - TimeSpan.FromDays(60));

        var service = BuildService(retentionDays: 0);
        await service.RunRetentionSweepAsync(now, CancellationToken.None);

        // Both entries must still be present.
        Assert.Equal(2, await _db.AuditLog.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task RetentionSweep_RetentionDays30_Deletes40DayOldEntry_KeepsNowEntry()
    {
        // The primary done_when case: RetentionDays=30, 40-day-old entry is deleted,
        // now-stamped entry is retained.
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        await SeedAuditEntryAsync(now - TimeSpan.FromDays(40)); // stale — must be deleted
        await SeedAuditEntryAsync(now);                          // fresh — must be retained

        var service = BuildService(retentionDays: 30);
        await service.RunRetentionSweepAsync(now, CancellationToken.None);

        var remaining = await _db.AuditLog.AsNoTracking().ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(now, remaining[0].Timestamp);
    }

    [Fact]
    public async Task RetentionSweep_RetentionDaysNegative_IsNoOp()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
        await SeedAuditEntryAsync(now - TimeSpan.FromDays(365));

        var service = BuildService(retentionDays: -1);
        await service.RunRetentionSweepAsync(now, CancellationToken.None);

        Assert.Equal(1, await _db.AuditLog.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task RetentionSweep_RetentionDays30_AllFresh_NothingDeleted()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        await SeedAuditEntryAsync(now - TimeSpan.FromDays(5));
        await SeedAuditEntryAsync(now - TimeSpan.FromDays(15));
        await SeedAuditEntryAsync(now - TimeSpan.FromDays(29));

        var service = BuildService(retentionDays: 30);
        await service.RunRetentionSweepAsync(now, CancellationToken.None);

        Assert.Equal(3, await _db.AuditLog.AsNoTracking().CountAsync());
    }
}
