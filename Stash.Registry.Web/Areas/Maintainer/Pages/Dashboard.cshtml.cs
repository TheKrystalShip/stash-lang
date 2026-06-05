using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Services;

namespace Stash.Registry.Web.Areas.Maintainer.Pages;

/// <summary>
/// Page model for <c>GET /dashboard</c>.
/// </summary>
/// <remarks>
/// <para>
/// Calls <see cref="IAuthenticatedRegistryClient.SearchOwnedAsync"/> with the session username
/// as the <c>owner</c> filter and <see cref="PackageSortOrder.Updated"/> as the sort order.
/// The authenticated client threads the publish JWT, so the registry's visibility predicate
/// includes the user's private packages alongside public ones.
/// </para>
/// <para>
/// No client-side visibility branching: this page renders whatever the authenticated client
/// returns (C5 — no visibility logic in BFF page models).
/// </para>
/// <para>
/// <b>C4 — error mapping:</b> A registry 401 clears the server-side session and cookie,
/// then redirects to <c>/login?expired=1</c>.
/// </para>
/// </remarks>
public sealed class DashboardModel : PageModel
{
    private readonly IAuthenticatedRegistryClient _authClient;
    private readonly ISessionTokenAccessor _sessionTokenAccessor;
    private readonly ISessionStore _sessionStore;

    // ── Named query-string keys (bounded-domain constants) ────────────────────

    /// <summary>Query-string key used on the <c>/login</c> redirect when a session expires.</summary>
    public const string ExpiredQueryKey = "expired";

    /// <summary>Query-string value for the expired-session redirect.</summary>
    public const string ExpiredQueryValue = "1";

    public DashboardModel(
        IAuthenticatedRegistryClient authClient,
        ISessionTokenAccessor sessionTokenAccessor,
        ISessionStore sessionStore)
    {
        _authClient = authClient;
        _sessionTokenAccessor = sessionTokenAccessor;
        _sessionStore = sessionStore;
    }

    /// <summary>Packages owned by the authenticated user (including private ones).</summary>
    public PagedResponse<PackageSummaryResponse>? Packages { get; private set; }

    /// <summary>Registry error message, or <see langword="null"/> if the registry is reachable.</summary>
    public string? RegistryError { get; private set; }

    /// <summary>Current page number (1-based).</summary>
    public int CurrentPage { get; private set; } = 1;

    public async Task<IActionResult> OnGetAsync(int page = 1, CancellationToken cancellationToken = default)
    {
        CurrentPage = page < 1 ? 1 : page;

        // The session username is always available (the [Authorize] convention + auth handler
        // guarantees User.Identity.Name is set before this page model runs).
        var username = User.Identity!.Name!;

        var query = new SearchQuery
        {
            owner = username,
            sort = PackageSortOrder.Updated,
            page = CurrentPage,
        };

        try
        {
            Packages = await _authClient.SearchOwnedAsync(query, cancellationToken)
                .ConfigureAwait(false);
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

    // ── Private helpers ───────────────────────────────────────────────────────

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
