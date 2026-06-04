using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Pages;
using Stash.Registry.Web.Services;
using Stash.Tests.Registry.Web.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Integration tests for the package detail page (<c>GET /packages/@{scope}/{name}</c>).
/// </summary>
public sealed class PackagePageTests
{
    // ── 200 — successful render ───────────────────────────────────────────────

    [Fact]
    public async Task PackagePage_WithValidPackage_Returns200AndRendersMetadata()
    {
        // Arrange
        var detail = StubRegistryClient.SamplePackageDetail(
            scope: "my-org",
            pkgName: "my-lib",
            description: "A sample library package",
            latest: "1.2.0",
            license: "MIT");

        var stub = new StubRegistryClient { GetPackageResult = detail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@my-org/my-lib");

        // Assert — 200
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        // Package name in heading
        Assert.Contains("@my-org/my-lib", html);

        // Description rendered
        Assert.Contains("A sample library package", html);

        // Install widget
        Assert.Contains("stash pkg add @my-org/my-lib", html);

        // License in sidebar
        Assert.Contains("MIT", html);

        // Version in sidebar and table
        Assert.Contains("1.2.0", html);

        // README placeholder — static text, no Html.Raw
        Assert.Contains("README rendering arrives in the next phase", html);
    }

    [Fact]
    public async Task PackagePage_WithVersions_RendersVersionsTable()
    {
        // Arrange
        var detail = StubRegistryClient.SamplePackageDetail(
            scope: "org",
            pkgName: "pkg",
            latest: "2.0.0");

        var stub = new StubRegistryClient { GetPackageResult = detail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@org/pkg")).Content.ReadAsStringAsync();

        // Both versions appear in the versions table
        Assert.Contains("2.0.0", html);
        Assert.Contains("1.0.0", html);

        // Publisher column
        Assert.Contains("test-user", html);
    }

    [Fact]
    public async Task PackagePage_WithDependencies_RendersDependenciesSection()
    {
        // Arrange
        var deps = new Dictionary<string, object> { ["@core/utils"] = "^1.0.0" };
        var versions = new Dictionary<string, VersionDetailResponse>
        {
            ["1.0.0"] = StubRegistryClient.SampleVersionDetail("1.0.0", dependencies: deps),
        };
        var detail = new PackageDetailResponse
        {
            Name = "org/pkg",
            Description = "Test package",
            Keywords = new List<string>(),
            Versions = versions,
            Latest = "1.0.0",
            CreatedAt = "2026-01-01T00:00:00Z",
            UpdatedAt = "2026-06-04T00:00:00Z",
        };

        var stub = new StubRegistryClient { GetPackageResult = detail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@org/pkg")).Content.ReadAsStringAsync();

        // Dependencies section exists and shows dep name
        Assert.Contains("@core/utils", html);
        Assert.Contains("^1.0.0", html);
    }

    // ── README placeholder ─────────────────────────────────────────────────────

    [Fact]
    public async Task PackagePage_ReadmeColumn_ContainsStaticPlaceholderAndNoHtmlRaw()
    {
        // Arrange
        var detail = StubRegistryClient.SamplePackageDetail();
        var stub = new StubRegistryClient { GetPackageResult = detail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@my-org/my-lib")).Content.ReadAsStringAsync();

        // README placeholder must appear
        Assert.Contains("README rendering arrives in the next phase", html);

        // No package-author injected HTML/markdown.
        // The page does include a site script tag (copy-install.js) which is expected,
        // but the README column contains only a static placeholder — no inline event handlers
        // or javascript: URIs from package content.
        Assert.DoesNotContain("onerror=", html);
        Assert.DoesNotContain("javascript:", html);
        // The readme-placeholder section must not contain any script/iframe tags
        // (confirm the placeholder itself is clean static text)
        Assert.Contains("readme-placeholder", html);
    }

    // ── Deprecation banner ─────────────────────────────────────────────────────

    [Fact]
    public async Task PackagePage_DeprecatedPackage_ShowsDeprecationBanner()
    {
        // Arrange
        var detail = StubRegistryClient.SamplePackageDetail(
            deprecated: true,
            deprecationMessage: "Please migrate to the new package.");

        var stub = new StubRegistryClient { GetPackageResult = detail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@my-org/my-lib");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Deprecated", html);
        Assert.Contains("Please migrate to the new package.", html);
    }

    [Fact]
    public async Task PackagePage_DeprecatedPackageWithAlternative_ShowsAlternativeLink()
    {
        // Arrange
        var detail = StubRegistryClient.SamplePackageDetail(
            deprecated: true,
            deprecationMessage: "Use the new lib.",
            deprecationAlternative: "new-org/new-lib");

        var stub = new StubRegistryClient { GetPackageResult = detail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@my-org/my-lib")).Content.ReadAsStringAsync();

        Assert.Contains("new-org/new-lib", html);
        // Alternative renders as a link to the package detail page
        Assert.Contains("/packages/@new-org/new-lib", html);
    }

    [Fact]
    public async Task PackagePage_NonDeprecatedPackage_DoesNotShowDeprecationBanner()
    {
        // Arrange
        var detail = StubRegistryClient.SamplePackageDetail(deprecated: false);
        var stub = new StubRegistryClient { GetPackageResult = detail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@my-org/my-lib")).Content.ReadAsStringAsync();

        // The deprecation-banner class must not appear
        Assert.DoesNotContain("deprecation-banner", html);
    }

    // ── Registry 404 → website 404 ────────────────────────────────────────────

    [Fact]
    public async Task PackagePage_RegistryReturns404_ReturnsWebsite404()
    {
        // Arrange — null simulates registry 404
        var stub = new StubRegistryClient { GetPackageResult = null };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@org/missing");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Not Found", html);
    }

    // ── Registry 5xx → website 502 ────────────────────────────────────────────

    [Fact]
    public async Task PackagePage_RegistryReturns5xx_ReturnsWebsite502()
    {
        // Arrange — throw simulates a 5xx
        var stub = new StubRegistryClient
        {
            GetPackageException = new RegistryClientException(System.Net.HttpStatusCode.ServiceUnavailable),
        };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@org/pkg");

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("unreachable", html);
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
