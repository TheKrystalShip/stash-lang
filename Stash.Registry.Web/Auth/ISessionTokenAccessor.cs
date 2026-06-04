using System.Threading;
using System.Threading.Tasks;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Per-request scoped accessor that resolves the current user's <see cref="BffSession"/>
/// from the incoming request's session cookie and surfaces the publish JWT to
/// <c>IAuthenticatedRegistryClient</c> (A2).
/// </summary>
/// <remarks>
/// This is the single chokepoint through which every authenticated registry call retrieves
/// its bearer token. The accessor <em>does not</em> expose the raw JWT to callers outside
/// the authed client — it returns only what the client construction needs.
/// </remarks>
public interface ISessionTokenAccessor
{
    /// <summary>
    /// Returns <see langword="true"/> and sets <paramref name="session"/> to the current
    /// <see cref="BffSession"/> if an active session is present for this request.
    /// Returns <see langword="false"/> (and <paramref name="session"/> = <see langword="null"/>)
    /// if no session cookie is present or the session has expired.
    /// </summary>
    bool TryGetSession(out BffSession? session);

    /// <summary>
    /// Returns the publish JWT for the current session.
    /// Throws <see cref="NoActiveSessionException"/> if no active session is present.
    /// This is the fail-closed backstop invoked by <c>IAuthenticatedRegistryClient</c> (A2).
    /// </summary>
    Task<string> GetPublishTokenAsync(CancellationToken cancellationToken = default);
}
