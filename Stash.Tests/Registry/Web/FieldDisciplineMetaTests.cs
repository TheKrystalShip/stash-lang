using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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
/// Field-discipline guard: asserts that no Bucket-B label vocabulary appears
/// anywhere in the rendered HTML of the home and search pages.
/// </summary>
/// <remarks>
/// <para>
/// The Bucket-A DTOs (<see cref="PackageSummaryResponse"/>, <see cref="PackageDetailResponse"/>,
/// <see cref="VersionDetailResponse"/>) do not carry Bucket-B fields such as
/// <c>downloads</c>, <c>dependents</c>, <c>vulnerable</c>, etc., so a data-bound Bucket-B
/// value cannot compile. However, a view template could still hardcode placeholder chrome
/// (e.g. <c>Downloads: —</c>). This test catches that case by scanning the rendered HTML for
/// the forbidden label tokens.
/// </para>
/// <para>
/// <b>Binding floor:</b> the test asserts at least one package card rendered before running
/// the forbidden-label scan. If the page is empty, a missing card does not imply the labels
/// are absent — it might mean the test rendered nothing and gave a vacuous pass.
/// </para>
/// <para>
/// <b>Fail-path fixture:</b> <see cref="ForbiddenLabelScan_DetectsViolation_InKnownBadHtml"/>
/// proves the scanner trips on a known-bad HTML fragment, so "0 violations because
/// nothing bound" fails loudly.
/// </para>
/// </remarks>
public sealed class FieldDisciplineMetaTests
{
    // ── Forbidden Bucket-B label vocabulary ──────────────────────────────────

    /// <summary>
    /// The closed set of Bucket-B label tokens that must never appear in rendered HTML.
    /// Each token is matched with word-boundary anchors (<c>\b…\b</c>, case-insensitive)
    /// to avoid false positives on substrings (e.g. "Designed" containing "signed").
    /// </summary>
    internal static readonly IReadOnlyList<string> ForbiddenTokens =
    [
        "Downloads",
        "Dependents",
        "Used by",
        "Vulnerabilities",
        "Provenance",
        "Verified publisher",
        "Signed",
    ];

    // ── Core scanner (pure helper, backed by fail-path self-test) ────────────

    /// <summary>
    /// Scans <paramref name="html"/> for forbidden Bucket-B label tokens.
    /// Returns the list of tokens found (empty = clean).
    /// </summary>
    internal static IReadOnlyList<string> FindForbiddenTokens(string html)
    {
        return ForbiddenTokens
            .Where(token =>
            {
                // Word-boundary match, case-insensitive.
                // "Signed" must not match inside "Designed" etc.
                var pattern = $@"\b{Regex.Escape(token)}\b";
                return Regex.IsMatch(html, pattern, RegexOptions.IgnoreCase);
            })
            .ToList();
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Verifies the scanner trips on a known-bad HTML fragment that contains each
    /// forbidden token. This is the fail-path fixture — proves the guard is not vacuous.
    /// </summary>
    [Fact]
    public void ForbiddenLabelScan_DetectsViolation_InKnownBadHtml()
    {
        // A fragment that deliberately includes every forbidden token:
        const string badHtml = """
            <div class="stats">
                <span>Downloads: 12,345</span>
                <span>Dependents: 87</span>
                <span>Used by 42 packages</span>
                <span>Vulnerabilities: 0</span>
                <span>Provenance: verified</span>
                <span>Verified publisher</span>
                <span>Signed package</span>
            </div>
            """;

        var found = FindForbiddenTokens(badHtml);

        Assert.True(
            found.Count == ForbiddenTokens.Count,
            $"Expected the scanner to flag all {ForbiddenTokens.Count} forbidden tokens in the bad HTML " +
            $"fragment, but only flagged {found.Count}: [{string.Join(", ", found)}]. " +
            "The field-discipline scan has lost its teeth for the un-flagged tokens.");
    }

    /// <summary>
    /// Verifies the scanner passes on a clean HTML fragment that contains none of the
    /// forbidden tokens (but may contain benign words like "Designed").
    /// </summary>
    [Fact]
    public void ForbiddenLabelScan_PassesCleanHtml()
    {
        const string cleanHtml = """
            <div class="package-card">
                <a class="package-card-name" href="/packages/@org/pkg">org/pkg</a>
                <p class="package-card-description">A well-designed package.</p>
                <dl>
                    <dd class="package-card-license">MIT</dd>
                    <dd class="package-card-owners">1 owner</dd>
                    <dd class="package-card-updated">Updated 2026-06-04T12:00:00Z</dd>
                </dl>
            </div>
            """;

        var found = FindForbiddenTokens(cleanHtml);

        Assert.Empty(found);
    }

    /// <summary>
    /// "Signed" must NOT match "Designed" — the word-boundary anchor must be tight enough.
    /// </summary>
    [Fact]
    public void ForbiddenLabelScan_DoesNotFalsePositive_OnSubstringMatch()
    {
        const string htmlWithDesigned = "<p>A beautifully designed package.</p>";

        var found = FindForbiddenTokens(htmlWithDesigned);

        Assert.Empty(found);
    }

    // ── Live WAF HTML walks ───────────────────────────────────────────────────

    /// <summary>
    /// Home page (<c>GET /</c>) with populated package cards — no Bucket-B label
    /// vocabulary appears in the rendered HTML.
    /// </summary>
    [Fact]
    public async Task HomePage_WithPackages_ContainsNoBucketBLabels()
    {
        // Arrange — at least one card so the binding floor passes
        var stub = StubRegistryClient.WithPackages([
            StubRegistryClient.SamplePackage("org/alpha", "Alpha package"),
            StubRegistryClient.SamplePackage("org/beta", "Beta package"),
        ]);

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // ── Binding floor: at least one card rendered ─────────────────────────
        Assert.Contains("org/alpha", html);

        // ── Forbidden label scan ──────────────────────────────────────────────
        var violations = FindForbiddenTokens(html);
        Assert.True(
            violations.Count == 0,
            $"Home page HTML contains forbidden Bucket-B label(s): [{string.Join(", ", violations)}]. " +
            "Remove all Bucket-B placeholder chrome from the page template and card partial.");
    }

    /// <summary>
    /// Search page (<c>GET /search</c>) with populated package cards — no Bucket-B label
    /// vocabulary appears in the rendered HTML.
    /// </summary>
    [Fact]
    public async Task SearchPage_WithPackages_ContainsNoBucketBLabels()
    {
        // Arrange — at least one card so the binding floor passes
        var stub = StubRegistryClient.WithPackages([
            StubRegistryClient.SamplePackage("org/gamma", "Gamma package"),
        ]);

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/search?q=test");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // ── Binding floor: at least one card rendered ─────────────────────────
        Assert.Contains("org/gamma", html);

        // ── Forbidden label scan ──────────────────────────────────────────────
        var violations = FindForbiddenTokens(html);
        Assert.True(
            violations.Count == 0,
            $"Search page HTML contains forbidden Bucket-B label(s): [{string.Join(", ", violations)}]. " +
            "Remove all Bucket-B placeholder chrome from the page template and card partial.");
    }

    /// <summary>
    /// Package detail page (<c>GET /packages/@{scope}/{name}</c>) with a populated package —
    /// no Bucket-B label vocabulary appears in the rendered HTML.
    /// </summary>
    [Fact]
    public async Task PackageDetailPage_WithPackage_ContainsNoBucketBLabels()
    {
        // Arrange — populate GetPackageResult so the binding floor passes
        var detail = StubRegistryClient.SamplePackageDetail(
            scope: "org",
            pkgName: "alpha",
            description: "Field discipline test package");

        var stub = new StubRegistryClient { GetPackageResult = detail };

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@org/alpha");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // ── Binding floor: description rendered (non-vacuous) ─────────────────
        Assert.Contains("Field discipline test package", html);

        // ── Forbidden label scan ──────────────────────────────────────────────
        var violations = FindForbiddenTokens(html);
        Assert.True(
            violations.Count == 0,
            $"Package detail page HTML contains forbidden Bucket-B label(s): [{string.Join(", ", violations)}]. " +
            "Remove all Bucket-B placeholder chrome from the page template and partials.");
    }

    /// <summary>
    /// Package detail page (<c>GET /packages/@{scope}/{name}</c>) with a populated README —
    /// the README content is rendered through the sanitizer, and no Bucket-B label vocabulary
    /// appears anywhere in the rendered HTML (including inside the README output).
    /// </summary>
    /// <remarks>
    /// The README content used here is deliberately neutral (no forbidden tokens).
    /// This test exercises the fully-populated README code path, not the empty-state path.
    /// </remarks>
    [Fact]
    public async Task PackageDetailPage_WithPopulatedReadme_ContainsNoBucketBLabels()
    {
        // Arrange — package + a README with neutral content (no Bucket-B tokens)
        var detail = StubRegistryClient.SamplePackageDetail(
            scope: "org",
            pkgName: "readme-test",
            description: "Field discipline README test");

        var stub = new StubRegistryClient
        {
            GetPackageResult = detail,
            GetReadmeResult = new ReadmeResponse
            {
                Content = "# Installation\n\nRun `stash pkg add @org/readme-test` to install.\n\n## Usage\n\nSimple and effective.",
                ContentType = ReadmeContentTypes.Markdown,
                ByteSize = 90,
                ExtractedFromVersion = "1.0.0",
            },
        };

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@org/readme-test");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // ── Binding floor: README content rendered (non-vacuous) ─────────────
        Assert.Contains("Installation", html);

        // ── Forbidden label scan ──────────────────────────────────────────────
        var violations = FindForbiddenTokens(html);
        Assert.True(
            violations.Count == 0,
            $"Package detail page HTML (with populated README) contains forbidden Bucket-B label(s): " +
            $"[{string.Join(", ", violations)}]. " +
            "The README content or surrounding template contains Bucket-B placeholder chrome.");
    }

    /// <summary>
    /// Version detail page (<c>GET /packages/@{scope}/{name}/v/{version}</c>) with a populated
    /// version — no Bucket-B label vocabulary appears in the rendered HTML.
    /// </summary>
    [Fact]
    public async Task VersionDetailPage_WithVersion_ContainsNoBucketBLabels()
    {
        // Arrange — a version detail response for the binding floor
        var versionDetail = StubRegistryClient.SampleVersionDetail(
            version: "1.0.0",
            publishedBy: "field-discipline-test-user");

        var stub = new StubRegistryClient { GetVersionResult = versionDetail };

        using var factory = CreateFactory(stub);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/packages/@org/alpha/v/1.0.0");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // ── Binding floor: publisher rendered (non-vacuous) ───────────────────
        Assert.Contains("field-discipline-test-user", html);

        // ── Forbidden label scan ──────────────────────────────────────────────
        var violations = FindForbiddenTokens(html);
        Assert.True(
            violations.Count == 0,
            $"Version detail page HTML contains forbidden Bucket-B label(s): [{string.Join(", ", violations)}]. " +
            "Remove all Bucket-B placeholder chrome from the page template and partials.");
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
