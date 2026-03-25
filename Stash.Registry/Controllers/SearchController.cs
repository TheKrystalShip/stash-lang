using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    /// <param name="db">Registry database used to execute search queries.</param>
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
    /// No authentication required.
    /// </remarks>
    /// <returns>
    /// <c>200</c> with a <see cref="SearchResponse"/> containing a list of
    /// <see cref="PackageSummaryResponse"/> objects, pagination metadata, and the total
    /// result count.
    /// </returns>
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Search()
    {
        string query = Request.Query.TryGetValue("q", out var q) ? q.ToString() : "";

        int page = 1;
        int pageSize = 20;

        if (Request.Query.TryGetValue("page", out var pageStr) && int.TryParse(pageStr, out int parsedPage) && parsedPage > 0)
        {
            page = parsedPage;
        }

        if (Request.Query.TryGetValue("pageSize", out var pageSizeStr) && int.TryParse(pageSizeStr, out int parsedPageSize) && parsedPageSize > 0)
        {
            pageSize = Math.Min(parsedPageSize, 100);
        }

        SearchResult result = await _db.SearchPackagesAsync(query, page, pageSize);

        List<PackageSummaryResponse> packages = result.Packages.Select(p =>
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

        int totalPages = (int)Math.Ceiling(result.TotalCount / (double)pageSize);

        return Ok(new SearchResponse
        {
            Packages = packages,
            TotalCount = result.TotalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        });
    }
}
