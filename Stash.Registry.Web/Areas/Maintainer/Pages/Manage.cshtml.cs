using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Services;

namespace Stash.Registry.Web.Areas.Maintainer.Pages;

/// <summary>
/// Page model for <c>GET /manage/@{scope}/{name}</c> — the owned-package management page.
/// </summary>
/// <remarks>
/// <para>
/// Loads <see cref="PackageDetailResponse"/> via <see cref="IAuthenticatedRegistryClient.GetPackageAsync"/>
/// (authenticated view that includes private packages).
/// </para>
/// <para>
/// <b>C5 — no client-side ownership logic.</b> The page renders all four maintainer controls
/// unconditionally. If the authenticated user does not own the package, the registry returns
/// 403 on a write and the BFF surfaces the registry's <c>ErrorResponse.Message</c> inline.
/// The BFF does not pre-check ownership.
/// </para>
/// <para>
/// <b>C4 — error mapping:</b>
/// <list type="bullet">
///   <item>GET registry 404 → website 404 page (sets 404 status).</item>
///   <item>Any registry 401 → clear cookie + session + redirect <c>/login?expired=1</c>.</item>
///   <item>Registry 403 on a write → inline error banner with the registry's message.</item>
///   <item>Registry 5xx → inline error banner.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ManageModel : PageModel
{
    private readonly IAuthenticatedRegistryClient _authClient;
    private readonly ISessionStore _sessionStore;

    // ── Named query-string keys (bounded-domain constants) ────────────────────

    /// <summary>Query-string key used on the <c>/login</c> redirect when a session expires.</summary>
    public const string ExpiredQueryKey = "expired";

    /// <summary>Query-string value for the expired-session redirect.</summary>
    public const string ExpiredQueryValue = "1";

    public ManageModel(
        IAuthenticatedRegistryClient authClient,
        ISessionStore sessionStore)
    {
        _authClient = authClient;
        _sessionStore = sessionStore;
    }

    // ── Bound route values ────────────────────────────────────────────────────

    /// <summary>The scope segment (without leading <c>@</c>) from the URL.</summary>
    [BindProperty(SupportsGet = true)]
    public string Scope { get; set; } = string.Empty;

    /// <summary>The package name segment from the URL.</summary>
    [BindProperty(SupportsGet = true)]
    public string Name { get; set; } = string.Empty;

    // ── Page state ────────────────────────────────────────────────────────────

    /// <summary>Package detail, or <see langword="null"/> when the registry returned 404.</summary>
    public PackageDetailResponse? Package { get; private set; }

    /// <summary>When <see langword="true"/>, the page renders the 404 state.</summary>
    public bool PackageNotFound { get; private set; }

    /// <summary>Success banner message, or <see langword="null"/> when no action was just performed.</summary>
    public string? SuccessBanner { get; private set; }

    /// <summary>Error banner message (registry 403 or 5xx on a write), or <see langword="null"/>.</summary>
    public string? ErrorBanner { get; private set; }

    // ── Bound form properties (for POST handlers) ─────────────────────────────

    /// <summary>Deprecation message for the deprecate-package form.</summary>
    [BindProperty]
    public string DeprecationMessage { get; set; } = string.Empty;

    /// <summary>Optional alternative package suggestion for the deprecate-package form.</summary>
    [BindProperty]
    public string? DeprecationAlternative { get; set; }

    /// <summary>Deprecation message for the deprecate-version form.</summary>
    [BindProperty]
    public string VersionDeprecationMessage { get; set; } = string.Empty;

    /// <summary>The version being acted on (for version-level deprecate/undeprecate).</summary>
    [BindProperty]
    public string TargetVersion { get; set; } = string.Empty;

    /// <summary>New visibility value selected in the visibility dropdown.</summary>
    [BindProperty]
    public Visibilities NewVisibility { get; set; }

    // ── Derived helpers ───────────────────────────────────────────────────────

    /// <summary>The fully-qualified package display name including the <c>@</c> prefix.</summary>
    public string FullDisplayName => $"@{Scope}/{Name}";

    // ── GET handler ───────────────────────────────────────────────────────────

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken = default)
    {
        return await LoadPackageAsync(cancellationToken);
    }

    // ── POST: deprecate package ───────────────────────────────────────────────

    public async Task<IActionResult> OnPostDeprecatePackageAsync(CancellationToken cancellationToken = default)
    {
        var request = new DeprecatePackageRequest
        {
            Message = DeprecationMessage,
            Alternative = string.IsNullOrWhiteSpace(DeprecationAlternative)
                ? null
                : DeprecationAlternative,
        };

        var authzResult = await TryCallAsync(
            () => _authClient.DeprecatePackageAsync(Scope, Name, request, cancellationToken),
            successBanner: "Package deprecated successfully.",
            cancellationToken: cancellationToken);

        return authzResult;
    }

    // ── POST: undeprecate package ─────────────────────────────────────────────

    public async Task<IActionResult> OnPostUndeprecatePackageAsync(CancellationToken cancellationToken = default)
    {
        var authzResult = await TryCallAsync(
            () => _authClient.UndeprecatePackageAsync(Scope, Name, cancellationToken),
            successBanner: "Package deprecation removed.",
            cancellationToken: cancellationToken);

        return authzResult;
    }

    // ── POST: deprecate version ───────────────────────────────────────────────

    public async Task<IActionResult> OnPostDeprecateVersionAsync(CancellationToken cancellationToken = default)
    {
        var request = new DeprecateVersionRequest { Message = VersionDeprecationMessage };

        var authzResult = await TryCallAsync(
            () => _authClient.DeprecateVersionAsync(Scope, Name, TargetVersion, request, cancellationToken),
            successBanner: $"Version {TargetVersion} deprecated.",
            cancellationToken: cancellationToken);

        return authzResult;
    }

    // ── POST: undeprecate version ─────────────────────────────────────────────

    public async Task<IActionResult> OnPostUndeprecateVersionAsync(CancellationToken cancellationToken = default)
    {
        var authzResult = await TryCallAsync(
            () => _authClient.UndeprecateVersionAsync(Scope, Name, TargetVersion, cancellationToken),
            successBanner: $"Deprecation removed from version {TargetVersion}.",
            cancellationToken: cancellationToken);

        return authzResult;
    }

    // ── POST: set visibility ──────────────────────────────────────────────────

    public async Task<IActionResult> OnPostSetVisibilityAsync(CancellationToken cancellationToken = default)
    {
        var request = new SetVisibilityRequest { Visibility = NewVisibility };

        var authzResult = await TryCallAsync(
            () => _authClient.SetVisibilityAsync(Scope, Name, request, cancellationToken),
            successBanner: "Package visibility updated.",
            cancellationToken: cancellationToken);

        return authzResult;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Executes <paramref name="action"/>, maps 401 / 403 / 5xx to the right outcome,
    /// and re-renders the page with the appropriate banner.
    /// </summary>
    private async Task<IActionResult> TryCallAsync<T>(
        System.Func<Task<T>> action,
        string successBanner,
        CancellationToken cancellationToken)
    {
        try
        {
            await action();
            SuccessBanner = successBanner;
        }
        catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return await ClearSessionAndRedirectToLoginExpiredAsync(cancellationToken);
        }
        catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            // C5: surface the registry's message inline; never pre-check ownership.
            ErrorBanner = ex.ErrorMessage
                ?? "You do not have permission to perform this action.";
        }
        catch (RegistryClientException ex)
        {
            ErrorBanner = ex.ErrorMessage
                ?? "The registry returned an error. Please try again.";
        }

        // Re-read package state to show post-mutation result.
        return await LoadPackageAsync(cancellationToken);
    }

    /// <summary>
    /// Loads the package via <see cref="IAuthenticatedRegistryClient.GetPackageAsync"/>.
    /// Maps 404 → website 404; 401 → session expiry redirect; 5xx → error banner.
    /// </summary>
    private async Task<IActionResult> LoadPackageAsync(CancellationToken cancellationToken)
    {
        try
        {
            Package = await _authClient.GetPackageAsync(Scope, Name, cancellationToken)
                .ConfigureAwait(false);

            if (Package is null)
            {
                PackageNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;
                return Page();
            }
        }
        catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return await ClearSessionAndRedirectToLoginExpiredAsync(cancellationToken);
        }
        catch (RegistryClientException ex)
        {
            ErrorBanner = ex.ErrorMessage
                ?? "The registry is temporarily unreachable.";
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
