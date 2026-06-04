using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Integration tests verifying that <c>SearchController.Search</c> returns a
/// <see cref="PagedResponse{T}"/> of <see cref="PackageSummaryResponse"/> with the
/// <c>"items"</c> wire key (not the former <c>"packages"</c> key that existed before
/// the P1 envelope migration).
/// </summary>
public sealed class SearchControllerTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private static WebApplicationFactory<Stash.Registry.Program> CreateFactory(SqliteConnection conn)
    {
        return new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(DbContextOptions<RegistryDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddDbContext<RegistryDbContext>(options =>
                        options.UseSqlite(conn));

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });
    }

    // ── Wire shape: items key ────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/search returns a JSON object with the key <c>"items"</c> (not <c>"packages"</c>).
    /// This is the P1 breaking wire change: the collection key is unified across all paginated endpoints.
    /// </summary>
    [Fact]
    public async Task Search_ReturnsItemsKey_NotPackagesKey()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out _),
            "Search response must use the key 'items' (PagedResponse<T> envelope).");
        Assert.False(root.TryGetProperty("packages", out _),
            "Search response must NOT use the old key 'packages'.");
    }

    /// <summary>
    /// GET /api/v1/search returns all five pagination metadata fields:
    /// <c>items</c>, <c>totalCount</c>, <c>page</c>, <c>pageSize</c>, <c>totalPages</c>.
    /// </summary>
    [Fact]
    public async Task Search_ResponseContainsAllPaginationFields()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out _), "Expected 'items' field.");
        Assert.True(root.TryGetProperty("totalCount", out _), "Expected 'totalCount' field.");
        Assert.True(root.TryGetProperty("page", out var pg), "Expected 'page' field.");
        Assert.True(root.TryGetProperty("pageSize", out var ps), "Expected 'pageSize' field.");
        Assert.True(root.TryGetProperty("totalPages", out _), "Expected 'totalPages' field.");

        Assert.Equal(1, pg.GetInt32());
        Assert.Equal(20, ps.GetInt32());
    }

    /// <summary>
    /// GET /api/v1/search returns a 200 OK with default pagination values when no query
    /// parameters are provided. Verifies the envelope default values.
    /// </summary>
    [Fact]
    public async Task Search_NoParameters_Returns200WithPagedEnvelope()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // Default values from SearchQuery
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(20, root.GetProperty("pageSize").GetInt32());
    }

    // ── Per-endpoint caps remain on query DTO ─────────────────────────────────

    /// <summary>
    /// GET /api/v1/search?pageSize=101 returns 400 InvalidRequest (cap = 100 on SearchQuery).
    /// Verifies the per-endpoint cap is still enforced via the query DTO, not the envelope.
    /// </summary>
    [Fact]
    public async Task Search_PageSizeAboveCap_Returns400()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// GET /api/v1/search?pageSize=100 (at the cap) returns 200 OK.
    /// The per-endpoint cap on SearchQuery is inclusive at 100.
    /// </summary>
    [Fact]
    public async Task Search_PageSizeAtCap100_Returns200()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
