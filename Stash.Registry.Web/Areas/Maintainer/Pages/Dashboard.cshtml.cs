using System.Threading;
using System.Threading.Tasks;
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
/// </remarks>
public sealed class DashboardModel : PageModel
{
    private readonly IAuthenticatedRegistryClient _authClient;
    private readonly ISessionTokenAccessor _sessionTokenAccessor;

    public DashboardModel(
        IAuthenticatedRegistryClient authClient,
        ISessionTokenAccessor sessionTokenAccessor)
    {
        _authClient = authClient;
        _sessionTokenAccessor = sessionTokenAccessor;
    }

    /// <summary>Packages owned by the authenticated user (including private ones).</summary>
    public PagedResponse<PackageSummaryResponse>? Packages { get; private set; }

    /// <summary>Registry error message, or <see langword="null"/> if the registry is reachable.</summary>
    public string? RegistryError { get; private set; }

    /// <summary>Current page number (1-based).</summary>
    public int CurrentPage { get; private set; } = 1;

    public async Task OnGetAsync(int page = 1, CancellationToken cancellationToken = default)
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
        catch (RegistryClientException ex)
        {
            RegistryError = ex.Message ?? "The registry is temporarily unreachable.";
        }
    }
}
