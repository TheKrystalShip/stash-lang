using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Named HTTP client used by <see cref="LogoutService"/> to revoke the publish token.
/// This client is listed alongside <see cref="LoginHttpClients"/> as part of the explicit
/// allowlist for the chokepoint scan in A2 (<c>AuthClientChokepointMetaTests</c>).
/// </summary>
public static class LogoutHttpClients
{
    /// <summary>
    /// Named client used by <see cref="LogoutService"/> to call
    /// <c>DELETE /api/v1/auth/tokens/{tokenId}</c>.
    /// The publish JWT is attached per-call inside <see cref="LogoutService.LogoutAsync"/>.
    /// </summary>
    public const string AuthRevoke = "AuthRevoke";
}

/// <summary>
/// Owns the complete logout workflow for the BFF.
/// </summary>
/// <remarks>
/// <para>
/// <b>Logout pipeline:</b>
/// <list type="number">
///   <item>Read the session from <see cref="ISessionStore"/> via the session cookie.</item>
///   <item>Call <c>DELETE /api/v1/auth/tokens/{publishTokenId}</c> with the publish JWT
///   as bearer (best-effort — success and 4xx both proceed; a failed revoke logs a warning
///   but never blocks the cookie clear).</item>
///   <item>Remove the session from <see cref="ISessionStore"/>.</item>
///   <item>Clear the session cookie unconditionally.</item>
/// </list>
/// </para>
/// <para>
/// The cookie is cleared unconditionally: a failed revoke must not leave the user
/// "logged in" client-side. The publish token's natural expiry (== session lifetime)
/// caps the worst-case window for an un-revoked token.
/// </para>
/// </remarks>
public sealed class LogoutService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISessionStore _sessionStore;
    private readonly ILogger<LogoutService> _logger;

    public LogoutService(
        IHttpClientFactory httpClientFactory,
        ISessionStore sessionStore,
        ILogger<LogoutService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    /// <summary>
    /// Logs out the current user: best-effort revokes the publish token, removes the
    /// server-side session, and clears the session cookie.
    /// Always succeeds from the user's perspective — cookie is cleared regardless of
    /// whether the registry revoke call succeeded.
    /// </summary>
    public async Task LogoutAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var sessionId = httpContext.Request.Cookies[SessionCookie.CookieName];

        if (!string.IsNullOrEmpty(sessionId))
        {
            var session = await _sessionStore.GetAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);

            if (session is not null)
            {
                // ── Best-effort revoke ─────────────────────────────────────────
                await TryRevokeTokenAsync(session, cancellationToken).ConfigureAwait(false);

                // ── Remove server-side session ────────────────────────────────
                await _sessionStore.RemoveAsync(sessionId, cancellationToken).ConfigureAwait(false);
            }
        }

        // ── Clear cookie unconditionally ──────────────────────────────────────
        httpContext.Response.Cookies.Delete(SessionCookie.CookieName);
    }

    private async Task TryRevokeTokenAsync(BffSession session, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(LogoutHttpClients.AuthRevoke);
            // Thread the publish JWT — ONLY place it is used for revocation.
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", session.PublishTokenJwt);

            var response = await client
                .DeleteAsync($"/api/v1/auth/tokens/{Uri.EscapeDataString(session.PublishTokenId)}", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode &&
                (int)response.StatusCode >= 500)
            {
                _logger.LogWarning(
                    "DELETE /api/v1/auth/tokens/{TokenId} returned {StatusCode} — token may not be revoked",
                    session.PublishTokenId,
                    response.StatusCode);
            }
            // 4xx (e.g. 404 — token already expired/revoked) are silently accepted.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to revoke publish token {TokenId} during logout — proceeding without revoke",
                session.PublishTokenId);
        }
    }
}
