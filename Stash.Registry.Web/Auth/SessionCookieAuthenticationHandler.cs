using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// ASP.NET Core authentication handler for the <c>"BffCookie"</c> scheme.
/// </summary>
/// <remarks>
/// <para>
/// <b>Role in the auth pipeline:</b> this is the <em>user-facing auth gate</em>.
/// On each request it reads the session cookie (<see cref="SessionCookie.CookieName"/>),
/// looks up the associated <see cref="BffSession"/> in <see cref="ISessionStore"/>, and
/// populates <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/> with a
/// <see cref="ClaimsPrincipal"/> carrying <c>Name = session.Username</c>.
/// </para>
/// <para>
/// A missing, expired, or unknown session leaves
/// <c>User.Identity.IsAuthenticated == false</c>, which causes the standard
/// <c>[Authorize]</c> middleware to invoke <see cref="ChallengeAsync"/> — this
/// handler redirects to <c>/login?returnUrl=&lt;original&gt;</c> (never 401 / never a body).
/// </para>
/// <para>
/// The <c>IAuthenticatedRegistryClient</c> DI factory's <see cref="NoActiveSessionException"/>
/// throw (A2) is the fail-closed backstop — it should never fire on a normal anonymous request
/// because <c>[Authorize]</c> 302s before the page model is constructed.
/// </para>
/// </remarks>
public sealed class SessionCookieAuthenticationHandler
    : AuthenticationHandler<SessionCookieAuthenticationOptions>
{
    private readonly ISessionStore _sessionStore;

    public SessionCookieAuthenticationHandler(
        IOptionsMonitor<SessionCookieAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISessionStore sessionStore)
        : base(options, logger, encoder)
    {
        _sessionStore = sessionStore;
    }

    /// <summary>
    /// Reads the session cookie, looks up the session in <see cref="ISessionStore"/>, and
    /// builds an authenticated <see cref="ClaimsPrincipal"/> if a valid session exists.
    /// </summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sessionId = Request.Cookies[SessionCookie.CookieName];
        if (string.IsNullOrEmpty(sessionId))
            return AuthenticateResult.NoResult();

        var session = await _sessionStore.GetAsync(sessionId).ConfigureAwait(false);
        if (session is null)
            return AuthenticateResult.NoResult();

        // Store the resolved session in HttpContext.Items so that
        // CookieSessionTokenAccessor.TryGetSession can read it synchronously,
        // avoiding any sync-over-async call to ISessionStore on the request thread.
        Context.Items[SessionCookie.SessionItemsKey] = session;

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, session.Username),
        };

        var identity = new ClaimsIdentity(claims, SessionCookie.AuthScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SessionCookie.AuthScheme);

        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// Redirects anonymous requests to <c>/login?returnUrl=&lt;original&gt;</c>.
    /// Never returns a 401 or a body — browser-based UIs receive a redirect.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Build the return URL from the original request path + query.
        var returnUrl = Request.Path;
        if (Request.QueryString.HasValue)
            returnUrl = returnUrl + Request.QueryString;

        var redirectUri = $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        Response.Redirect(redirectUri);

        return Task.CompletedTask;
    }

}
