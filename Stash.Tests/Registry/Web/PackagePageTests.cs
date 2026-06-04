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

        // README is null (stub returns null) → empty-state shown
        Assert.Contains("This package has no README.", html);
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

    // ── README column — P5 sanitized rendering ─────────────────────────────────

    [Fact]
    public async Task PackagePage_ReadmeColumn_WithNoReadme_ShowsEmptyState()
    {
        // Arrange — GetReadmeResult is null (default), simulating no README
        var detail = StubRegistryClient.SamplePackageDetail();
        var stub = new StubRegistryClient { GetPackageResult = detail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@my-org/my-lib")).Content.ReadAsStringAsync();

        // Empty-state message shown when README is absent
        Assert.Contains("This package has no README.", html);
        Assert.Contains("readme-empty-state", html);

        // No hostile content from package README (there is none to inject)
        Assert.DoesNotContain("onerror=", html);
        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public async Task PackagePage_ReadmeColumn_WithReadme_RendersContent()
    {
        // Arrange — provide a README with benign content
        var detail = StubRegistryClient.SamplePackageDetail();
        var stub = new StubRegistryClient
        {
            GetPackageResult = detail,
            GetReadmeResult = new Stash.Registry.Contracts.ReadmeResponse
            {
                Content = "# My Package\n\nA helpful description.",
                ContentType = Stash.Registry.Contracts.ReadmeContentTypes.Markdown,
                ByteSize = 40,
                ExtractedFromVersion = "1.0.0",
            },
        };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@my-org/my-lib")).Content.ReadAsStringAsync();

        // README content rendered (heading → <h1>, paragraph text)
        Assert.Contains("My Package", html);
        Assert.Contains("A helpful description.", html);

        // Not the empty-state
        Assert.DoesNotContain("This package has no README.", html);

        // README content class present
        Assert.Contains("readme-content", html);
    }

    [Fact]
    public async Task PackagePage_ReadmeColumn_WithHostileReadme_StripsScriptTags()
    {
        // Arrange — README containing a script injection attempt
        var detail = StubRegistryClient.SamplePackageDetail();
        var stub = new StubRegistryClient
        {
            GetPackageResult = detail,
            GetReadmeResult = new Stash.Registry.Contracts.ReadmeResponse
            {
                Content = "Hello <script>alert('xss')</script> world",
                ContentType = Stash.Registry.Contracts.ReadmeContentTypes.Markdown,
                ByteSize = 44,
                ExtractedFromVersion = "1.0.0",
            },
        };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@my-org/my-lib")).Content.ReadAsStringAsync();

        // The README content must not have introduced a live INLINE <script> opening tag.
        // Markdig DisableHtml() encodes <script> to &lt;script&gt; (inert text).
        // The page legitimately includes a <script src="..."> (external reference for copy-install.js)
        // which is distinct: it uses an attribute. We only check for the dangerous inline open form.
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
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

    // ── Repository URL — XSS gate (F01) ──────────────────────────────────────

    [Fact]
    public async Task PackagePage_RepositoryWithJavascriptScheme_DoesNotRenderAsHref()
    {
        // Arrange — malicious package-authored repository URL
        var detail = new PackageDetailResponse
        {
            Name = "org/pkg",
            Description = "Test",
            Keywords = new System.Collections.Generic.List<string>(),
            Versions = new System.Collections.Generic.Dictionary<string, VersionDetailResponse>(),
            Latest = null,
            CreatedAt = "2026-01-01T00:00:00Z",
            UpdatedAt = "2026-06-04T00:00:00Z",
            Repository = "javascript:alert(1)",
        };

        var stub = new StubRegistryClient { GetPackageResult = detail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@org/pkg")).Content.ReadAsStringAsync();

        // The javascript: URL must NEVER appear as an href value — the gate must strip it.
        Assert.DoesNotContain("href=\"javascript:", html, StringComparison.OrdinalIgnoreCase);

        // The URL text itself may appear as plain (Razor-encoded) visible text, which is harmless;
        // what must be absent is the attribute that would make it an executable link.
    }

    [Fact]
    public async Task PackagePage_RepositoryWithHttpsUrl_RendersAsClickableLink()
    {
        // Arrange — legitimate repository URL
        var detail = new PackageDetailResponse
        {
            Name = "org/pkg",
            Description = "Test",
            Keywords = new System.Collections.Generic.List<string>(),
            Versions = new System.Collections.Generic.Dictionary<string, VersionDetailResponse>(),
            Latest = null,
            CreatedAt = "2026-01-01T00:00:00Z",
            UpdatedAt = "2026-06-04T00:00:00Z",
            Repository = "https://github.com/org/repo",
        };

        var stub = new StubRegistryClient { GetPackageResult = detail };
        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var html = await (await client.GetAsync("/packages/@org/pkg")).Content.ReadAsStringAsync();

        // Safe https URL must render as a clickable link (the gate passes it through).
        Assert.Contains("href=\"https://github.com/org/repo\"", html, StringComparison.OrdinalIgnoreCase);
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
