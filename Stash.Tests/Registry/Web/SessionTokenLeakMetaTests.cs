using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Pages;
using Stash.Tests.Registry.Web.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Meta-test enforcing the C4 session-token-never-in-browser invariant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two complementary checks:</b>
/// <list type="number">
///   <item>
///     <b>Roslyn text scan</b> — scans <c>.cshtml</c> files and <c>Areas/**/*.cshtml.cs</c>
///     files for any reference to the literal string <c>PublishTokenJwt</c>, which is the
///     property name on <see cref="BffSession"/> that holds the JWT. Any such reference in a
///     view or page-model is a potential leak path.
///   </item>
///   <item>
///     <b>Runtime WAF walk</b> — starts a <see cref="WebApplicationFactory{TEntryPoint}"/>
///     with a fixture session whose <c>PublishTokenJwt</c> is a recognisable sentinel value,
///     GETs every authed page (<c>/dashboard</c>), and asserts the sentinel JWT never
///     appears in the response body or in any <c>Set-Cookie</c> header.
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Fail-path fixture:</b> <see cref="SessionLeakFailPathFixture"/> provides known-bad and
/// known-good snippets for the text scanner so a vacuous pass is impossible.
/// </para>
/// </remarks>
public sealed class SessionTokenLeakMetaTests
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// The property name whose presence in any view or area page-model file is a violation.
    /// </summary>
    private const string LeakIdentifier = "PublishTokenJwt";

    /// <summary>
    /// Minimum number of source files that must be scanned.
    /// Guards against a vacuous pass when path discovery regresses.
    /// </summary>
    private const int MinScannedFiles = 1;

    // ── Repo-root / source-dir discovery ─────────────────────────────────────

    private static string FindWebSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Stash.Registry.Web", "Stash.Registry.Web.csproj");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "Stash.Registry.Web");
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find Stash.Registry.Web/Stash.Registry.Web.csproj — test must run from within the repo.");
    }

    // ── Roslyn text scanner ───────────────────────────────────────────────────

    /// <summary>
    /// Scans <c>.cshtml</c> files and <c>*.cshtml.cs</c> files in the Areas tree for
    /// references to <c>PublishTokenJwt</c>. Returns violations and the file count.
    /// </summary>
    private static (List<string> Violations, int ScannedFiles) ScanForLeaks(string sourceDir)
    {
        var violations = new List<string>();

        // Collect .cshtml and .cshtml.cs files in the Pages/ and Areas/ trees.
        var filesToScan = new List<string>();

        // All .cshtml files under Pages/ and Areas/
        foreach (var dir in new[] { "Pages", "Areas" })
        {
            var fullDir = Path.Combine(sourceDir, dir);
            if (Directory.Exists(fullDir))
            {
                filesToScan.AddRange(
                    Directory.EnumerateFiles(fullDir, "*.cshtml", SearchOption.AllDirectories));
                filesToScan.AddRange(
                    Directory.EnumerateFiles(fullDir, "*.cshtml.cs", SearchOption.AllDirectories));
            }
        }

        foreach (var filePath in filesToScan)
        {
            string content = File.ReadAllText(filePath);
            if (content.Contains(LeakIdentifier, StringComparison.Ordinal))
            {
                string relativePath = filePath.Substring(sourceDir.Length)
                    .TrimStart(Path.DirectorySeparatorChar, '/');

                // Find which lines contain the leak identifier.
                var lines = content.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(LeakIdentifier, StringComparison.Ordinal))
                    {
                        violations.Add($"{relativePath}:{i + 1} — references '{LeakIdentifier}'");
                    }
                }
            }
        }

        return (violations, filesToScan.Count);
    }

    /// <summary>
    /// Scans an in-memory snippet for references to <see cref="LeakIdentifier"/>.
    /// Used by self-tests to prove the scanner has teeth.
    /// </summary>
    internal static List<string> ScanSnippet(string snippet, string label)
    {
        var violations = new List<string>();
        if (snippet.Contains(LeakIdentifier, StringComparison.Ordinal))
        {
            violations.Add($"{label} — references '{LeakIdentifier}'");
        }
        return violations;
    }

    // ── Production Roslyn scan ────────────────────────────────────────────────

    /// <summary>
    /// <b>Load-bearing (1 of 2).</b> Scans all <c>.cshtml</c> and <c>*.cshtml.cs</c>
    /// files in <c>Pages/</c> and <c>Areas/</c> for references to <c>PublishTokenJwt</c>.
    /// </summary>
    [Fact]
    public void NoViewOrPageModel_ReferencesPublishTokenJwt()
    {
        string sourceDir = FindWebSourceDir();
        var (violations, scannedFiles) = ScanForLeaks(sourceDir);

        // ── File-count floor ──────────────────────────────────────────────────
        Assert.True(
            scannedFiles >= MinScannedFiles,
            $"Only {scannedFiles} .cshtml / .cshtml.cs file(s) scanned under '{sourceDir}' " +
            $"(expected >= {MinScannedFiles}). Path discovery likely regressed.");

        // ── Primary compliance assertion ──────────────────────────────────────
        Assert.True(
            violations.Count == 0,
            $"{violations.Count} reference(s) to '{LeakIdentifier}' found in view / page-model files.\n" +
            "The session's publish JWT must never be referenced from any .cshtml or page-model file.\n\n" +
            string.Join("\n", violations));
    }

    // ── Runtime WAF walk ──────────────────────────────────────────────────────

    // A recognisable sentinel that must never appear in any response body or Set-Cookie header.
    private const string SentinelJwt = "SENTINEL_JWT_THAT_MUST_NEVER_REACH_BROWSER_XYZ123";
    private const string FixtureSessionId = "test-session-id-leak-meta";

    private static WebApplicationFactory<HealthModel> CreateFactory()
    {
        return new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");

            builder.ConfigureTestServices(services =>
            {
                // Seed the session store with a session whose JWT is the recognisable sentinel.
                services.AddSingleton<ISessionStore>(sp =>
                {
                    var store = new InMemorySessionStore();
                    var session = new BffSession
                    {
                        Username = "leak-test-user",
                        PublishTokenJwt = SentinelJwt,
                        PublishTokenId = "tok-leak-001",
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
                    };
                    store.SetAsync(FixtureSessionId, session, DateTimeOffset.UtcNow.AddHours(8))
                         .GetAwaiter().GetResult();
                    return store;
                });

                // Replace the authenticated registry client with a stub (no real registry needed).
                services.AddScoped<IAuthenticatedRegistryClient>(_ =>
                    new StubAuthenticatedRegistryClient());
            });
        });
    }

    /// <summary>
    /// <b>Load-bearing (2 of 2).</b> Walks <c>/dashboard</c> as an authenticated user
    /// and asserts the sentinel JWT never appears in the response body or any
    /// <c>Set-Cookie</c> header.
    /// </summary>
    [Fact]
    public async Task AuthedPages_NeverEmitPublishJwtInBodyOrCookie()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}");

        // Walk the authed pages available in A2.
        var authedUrls = new[] { "/dashboard" };

        foreach (var url in authedUrls)
        {
            var response = await client.GetAsync(url);

            // Must be a successful response (the session is valid).
            Assert.True(
                response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Redirect,
                $"Unexpected status {response.StatusCode} for {url}. Expected 200 or 302.");

            // ── Body must not contain the sentinel JWT ─────────────────────────
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(
                SentinelJwt,
                body,
                StringComparison.Ordinal);

            // ── Set-Cookie headers must not contain the sentinel JWT ────────────
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
            {
                foreach (var cookie in setCookieHeaders)
                {
                    Assert.DoesNotContain(
                        SentinelJwt,
                        cookie,
                        StringComparison.Ordinal);
                }
            }
        }
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    [Fact]
    public void Scanner_SnippetWithPublishTokenJwt_FlagsViolation()
    {
        var violations = ScanSnippet(
            SessionLeakFailPathFixture.RoguePublishTokenJwtReferenceInCSharp,
            "rogue-cs-snippet");

        Assert.True(
            violations.Count >= 1,
            $"Expected at least 1 violation for the rogue snippet, but got {violations.Count}.");
    }

    [Fact]
    public void Scanner_CleanSnippet_NoViolation()
    {
        var violations = ScanSnippet(
            SessionLeakFailPathFixture.CleanPageModelSnippet,
            "clean-cs-snippet");

        Assert.Empty(violations);
    }

    [Fact]
    public void Scanner_RazorSnippetWithPublishTokenJwt_FlagsViolation()
    {
        var violations = ScanSnippet(
            SessionLeakFailPathFixture.RoguePublishTokenJwtInRazor,
            "rogue-razor-snippet");

        Assert.True(
            violations.Count >= 1,
            $"Expected at least 1 violation for the rogue Razor snippet, but got {violations.Count}.");
    }

    [Fact]
    public void Scanner_CleanRazorSnippet_NoViolation()
    {
        var violations = ScanSnippet(
            SessionLeakFailPathFixture.CleanRazorSnippet,
            "clean-razor-snippet");

        Assert.Empty(violations);
    }
}
