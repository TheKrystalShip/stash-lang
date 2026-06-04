using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Services;

namespace Stash.Registry.Web.Pages;

/// <summary>
/// Page model for the home page (<c>GET /</c>).
/// Renders a "recently updated" rail of package cards from the registry's search endpoint.
/// </summary>
public sealed class IndexModel : PageModel
{
    private const int RecentlyUpdatedCount = 20;

    private readonly IRegistryClient _registryClient;

    public IndexModel(IRegistryClient registryClient)
    {
        _registryClient = registryClient;
    }

    /// <summary>
    /// The recently-updated packages to render in the rail. Empty when the registry returns no results.
    /// </summary>
    public IReadOnlyList<PackageSummaryResponse> RecentPackages { get; private set; } =
        Array.Empty<PackageSummaryResponse>();

    /// <summary>
    /// Error message to display when the registry is unreachable (502), or <c>null</c> on success.
    /// </summary>
    public string? RegistryError { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _registryClient.SearchAsync(
                new SearchQuery
                {
                    sort = PackageSortOrder.Updated,
                    pageSize = RecentlyUpdatedCount,
                },
                cancellationToken);

            RecentPackages = result.Items;
        }
        catch (RegistryClientException ex) when ((int)ex.StatusCode >= 500)
        {
            Response.StatusCode = StatusCodes.Status502BadGateway;
            RegistryError = "The package registry is currently unreachable.";
        }
        catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // 404 on a list call → treat as empty results, not an error.
            RecentPackages = Array.Empty<PackageSummaryResponse>();
        }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status502BadGateway;
            RegistryError = "The package registry is currently unreachable.";
        }

        return Page();
    }
}
