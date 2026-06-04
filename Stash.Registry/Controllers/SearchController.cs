using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Stash.Registry.Auth.Authorization;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Controllers;

/// <summary>
/// REST API controller for searching the package registry.
/// </summary>
/// <remarks>
/// <para>
/// All search endpoints are public and require no authentication.
/// Queries are matched against package names and descriptions using a SQL
/// <c>LIKE</c> pattern. Results are paginated; the maximum page size is 100.
/// Visibility filtering is enforced through the PDP-backed predicate in
/// <see cref="IRegistryDatabase.SearchPackagesAsync"/>: anonymous callers see only
/// public packages; authenticated callers also see packages they have at least reader
/// access to via their role, team, or org membership.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/search")]
public sealed class SearchController : ControllerBase
{
    private readonly IRegistryDatabase _db;

    /// <summary>
    /// Initialises the controller with its required services.
    /// </summary>
    public SearchController(IRegistryDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Searches for packages by name or description with pagination.
    /// </summary>
    /// <remarks>
    /// Accepts the following query-string parameters:
    /// <list type="bullet">
    ///   <item><term>q</term><description>Search term (default: empty, returns all packages).</description></item>
    ///   <item><term>page</term><description>1-based page number (default: 1).</description></item>
    ///   <item><term>pageSize</term><description>Results per page, 1–100 (default: 20).</description></item>
    /// </list>
    /// No authentication required. Private packages are omitted for unauthenticated callers.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="PagedResponse{T}"/> of <see cref="PackageSummaryResponse"/>
    /// objects, pagination metadata, and the total result count.
    /// The collection key is <c>"items"</c> (unified across all paginated endpoints).
    /// </returns>
    [PublicEndpoint("package search is a public discovery endpoint — unauthenticated callers see only public packages")]
    [RegistryAuthorize(RegistryAction.Search)]
    [HttpGet]
    public async Task<Results<Ok<PagedResponse<PackageSummaryResponse>>, BadRequest<ErrorResponse>>> Search([FromQuery] SearchQuery query)
    {
        // [Range] on SearchQuery.page and SearchQuery.pageSize ensures valid values.
        // Out-of-range values return 400 InvalidRequest (replaces the previous silent clamp).

        // Pass the caller's username so that visibility filtering can include private/internal
        // packages the caller has permission to read. Unauthenticated callers get null and see
        // only public packages. The PDP-backed predicate lives in SearchPackagesAsync.
        string? callerUsername = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;
        SearchResult result = await _db.SearchPackagesAsync(query.q ?? "", query.page, query.pageSize, callerUsername);

        List<PackageSummaryResponse> items = result.Packages.Select(p =>
        {
            List<string>? keywords = null;
            if (p.Keywords != null)
            {
                try { keywords = JsonSerializer.Deserialize<List<string>>(p.Keywords); }
                catch (JsonException) { }
            }

            return new PackageSummaryResponse
            {
                Name = p.Name,
                Description = p.Description,
                Latest = p.Latest,
                Keywords = keywords ?? new List<string>(),
                UpdatedAt = p.UpdatedAt.ToString("o"),
                Deprecated = p.Deprecated
            };
        }).ToList();

        int totalPages = (int)Math.Ceiling(result.TotalCount / (double)query.pageSize);

        return TypedResults.Ok(new PagedResponse<PackageSummaryResponse>
        {
            Items = items,
            TotalCount = result.TotalCount,
            Page = query.page,
            PageSize = query.pageSize,
            TotalPages = totalPages
        });
    }
}
