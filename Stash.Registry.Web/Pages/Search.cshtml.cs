using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Services;

namespace Stash.Registry.Web.Pages;

/// <summary>
/// Page model for the search page (<c>GET /search</c>).
/// Renders a filterable, sortable, paginated list of packages.
/// </summary>
/// <remarks>
/// Query parameters are bound declaratively through individual <see cref="BindPropertyAttribute"/>
/// properties. These are assembled into a <see cref="SearchQuery"/> for the registry call —
/// no raw <c>Request.Query</c> reads.
/// </remarks>
public sealed class SearchModel : PageModel
{
    private readonly IRegistryClient _registryClient;

    public SearchModel(IRegistryClient registryClient)
    {
        _registryClient = registryClient;
    }

    // ── Bound query parameters ────────────────────────────────────────────────

    /// <summary>Free-text search query (bound from <c>?q=</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>Keyword filter (bound from <c>?keyword=</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Keyword { get; set; }

    /// <summary>License filter (bound from <c>?license=</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public string? License { get; set; }

    /// <summary>Deprecated filter (bound from <c>?deprecated=</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public bool? Deprecated { get; set; }

    /// <summary>Owner filter (bound from <c>?owner=</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Owner { get; set; }

    /// <summary>Sort order (bound from <c>?sort=</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public PackageSortOrder Sort { get; set; } = PackageSortOrder.Relevance;

    /// <summary>Page number (bound from <c>?page=</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public new int Page { get; set; } = 1;

    /// <summary>Page size (bound from <c>?pageSize=</c>).</summary>
    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    // ── Result state ──────────────────────────────────────────────────────────

    /// <summary>The paged search results. Null before <see cref="OnGetAsync"/> runs.</summary>
    public PagedResponse<PackageSummaryResponse>? Results { get; private set; }

    /// <summary>Error message when the registry is unreachable (502), or <c>null</c> on success.</summary>
    public string? RegistryError { get; private set; }

    /// <summary>
    /// Validation message when the registry rejected the request with 400, or <c>null</c> otherwise.
    /// Distinct from <see cref="RegistryError"/> so the view can render a concise inline alert
    /// without the "Registry Unavailable" framing that only belongs with 5xx responses.
    /// </summary>
    public string? ValidationError { get; private set; }

    // ── Page actions ──────────────────────────────────────────────────────────

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Page < 1) Page = 1;
        if (PageSize < 1) PageSize = 20;
        if (PageSize > PagingLimits.MaxPageSize) PageSize = PagingLimits.MaxPageSize;

        var query = new SearchQuery
        {
            q = Q,
            keyword = Keyword,
            license = License,
            deprecated = Deprecated,
            owner = Owner,
            sort = Sort,
            page = Page,
            pageSize = PageSize,
        };

        try
        {
            Results = await _registryClient.SearchAsync(query, cancellationToken);
        }
        catch (RegistryClientException ex) when ((int)ex.StatusCode == StatusCodes.Status400BadRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            ValidationError = ex.ErrorMessage ?? "The request was invalid.";
        }
        catch (RegistryClientException ex) when ((int)ex.StatusCode >= 500)
        {
            Response.StatusCode = StatusCodes.Status502BadGateway;
            RegistryError = "The package registry is currently unreachable.";
        }
        catch (RegistryClientException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // 404 on search → treat as empty results.
            Results = new PagedResponse<PackageSummaryResponse>
            {
                Items = new List<PackageSummaryResponse>(),
                TotalCount = 0,
                Page = Page,
                PageSize = PageSize,
                TotalPages = 0,
            };
        }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status502BadGateway;
            RegistryError = "The package registry is currently unreachable.";
        }

        return Page();
    }

    // ── Pagination helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Builds a query string that round-trips all current filter/sort params with only <c>page</c> changed.
    /// </summary>
    public string BuildPageUrl(int targetPage)
    {
        var qs = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(Q)) qs["q"] = Q;
        if (!string.IsNullOrEmpty(Keyword)) qs["keyword"] = Keyword;
        if (!string.IsNullOrEmpty(License)) qs["license"] = License;
        if (Deprecated.HasValue) qs["deprecated"] = Deprecated.Value ? "true" : "false";
        if (!string.IsNullOrEmpty(Owner)) qs["owner"] = Owner;
        if (Sort != PackageSortOrder.Relevance) qs["sort"] = Sort.ToString();
        if (PageSize != 20) qs["pageSize"] = PageSize.ToString();
        qs["page"] = targetPage.ToString();

        return "/search" + Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("", qs);
    }
}
