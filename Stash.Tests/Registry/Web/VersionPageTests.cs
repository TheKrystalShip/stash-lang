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
/// Integration tests for the version detail page (<c>GET /packages/@{scope}/{name}/v/{version}</c>).
/// </summary>
public sealed class VersionPageTests
{
    // ── 200 — successful render ───────────────────────────────────────────────

    [Fact]
    public async Task VersionPage_WithValidVersion_Returns200AndRendersMetadata()
    {
        // Arrange — a version with all Bucket-A fields populated
        var versionDetail = new VersionDetailResponse
        {
            Version = "1.2.3",
            PublishedAt = "2026-06-04T10:00:00Z",
            PublishedBy = "alice",
            StashVersion = ">=1.0.0",
            Integrity = "sha256-abcdef1234567890",
            Dependencies = new Dictionary<string, object>
            {
                ["@core/utils"] = "^2.0.0",
                ["@core/types"] = "~1.5.0",
            },
            Deprecated = false,
        };

        var stub = new StubRegistryClient { GetVersionResult = versionDetail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@my-org/my-lib/v/1.2.3");

        // Assert — 200
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        // Version string rendered
        Assert.Contains("1.2.3", html);

        // publishedAt
        Assert.Contains("2026-06-04T10:00:00Z", html);

        // publishedBy
        Assert.Contains("alice", html);

        // stashVersion — Razor HTML-encodes '>' as '&gt;' so we check the encoded form
        Assert.Contains("&gt;=1.0.0", html);

        // integrity
        Assert.Contains("sha256-abcdef1234567890", html);

        // dependencies
        Assert.Contains("@core/utils", html);
        Assert.Contains("^2.0.0", html);
        Assert.Contains("@core/types", html);
        Assert.Contains("~1.5.0", html);

        // Package name present (breadcrumb / heading)
        Assert.Contains("@my-org/my-lib", html);
    }

    [Fact]
    public async Task VersionPage_WithNoDependencies_ShowsEmptyState()
    {
        // Arrange — version with empty dependencies dict
        var versionDetail = StubRegistryClient.SampleVersionDetail(
            version: "2.0.0",
            dependencies: new Dictionary<string, object>());

        var stub = new StubRegistryClient { GetVersionResult = versionDetail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@org/pkg/v/2.0.0")).Content.ReadAsStringAsync();

        // Empty-state label
        Assert.Contains("No dependencies.", html);
    }

    // ── Deprecated version ────────────────────────────────────────────────────

    [Fact]
    public async Task VersionPage_DeprecatedVersion_ShowsDeprecationBanner()
    {
        // Arrange
        var versionDetail = StubRegistryClient.SampleVersionDetail(
            version: "1.0.0",
            deprecated: true,
            deprecationMessage: "Use version 2.0.0 instead.");

        var stub = new StubRegistryClient { GetVersionResult = versionDetail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@my-org/my-lib/v/1.0.0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        // Deprecation banner shown
        Assert.Contains("Deprecated", html);
        Assert.Contains("Use version 2.0.0 instead.", html);
        Assert.Contains("deprecation-banner", html);
    }

    [Fact]
    public async Task VersionPage_NonDeprecatedVersion_DoesNotShowDeprecationBanner()
    {
        // Arrange
        var versionDetail = StubRegistryClient.SampleVersionDetail(version: "1.0.0", deprecated: false);

        var stub = new StubRegistryClient { GetVersionResult = versionDetail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@my-org/my-lib/v/1.0.0")).Content.ReadAsStringAsync();

        // No deprecation banner
        Assert.DoesNotContain("deprecation-banner", html);
    }

    // ── Registry 404 → website 404 ────────────────────────────────────────────

    [Fact]
    public async Task VersionPage_RegistryReturns404_ReturnsWebsite404()
    {
        // Arrange — null simulates registry 404
        var stub = new StubRegistryClient { GetVersionResult = null };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@org/pkg/v/9.9.9");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Not Found", html);
    }

    // ── Registry 5xx → website 502 ────────────────────────────────────────────

    [Fact]
    public async Task VersionPage_RegistryReturns5xx_ReturnsWebsite502()
    {
        // Arrange — throw simulates a 5xx
        var stub = new StubRegistryClient
        {
            GetVersionException = new RegistryClientException(System.Net.HttpStatusCode.ServiceUnavailable),
        };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@org/pkg/v/1.0.0");

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("unreachable", html);
    }

    // ── Registry 400 → website 400 ────────────────────────────────────────────

    [Fact]
    public async Task VersionPage_RegistryReturns400_Returns400WithValidationMessage()
    {
        // Arrange — registry rejects with 400 InvalidRequest
        var stub = new StubRegistryClient
        {
            GetVersionException = new RegistryClientException(
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
        var response = await client.GetAsync("/packages/@org/pkg/v/1.0.0");

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
