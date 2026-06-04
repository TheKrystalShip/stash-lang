using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// In-memory implementation of <see cref="ISessionStore"/>.
/// Suitable for single-instance v1 deployments. For multi-instance hosting, replace
/// with a distributed store (Redis, SQL Server, Postgres) — the <see cref="ISessionStore"/>
/// interface is the swap point; no other code changes are required.
/// </summary>
/// <remarks>
/// TTL eviction is lazy: stale entries are removed on <see cref="GetAsync"/> if they have
/// expired. A background compaction sweep is not included in v1 to keep the implementation
/// simple; memory growth is bounded by the session lifetime (8h default) times concurrent sessions.
/// </remarks>
public sealed class InMemorySessionStore : ISessionStore
{
    private sealed record Entry(BffSession Session, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _sessions = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<BffSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
                return Task.FromResult<BffSession?>(entry.Session);

            // Lazy TTL eviction: remove the expired entry.
            _sessions.TryRemove(sessionId, out _);
        }

        return Task.FromResult<BffSession?>(null);
    }

    /// <inheritdoc/>
    public Task SetAsync(string sessionId, BffSession session, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        _sessions[sessionId] = new Entry(session, expiresAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
