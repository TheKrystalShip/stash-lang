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

namespace Stash.Tests.Registry.Validation;

/// <summary>
/// Integration tests verifying that <c>SearchController.Search</c> rejects out-of-range
/// <c>pageSize</c> values with 400 <c>InvalidRequest</c> (replacing the former silent clamp)
/// and continues to serve in-range requests normally.
/// </summary>
public sealed class SearchControllerValidationTests
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

    private static async Task<ErrorResponse?> ReadErrorResponseAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        try
        {
            return JsonSerializer.Deserialize<ErrorResponse>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    // ── Out-of-range pageSize → 400 ───────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/search?pageSize=500 returns 400 InvalidRequest (pageSize > 100).
    /// This replaces the previous silent clamp behaviour (Math.Min(500, 100) → 200 OK).
    /// </summary>
    [Fact]
    public async Task Search_PageSizeAboveMax_Returns400InvalidRequest()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?pageSize=500");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
    }

    /// <summary>
    /// GET /api/v1/search?pageSize=0 returns 400 InvalidRequest (pageSize < 1).
    /// </summary>
    [Fact]
    public async Task Search_PageSizeZero_Returns400InvalidRequest()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?pageSize=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
    }

    /// <summary>
    /// GET /api/v1/search?page=0 returns 400 InvalidRequest (page < 1).
    /// </summary>
    [Fact]
    public async Task Search_PageZero_Returns400InvalidRequest()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
    }

    // ── Valid pageSize → 200 ──────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/search?pageSize=20 returns 200 OK with a 20-item-max page.
    /// </summary>
    [Fact]
    public async Task Search_ValidPageSize20_Returns200()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        Assert.True(doc.RootElement.TryGetProperty("packages", out _),
            "Expected 'packages' array in search response");
    }

    /// <summary>
    /// GET /api/v1/search?pageSize=100 (at the max limit) returns 200 OK.
    /// </summary>
    [Fact]
    public async Task Search_PageSizeAtMax100_Returns200()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// GET /api/v1/search (no query parameters) returns 200 OK with default page/pageSize.
    /// Verifies the default values (Page=1, PageSize=20) are accepted without a 400.
    /// </summary>
    [Fact]
    public async Task Search_NoParameters_Returns200WithDefaults()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(20, doc.RootElement.GetProperty("pageSize").GetInt32());
    }
}
