using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Areas.Maintainer;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Services;

namespace Stash.Registry.Web.Areas.Maintainer.Pages.Settings;

/// <summary>
/// Page model for <c>GET /settings/tokens</c>, <c>POST /settings/tokens</c> (create),
/// and <c>POST /settings/tokens/{id}/revoke</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>C4 — single-use token reveal.</b> The newly-created token value is passed to the browser
/// exactly once via <c>TempData</c> (DataProtection-encrypted, single-use cookie). It is
/// never persisted to <see cref="ISessionStore"/>, never written to a log, and never placed
/// in a normal cookie or in <c>ViewData</c> that survives a redirect.
/// </para>
/// <para>
/// <b>Current-session badge.</b> The row whose <c>TokenId == BffSession.PublishTokenId</c>
/// (surfaced via <see cref="ISessionTokenAccessor"/>) gets a "current session" badge and
/// NO revoke button. Revoking the session token is only possible via <c>POST /logout</c>.
/// </para>
/// <para>
/// <b>C6 — bounded-domain dropdown.</b> The ceiling dropdown is built from
/// <see cref="AllowedCeilings"/> — the <see cref="TokenScopes"/> enum values filtered to
/// <see cref="TokenScopes.Read"/> and <see cref="TokenScopes.Publish"/>.
/// Admin is explicitly excluded; no inline <c>"read"</c>/<c>"publish"</c> literals appear.
/// </para>
/// </remarks>
public sealed class TokensModel : PageModel
{
    private readonly IAuthenticatedRegistryClient _authClient;
    private readonly ISessionTokenAccessor _sessionTokenAccessor;
    private readonly ISessionStore _sessionStore;

    // ── TempData keys (bounded-domain named constants) ────────────────────────

    /// <summary>TempData key used to flow the newly-minted token value once from POST to GET.</summary>
    public const string JustCreatedTokenValueKey = "JustCreatedTokenValue";

    /// <summary>TempData key used to flow the just-created token id (for display pairing).</summary>
    public const string JustCreatedTokenIdKey = "JustCreatedTokenId";

    // ── Query-string keys ─────────────────────────────────────────────────────

    /// <summary>Query-string key used on the <c>/login</c> redirect when a session expires.</summary>
    public const string ExpiredQueryKey = "expired";

    /// <summary>Query-string value for the expired-session redirect.</summary>
    public const string ExpiredQueryValue = "1";

    // ── Ceiling allowed values (named — no inline literals) ───────────────────

    /// <summary>
    /// The closed set of token scope ceiling values surfaced in the v1 create-token dropdown.
    /// Admin is intentionally excluded — admin tooling is out of scope.
    /// </summary>
    public static readonly TokenScopes[] AllowedCeilings =
    [
        TokenScopes.Read,
        TokenScopes.Publish,
    ];

    public TokensModel(
        IAuthenticatedRegistryClient authClient,
        ISessionTokenAccessor sessionTokenAccessor,
        ISessionStore sessionStore)
    {
        _authClient = authClient;
        _sessionTokenAccessor = sessionTokenAccessor;
        _sessionStore = sessionStore;
    }

    // ── Page state ────────────────────────────────────────────────────────────

    /// <summary>All active tokens for the signed-in user.</summary>
    public TokenListResponse? Tokens { get; private set; }

    /// <summary>The <c>tokenId</c> of the current session's publish token. Used to render the "current session" badge.</summary>
    public string? CurrentSessionTokenId { get; private set; }

    /// <summary>
    /// The newly-minted token value, present on the single GET immediately after a successful create.
    /// Read from TempData (consumed exactly once). <see langword="null"/> on all other renders.
    /// </summary>
    public string? JustCreatedTokenValue { get; private set; }

    /// <summary>The token id paired with <see cref="JustCreatedTokenValue"/>.</summary>
    public string? JustCreatedTokenId { get; private set; }

    /// <summary>Success banner message (revoke success), or <see langword="null"/>.</summary>
    public string? SuccessBanner { get; private set; }

    /// <summary>Error banner message (registry 4xx/5xx on a write), or <see langword="null"/>.</summary>
    public string? ErrorBanner { get; private set; }

    /// <summary>Registry error message from loading the token list, or <see langword="null"/>.</summary>
    public string? RegistryError { get; private set; }

    // ── Bound form properties (for POST /settings/tokens) ─────────────────────

    /// <summary>
    /// Token scope ceiling bound from the create form.
    /// Values are constrained to <see cref="AllowedCeilings"/> by the dropdown options;
    /// admin is never surfaced in the UI.
    /// </summary>
    [BindProperty]
    public TokenScopes Ceiling { get; set; } = TokenScopes.Read;

    /// <summary>Optional human-readable description for the new token.</summary>
    [BindProperty]
    public string? Description { get; set; }

    /// <summary>Token lifetime expressed as a duration string (e.g. "30d", "12h").</summary>
    [BindProperty]
    public string ExpiresIn { get; set; } = string.Empty;

    // ── GET handler ───────────────────────────────────────────────────────────

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken = default)
    {
        // Populate current-session token id for the "current session" badge.
        if (_sessionTokenAccessor.TryGetSession(out var session) && session is not null)
        {
            CurrentSessionTokenId = session.PublishTokenId;
        }

        // Consume TempData — this read marks the entry for deletion so it is NOT available on re-load.
        JustCreatedTokenValue = TempData[JustCreatedTokenValueKey] as string;
        JustCreatedTokenId = TempData[JustCreatedTokenIdKey] as string;

        // Load the token list.
        try
        {
            Tokens = await _authClient.ListTokensAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return await ClearSessionAndRedirectToLoginExpiredAsync(cancellationToken);
        }
        catch (RegistryClientException ex)
        {
            RegistryError = ex.ErrorMessage ?? "The registry is temporarily unreachable.";
        }

        return Page();
    }

    // ── POST: create token ────────────────────────────────────────────────────

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken = default)
    {
        // Guard: Ceiling must be one of the allowed values (defense in depth — model binding
        // normally enforces this, but a hand-crafted POST could supply TokenScopes.Admin).
        if (Ceiling == TokenScopes.Admin)
        {
            ErrorBanner = "Admin-ceiling tokens cannot be created from this page.";
            return await ReloadAndPageAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(ExpiresIn))
        {
            ErrorBanner = "A token lifetime is required (e.g. \"30d\", \"12h\").";
            return await ReloadAndPageAsync(cancellationToken);
        }

        var request = new TokenCreateRequest
        {
            Ceiling = Ceiling.ToWire(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            ExpiresIn = ExpiresIn,
        };

        TokenCreateResponse response;
        try
        {
            response = await _authClient.CreateTokenAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return await ClearSessionAndRedirectToLoginExpiredAsync(cancellationToken);
        }
        catch (RegistryClientException ex)
        {
            ErrorBanner = ex.ErrorMessage ?? "Token creation failed. Please try again.";
            return await ReloadAndPageAsync(cancellationToken);
        }

        // Flow the token value exactly once via TempData (DataProtection-encrypted, single-use).
        // NEVER written to session store, ISessionStore, a normal cookie, ViewData, or any log.
        TempData[JustCreatedTokenValueKey] = response.Token;
        TempData[JustCreatedTokenIdKey] = response.TokenId;

        // 303 See Other — the POST-Redirect-GET pattern; 303 forces the follow-up to be a GET
        // regardless of HTTP version. RedirectToPageResult cannot produce 303 (no flag combination),
        // so we set Location + return StatusCode(303) directly.
        // TempData is committed by the SaveTempData filter before the response is written.
        Response.Headers.Location = Url.Page("/Settings/Tokens", new { area = MaintainerAreaConventions.AreaName });
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    // ── POST: revoke token ────────────────────────────────────────────────────

    public async Task<IActionResult> OnPostRevokeAsync(string id, CancellationToken cancellationToken = default)
    {
        // Guard: must not revoke the current session token.
        if (_sessionTokenAccessor.TryGetSession(out var session) &&
            session is not null &&
            string.Equals(id, session.PublishTokenId, StringComparison.Ordinal))
        {
            ErrorBanner = "The current session token cannot be revoked from this page. Use Log out to end the session.";
            return await ReloadAndPageAsync(cancellationToken);
        }

        try
        {
            await _authClient.RevokeTokenAsync(id, cancellationToken).ConfigureAwait(false);
            SuccessBanner = "Token revoked successfully.";
        }
        catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return await ClearSessionAndRedirectToLoginExpiredAsync(cancellationToken);
        }
        catch (RegistryClientException ex)
        {
            ErrorBanner = ex.ErrorMessage ?? "Token revocation failed. Please try again.";
        }

        return await ReloadAndPageAsync(cancellationToken);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Re-loads the token list and returns a Page result (used after write operations to show
    /// the post-mutation state alongside banners).
    /// </summary>
    private async Task<IActionResult> ReloadAndPageAsync(CancellationToken cancellationToken)
    {
        if (_sessionTokenAccessor.TryGetSession(out var session) && session is not null)
        {
            CurrentSessionTokenId = session.PublishTokenId;
        }

        try
        {
            Tokens = await _authClient.ListTokensAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return await ClearSessionAndRedirectToLoginExpiredAsync(cancellationToken);
        }
        catch (RegistryClientException ex)
        {
            RegistryError = ex.ErrorMessage ?? "The registry is temporarily unreachable.";
        }

        return Page();
    }

    /// <summary>
    /// Clears the server-side session and session cookie, then redirects to
    /// <c>/login?expired=1</c> — handles a registry 401 meaning the publish token
    /// was revoked out-of-band.
    /// </summary>
    private async Task<IActionResult> ClearSessionAndRedirectToLoginExpiredAsync(
        CancellationToken cancellationToken)
    {
        var sessionId = Request.Cookies[SessionCookie.CookieName];
        if (!string.IsNullOrEmpty(sessionId))
        {
            await _sessionStore.RemoveAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        Response.Cookies.Delete(SessionCookie.CookieName);

        return Redirect($"/login?{ExpiredQueryKey}={ExpiredQueryValue}");
    }
}
