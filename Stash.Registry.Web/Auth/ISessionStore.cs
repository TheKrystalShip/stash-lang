using System;
using System.Threading;
using System.Threading.Tasks;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Server-side session storage for BFF sessions.
/// The default implementation is <see cref="InMemorySessionStore"/> (suitable for
/// single-instance deployments). Replace with a Redis- or Postgres-backed implementation
/// for multi-instance hosting without code changes.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Retrieves the session associated with <paramref name="sessionId"/>.
    /// Returns <see langword="null"/> if the session does not exist or has expired.
    /// </summary>
    Task<BffSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists or overwrites the session for <paramref name="sessionId"/>.
    /// The session will be evicted automatically at <paramref name="expiresAt"/>.
    /// </summary>
    Task SetAsync(string sessionId, BffSession session, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the session for <paramref name="sessionId"/>.
    /// A no-op if the session does not exist.
    /// </summary>
    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}
