using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Web.Pages;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Services;
using Stash.Tests.Registry.Web.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Integration tests for the search page (<c>GET /search</c>).
/// Uses <see cref="WebApplicationFactory{TProgram}"/> against a <see cref="StubRegistryClient"/>.
/// </summary>
public sealed class SearchPageTests
{
    // ── 200 with results ──────────────────────────────────────────────────────

    [Fact]
    public async Task SearchPage_WithResults_Returns200_AndRendersCards()
    {
        // Arrange
        var stub = StubRegistryClient.WithPackages([
            StubRegistryClient.SamplePackage("org/foo", "Foo package", "1.0.0"),
        ]);

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/search?q=foo");

        // Assert
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("org/foo", html);
        Assert.Contains("Foo package", html);
    }

    [Fact]
    public async Task SearchPage_WithResults_RendersSortDropdown_WithSortLabels()
    {
        // Arrange — all four enum values should appear in the sort dropdown
        var stub = StubRegistryClient.WithPackages([]);

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/search");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // The dropdown should show labels from SortLabels, not enum names directly
        Assert.Contains("Relevance", html);
        Assert.Contains("Recently Updated", html);
        Assert.Contains("Recently Published", html);
        Assert.Contains("Name", html);

        // The option values are enum member names (for round-trip binding)
        Assert.Contains("value=\"Updated\"", html);
        Assert.Contains("value=\"Published\"", html);
        Assert.Contains("value=\"Relevance\"", html);
        Assert.Contains("value=\"Name\"", html);
    }

    [Fact]
    public async Task SearchPage_WithResults_RendersFilterBar()
    {
        // Arrange
        var stub = StubRegistryClient.WithPackages([]);

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/search");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // The filter bar should have inputs for keyword, license, owner, deprecated
        Assert.Contains("name=\"keyword\"", html);
        Assert.Contains("name=\"license\"", html);
        Assert.Contains("name=\"owner\"", html);
        Assert.Contains("name=\"deprecated\"", html);
    }

    [Fact]
    public async Task SearchPage_Pagination_LinksRoundTripAllQueryParams()
    {
        // Arrange — 3 pages of results so pagination renders
        var items = new List<PackageSummaryResponse>();
        for (var i = 0; i < 5; i++)
            items.Add(StubRegistryClient.SamplePackage($"org/pkg{i}"));

        var stub = new StubRegistryClient
        {
            SearchResult = new PagedResponse<PackageSummaryResponse>
            {
                Items = items,
                TotalCount = 100,
                Page = 1,
                PageSize = 5,
                TotalPages = 20,
            },
        };

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/search?q=foo&sort=Updated&license=MIT&page=1");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // Pagination links must carry sort and q parameters through
        Assert.Contains("sort=Updated", html);
        Assert.Contains("q=foo", html);
        Assert.Contains("license=MIT", html);
    }

    [Fact]
    public async Task SearchPage_FilterBar_PreservesSelectedSort()
    {
        // Arrange
        var stub = StubRegistryClient.WithPackages([]);

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/search?sort=Name");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // The "Name" option should be selected
        Assert.Contains("value=\"Name\"", html);
        // The selected option for Name should appear in the rendered HTML
        // (the actual 'selected' attribute is rendered by the tag helper)
        var nameOptionIndex = html.IndexOf("value=\"Name\"", StringComparison.Ordinal);
        var snippetAfterName = html.Substring(nameOptionIndex, Math.Min(80, html.Length - nameOptionIndex));
        Assert.Contains("selected", snippetAfterName);
    }

    // ── Empty results ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchPage_EmptyResults_Returns200_AndRendersEmptyState()
    {
        // Arrange
        var stub = new StubRegistryClient(); // default: empty result

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/search?q=nonexistent");

        // Assert
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("No packages found", html);
    }

    // ── Registry 5xx → 502 ────────────────────────────────────────────────────

    [Fact]
    public async Task SearchPage_RegistryServerError_Returns502_AndRendersErrorMessage()
    {
        // Arrange
        var stub = StubRegistryClient.WithSearchServerError();

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // Act
        var response = await client.GetAsync("/search?q=foo");

        // Assert — status 502 and the error is rendered (not an unhandled exception)
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("unavailable", html);
    }

    // ── Registry 400 → website 400 ────────────────────────────────────────────

    [Fact]
    public async Task SearchPage_RegistryReturns400_Returns400WithValidationMessage()
    {
        // Arrange — registry rejects with 400 InvalidRequest (e.g. pageSize out of range)
        var stub = new StubRegistryClient
        {
            SearchException = new RegistryClientException(
                System.Net.HttpStatusCode.BadRequest,
                "InvalidRequest",
                "pageSize must be at most 100."),
        };

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // Act
        var response = await client.GetAsync("/search?q=x&pageSize=99999");

        // Assert — 400 status, validation message present, no "Registry Unavailable" heading
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("pageSize must be at most 100.", html);
        Assert.DoesNotContain("Registry Unavailable", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("502", html);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WebApplicationFactory<HealthModel> CreateFactory(IRegistryClient stub)
    {
        return new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IRegistryClient>(_ => stub);
            });
        });
    }
}
