using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Cookie-backed implementation of <see cref="ISessionTokenAccessor"/>.
/// Reads the session cookie from the current HTTP context, looks up the
/// <see cref="BffSession"/> in <see cref="ISessionStore"/>, and surfaces the
/// publish JWT to <c>IAuthenticatedRegistryClient</c> (A2).
/// </summary>
/// <remarks>
/// This class is registered as <c>Scoped</c> — one instance per HTTP request.
/// The session lookup result is cached per-instance so multiple calls within the
/// same request do not hit the store multiple times.
/// </remarks>
public sealed class CookieSessionTokenAccessor : ISessionTokenAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISessionStore _sessionStore;

    // Per-request cache: null = not yet resolved; (null, false) = resolved but no session.
    private bool _resolved;
    private BffSession? _cachedSession;

    public CookieSessionTokenAccessor(IHttpContextAccessor httpContextAccessor, ISessionStore sessionStore)
    {
        _httpContextAccessor = httpContextAccessor;
        _sessionStore = sessionStore;
    }

    /// <inheritdoc/>
    public bool TryGetSession(out BffSession? session)
    {
        if (!_resolved)
        {
            // Synchronously resolve: we need the result now for DI factory decisions.
            // The store is in-memory (Task.CompletedTask), so .GetAwaiter().GetResult() is safe.
            _cachedSession = ResolveSessionAsync().GetAwaiter().GetResult();
            _resolved = true;
        }

        session = _cachedSession;
        return session is not null;
    }

    /// <inheritdoc/>
    public async Task<string> GetPublishTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!_resolved)
        {
            _cachedSession = await ResolveSessionAsync().ConfigureAwait(false);
            _resolved = true;
        }

        if (_cachedSession is null)
            throw new NoActiveSessionException();

        return _cachedSession.PublishTokenJwt;
    }

    private async Task<BffSession?> ResolveSessionAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
            return null;

        var sessionId = httpContext.Request.Cookies[SessionCookie.CookieName];
        if (string.IsNullOrEmpty(sessionId))
            return null;

        return await _sessionStore.GetAsync(sessionId).ConfigureAwait(false);
    }
}
