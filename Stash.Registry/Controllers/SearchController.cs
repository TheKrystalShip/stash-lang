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

[ApiController]
[Route("api/v1/search")]
public sealed class SearchController : ControllerBase
{
    private readonly IRegistryDatabase _db;

    public SearchController(IRegistryDatabase db)
    {
        _db = db;
    }

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
                UpdatedAt = p.UpdatedAt.ToString("o")
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
