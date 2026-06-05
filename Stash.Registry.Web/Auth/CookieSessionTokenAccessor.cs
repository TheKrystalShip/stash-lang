using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Cookie-backed implementation of <see cref="ISessionTokenAccessor"/>.
/// Reads the <see cref="BffSession"/> from <see cref="HttpContext.Items"/> (populated by
/// <see cref="SessionCookieAuthenticationHandler"/> during authentication) and surfaces the
/// publish JWT to <c>IAuthenticatedRegistryClient</c> (A2).
/// </summary>
/// <remarks>
/// <para>
/// This class is registered as <c>Scoped</c> — one instance per HTTP request.
/// </para>
/// <para>
/// <see cref="TryGetSession"/> is a pure synchronous read of <see cref="HttpContext.Items"/>:
/// the session lookup already happened — naturally async — inside the authentication handler.
/// This design is safe for distributed session stores (Redis/SQL/Postgres) because the
/// store call is never on the request-handling thread, eliminating the sync-over-async
/// deadlock vector that would arise from calling the store here via
/// <c>.GetAwaiter().GetResult()</c>.
/// </para>
/// </remarks>
public sealed class CookieSessionTokenAccessor : ISessionTokenAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISessionStore _sessionStore;

    // Per-request cache for GetPublishTokenAsync: null = not yet resolved.
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
        // The authentication handler already resolved the session from the store (truly async)
        // and stored it in HttpContext.Items. Read it synchronously — no store call needed here.
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null
            && httpContext.Items.TryGetValue(SessionCookie.SessionItemsKey, out var raw)
            && raw is BffSession s)
        {
            session = s;
            return true;
        }

        session = null;
        return false;
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
