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
/// Integration tests for the home page (<c>GET /</c>).
/// Uses <see cref="WebApplicationFactory{TProgram}"/> against a <see cref="StubRegistryClient"/>.
/// </summary>
public sealed class HomePageTests
{
    // ── 200 with results ──────────────────────────────────────────────────────

    [Fact]
    public async Task HomePage_WithPackages_Returns200_AndRendersCards()
    {
        // Arrange
        var stub = StubRegistryClient.WithPackages([
            StubRegistryClient.SamplePackage("org/alpha", "Alpha package", "1.0.0"),
            StubRegistryClient.SamplePackage("org/beta", "Beta package", "2.0.0"),
        ]);

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("org/alpha", html);
        Assert.Contains("Alpha package", html);
        Assert.Contains("org/beta", html);
        Assert.Contains("Beta package", html);
    }

    [Fact]
    public async Task HomePage_WithPackages_RendersRecentlyUpdatedSection()
    {
        // Arrange
        var stub = StubRegistryClient.WithPackages([
            StubRegistryClient.SamplePackage("org/pkg"),
        ]);

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Recently Updated", html);
    }

    [Fact]
    public async Task HomePage_WithPackages_SearchUsesUpdatedSort()
    {
        // Arrange — verify the stub received a query with Sort=Updated
        PackageSortOrder? capturedSort = null;
        var stub = new CapturingSortStub(s => capturedSort = s,
            StubRegistryClient.WithPackages([StubRegistryClient.SamplePackage()]));

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        await client.GetAsync("/");

        // Assert
        Assert.Equal(PackageSortOrder.Updated, capturedSort);
    }

    // ── Empty results ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HomePage_EmptyResults_Returns200_AndRendersEmptyState()
    {
        // Arrange
        var stub = new StubRegistryClient(); // default: empty search result

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("No packages found", html);
    }

    // ── Registry 5xx → 502 ────────────────────────────────────────────────────

    [Fact]
    public async Task HomePage_RegistryServerError_Returns502_AndRendersErrorMessage()
    {
        // Arrange
        var stub = StubRegistryClient.WithSearchServerError();

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // Act
        var response = await client.GetAsync("/");

        // Assert — page renders (not an unhandled exception), status is 502
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("unavailable", html);
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

    /// <summary>
    /// A <see cref="IRegistryClient"/> decorator that captures the sort order passed to
    /// <see cref="SearchAsync"/> for assertion, then delegates to an inner stub.
    /// </summary>
    private sealed class CapturingSortStub : IRegistryClient
    {
        private readonly System.Action<PackageSortOrder> _capture;
        private readonly IRegistryClient _inner;

        public CapturingSortStub(System.Action<PackageSortOrder> capture, IRegistryClient inner)
        {
            _capture = capture;
            _inner = inner;
        }

        public System.Threading.Tasks.Task<PagedResponse<PackageSummaryResponse>> SearchAsync(
            SearchQuery query,
            System.Threading.CancellationToken cancellationToken = default)
        {
            _capture(query.sort);
            return _inner.SearchAsync(query, cancellationToken);
        }

        public System.Threading.Tasks.Task<PackageDetailResponse?> GetPackageAsync(string scope, string name,
            System.Threading.CancellationToken cancellationToken = default)
            => _inner.GetPackageAsync(scope, name, cancellationToken);

        public System.Threading.Tasks.Task<PagedResponse<VersionDetailResponse>?> GetVersionsAsync(
            string scope, string name, VersionsQuery query,
            System.Threading.CancellationToken cancellationToken = default)
            => _inner.GetVersionsAsync(scope, name, query, cancellationToken);

        public System.Threading.Tasks.Task<ReadmeResponse?> GetReadmeAsync(string scope, string name,
            System.Threading.CancellationToken cancellationToken = default)
            => _inner.GetReadmeAsync(scope, name, cancellationToken);

        public System.Threading.Tasks.Task<VersionDetailResponse?> GetVersionAsync(string scope, string name,
            string version, System.Threading.CancellationToken cancellationToken = default)
            => _inner.GetVersionAsync(scope, name, version, cancellationToken);

        public System.Threading.Tasks.Task<DiscoveryResponse> GetDiscoveryAsync(
            System.Threading.CancellationToken cancellationToken = default)
            => _inner.GetDiscoveryAsync(cancellationToken);
    }
}
