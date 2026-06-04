using System;
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
using Stash.Registry.Database.Models;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Integration tests for the Search v2 sort orders (Bucket-A only):
/// <c>Relevance</c> (default), <c>Name</c>, <c>Updated</c>, <c>Published</c>.
/// Also verifies that Bucket-B sort values (<c>sort=downloads</c>) are rejected.
/// </summary>
public sealed class SearchV2SortTests
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

    private static async Task SeedPackageAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        PackageRecord package)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        await db.CreatePackageAsync(package);
    }

    // ── sort=Relevance (default — name ascending) ─────────────────────────────

    /// <summary>
    /// <c>sort=Relevance</c> (the default) orders results by package name ascending —
    /// identical to the pre-P5 behavior.
    /// </summary>
    [Fact]
    public async Task Sort_Relevance_OrdersByNameAscending()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        // Seed in reverse alphabetical order to confirm ordering is applied
        await SeedPackageAsync(factory, new PackageRecord
        {
            Name = "c-package", Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Visibility = Visibilities.Public
        });
        await SeedPackageAsync(factory, new PackageRecord
        {
            Name = "a-package", Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Visibility = Visibilities.Public
        });
        await SeedPackageAsync(factory, new PackageRecord
        {
            Name = "b-package", Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Visibility = Visibilities.Public
        });

        var response = await client.GetAsync("/api/v1/search?sort=Relevance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("a-package", items[0].GetProperty("name").GetString());
        Assert.Equal("b-package", items[1].GetProperty("name").GetString());
        Assert.Equal("c-package", items[2].GetProperty("name").GetString());
    }

    /// <summary>
    /// Omitting <c>sort</c> entirely must behave identically to <c>sort=Relevance</c>
    /// (name ascending). Asserts byte-identical default-path behavior from pre-P5.
    /// </summary>
    [Fact]
    public async Task Sort_Default_BehavesIdenticalToRelevance()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        await SeedPackageAsync(factory, new PackageRecord
        {
            Name = "z-default", Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Visibility = Visibilities.Public
        });
        await SeedPackageAsync(factory, new PackageRecord
        {
            Name = "a-default", Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Visibility = Visibilities.Public
        });

        // Without sort param
        var respNoSort = await client.GetAsync("/api/v1/search");
        // With sort=Relevance
        var respRelevance = await client.GetAsync("/api/v1/search?sort=Relevance");

        Assert.Equal(HttpStatusCode.OK, respNoSort.StatusCode);
        Assert.Equal(HttpStatusCode.OK, respRelevance.StatusCode);

        string contentNoSort = await respNoSort.Content.ReadAsStringAsync();
        string contentRelevance = await respRelevance.Content.ReadAsStringAsync();

        using var docNoSort = JsonDocument.Parse(contentNoSort);
        using var docRelevance = JsonDocument.Parse(contentRelevance);

        var itemsNoSort = docNoSort.RootElement.GetProperty("items");
        var itemsRelevance = docRelevance.RootElement.GetProperty("items");

        Assert.Equal(itemsNoSort.GetArrayLength(), itemsRelevance.GetArrayLength());
        for (int i = 0; i < itemsNoSort.GetArrayLength(); i++)
        {
            Assert.Equal(
                itemsNoSort[i].GetProperty("name").GetString(),
                itemsRelevance[i].GetProperty("name").GetString());
        }
    }

    // ── sort=Name ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>sort=Name</c> orders results by package name ascending (same as Relevance).
    /// Case-insensitive binding: <c>sort=name</c> must also be accepted.
    /// </summary>
    [Fact]
    public async Task Sort_Name_OrdersByNameAscending()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        await SeedPackageAsync(factory, new PackageRecord
        {
            Name = "z-sort-name", Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Visibility = Visibilities.Public
        });
        await SeedPackageAsync(factory, new PackageRecord
        {
            Name = "a-sort-name", Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Visibility = Visibilities.Public
        });

        var response = await client.GetAsync("/api/v1/search?sort=Name");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("a-sort-name", items[0].GetProperty("name").GetString());
        Assert.Equal("z-sort-name", items[1].GetProperty("name").GetString());
    }

    /// <summary>
    /// <c>sort=name</c> (lowercase) is accepted because <c>[FromQuery]</c> enum binding
    /// is case-insensitive by member name.
    /// </summary>
    [Fact]
    public async Task Sort_NameLowercase_IsAccepted()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?sort=name");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── sort=Updated ──────────────────────────────────────────────────────────

    /// <summary>
    /// <c>sort=Updated</c> orders results by <c>UpdatedAt</c> descending (most-recently updated first).
    /// </summary>
    [Fact]
    public async Task Sort_Updated_OrdersByUpdatedAtDescending()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var older = new PackageRecord
        {
            Name = "older-updated", Latest = "1.0.0",
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Visibility = Visibilities.Public
        };
        var newer = new PackageRecord
        {
            Name = "newer-updated", Latest = "1.0.0",
            CreatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Visibility = Visibilities.Public
        };
        await SeedPackageAsync(factory, older);
        await SeedPackageAsync(factory, newer);

        var response = await client.GetAsync("/api/v1/search?sort=Updated");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(2, items.GetArrayLength());
        // Most recently updated first
        Assert.Equal("newer-updated", items[0].GetProperty("name").GetString());
        Assert.Equal("older-updated", items[1].GetProperty("name").GetString());
    }

    // ── sort=Published ────────────────────────────────────────────────────────

    /// <summary>
    /// <c>sort=Published</c> orders results by <c>CreatedAt</c> descending (most-recently published first).
    /// </summary>
    [Fact]
    public async Task Sort_Published_OrdersByCreatedAtDescending()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var olderPub = new PackageRecord
        {
            Name = "older-published", Latest = "1.0.0",
            CreatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Visibility = Visibilities.Public
        };
        var newerPub = new PackageRecord
        {
            Name = "newer-published", Latest = "1.0.0",
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Visibility = Visibilities.Public
        };
        await SeedPackageAsync(factory, olderPub);
        await SeedPackageAsync(factory, newerPub);

        var response = await client.GetAsync("/api/v1/search?sort=Published");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(2, items.GetArrayLength());
        // Most recently published first
        Assert.Equal("newer-published", items[0].GetProperty("name").GetString());
        Assert.Equal("older-published", items[1].GetProperty("name").GetString());
    }

    // ── sort=downloads returns 400 (Bucket-B not accepted) ────────────────────

    /// <summary>
    /// <c>sort=downloads</c> is a Bucket-B value. The model binder must reject it
    /// and return <c>400 InvalidRequest</c> via the existing <c>InvalidModelStateResponseFactory</c>.
    /// This is the primary Bucket-B boundary test for sorting.
    /// </summary>
    [Fact]
    public async Task Sort_Downloads_Returns400_WithInvalidRequestBody()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?sort=downloads");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        // Must use the standard ErrorResponse shape from InvalidModelStateResponseFactory.
        Assert.True(doc.RootElement.TryGetProperty("error", out var errorProp),
            "400 body must include 'error' field (ErrorResponse shape).");
        Assert.Equal("InvalidRequest", errorProp.GetString());

        Assert.True(doc.RootElement.TryGetProperty("message", out _),
            "400 body must include 'message' field (ErrorResponse shape).");
    }

    /// <summary>
    /// <c>sort=vulnerable</c> is another Bucket-B value that must also return 400.
    /// </summary>
    [Fact]
    public async Task Sort_Vulnerable_Returns400()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?sort=vulnerable");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// <c>sort=verified</c> is another Bucket-B value that must also return 400.
    /// </summary>
    [Fact]
    public async Task Sort_Verified_Returns400()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/search?sort=verified");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Bucket-B filter fields absent from SearchQuery ────────────────────────

    /// <summary>
    /// Verifies that <see cref="SearchQuery"/> does NOT expose <c>vulnerable</c>,
    /// <c>verified</c>, or <c>provenance</c> properties — these are Bucket-B and
    /// must not be accepted as filter parameters. This test confirms the DTO shape
    /// at the type system level.
    /// </summary>
    [Fact]
    public void SearchQuery_DoesNotExposeBucketBFilterFields()
    {
        var t = typeof(SearchQuery);

        Assert.Null(t.GetProperty("vulnerable"));    // Bucket-B — must be absent
        Assert.Null(t.GetProperty("verified"));      // Bucket-B — must be absent
        Assert.Null(t.GetProperty("provenance"));    // Bucket-B — must be absent
    }

    /// <summary>
    /// Verifies that <see cref="PackageSortOrder"/> does NOT contain a <c>Downloads</c>
    /// member — the Bucket-B sort value must be absent from the enum.
    /// </summary>
    [Fact]
    public void PackageSortOrder_DoesNotContainDownloads()
    {
        var names = Enum.GetNames(typeof(PackageSortOrder));
        Assert.DoesNotContain("Downloads", (System.Collections.Generic.IEnumerable<string>)names);
    }
}
