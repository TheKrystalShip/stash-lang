using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Pages;
using Stash.Registry.Web.Services;
using Stash.Tests.Registry.Web.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Integration tests for <c>GET /manage/@{scope}/{name}</c> and its POST handlers.
/// </summary>
/// <remarks>
/// <para>
/// Tests use <see cref="WebApplicationFactory{TEntryPoint}"/> with:
/// <list type="bullet">
///   <item>A fixture session seeded into the singleton <see cref="ISessionStore"/>.</item>
///   <item>The <see cref="StubAuthenticatedRegistryClient"/> stub replacing the real DI registration.</item>
///   <item>The session cookie sent on requests so the auth handler populates <c>User</c>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ManagePageTests
{
    private const string FixtureSessionId = "test-session-id-manage";
    private const string FixtureUsername = "alice";
    private const string FixtureJwt = "fixture.manage.jwt";
    private const string TestScope = "my-org";
    private const string TestName = "my-lib";

    private static readonly string ManageUrl = $"/manage/@{TestScope}/{TestName}";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BffSession MakeSession() => new()
    {
        Username = FixtureUsername,
        PublishTokenJwt = FixtureJwt,
        PublishTokenId = "tok-manage-001",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
    };

    private static WebApplicationFactory<HealthModel> CreateFactory(
        StubAuthenticatedRegistryClient authStub,
        IRegistryClient? anonStub = null)
    {
        return new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");

            builder.ConfigureTestServices(services =>
            {
                // Seed the session store with a fixture session.
                services.AddSingleton<ISessionStore>(sp =>
                {
                    var store = new InMemorySessionStore();
                    store.SetAsync(FixtureSessionId, MakeSession(), DateTimeOffset.UtcNow.AddHours(8))
                         .GetAwaiter().GetResult();
                    return store;
                });

                // Replace the authenticated registry client with the stub.
                services.AddScoped<IAuthenticatedRegistryClient>(_ => authStub);

                // Optionally replace the anonymous client too (for differential tests).
                if (anonStub is not null)
                    services.AddScoped<IRegistryClient>(_ => anonStub);
            });
        });
    }

    private static HttpClient CreateAuthenticatedHttpClient(
        WebApplicationFactory<HealthModel> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.CookieName}={FixtureSessionId}");
        return client;
    }

    /// <summary>Creates a populated <see cref="PackageDetailResponse"/> for tests.</summary>
    private static PackageDetailResponse SamplePackageDetail(
        bool deprecated = false,
        string? deprecationMessage = null) =>
        new()
        {
            Name = $"{TestScope}/{TestName}",
            Description = "A test library package.",
            License = "MIT",
            Keywords = new List<string> { "test" },
            Latest = "1.2.0",
            CreatedAt = "2026-01-01T00:00:00Z",
            UpdatedAt = "2026-06-04T12:00:00Z",
            Deprecated = deprecated,
            DeprecationMessage = deprecationMessage,
            Versions = new Dictionary<string, VersionDetailResponse>
            {
                ["1.2.0"] = new VersionDetailResponse
                {
                    Version = "1.2.0",
                    PublishedAt = "2026-06-04T10:00:00Z",
                    PublishedBy = "alice",
                    Dependencies = new Dictionary<string, object>(),
                    Integrity = "sha256-fake-1.2.0",
                },
                ["1.0.0"] = new VersionDetailResponse
                {
                    Version = "1.0.0",
                    PublishedAt = "2026-01-01T10:00:00Z",
                    PublishedBy = "alice",
                    Dependencies = new Dictionary<string, object>(),
                    Integrity = "sha256-fake-1.0.0",
                },
            },
        };

    // ── Anonymous redirect ────────────────────────────────────────────────────

    /// <summary>
    /// An anonymous <c>GET /manage/@scope/name</c> returns 302 to <c>/login?returnUrl=…</c>,
    /// NOT 500 (the DI factory throw is never reached on the anonymous path).
    /// </summary>
    [Fact]
    public async Task Manage_AnonymousRequest_Returns302ToLogin()
    {
        var stub = new StubAuthenticatedRegistryClient();
        using var factory = new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var response = await client.GetAsync(ManageUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains("/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("returnUrl", location, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ── GET: package exists ───────────────────────────────────────────────────

    [Fact]
    public async Task Manage_AuthenticatedGet_ExistingPackage_Returns200()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = SamplePackageDetail(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(ManageUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Manage_AuthenticatedGet_ExistingPackage_RendersPackageName()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = SamplePackageDetail(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(ManageUrl);
        var html = await response.Content.ReadAsStringAsync();

        // The package name should appear in the rendered page.
        Assert.Contains($"{TestScope}/{TestName}", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Manage_AuthenticatedGet_ExistingPackage_RendersAllFourControls()
    {
        // Non-deprecated package: should show deprecate-package form + visibility form + versions table.
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = SamplePackageDetail(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(ManageUrl);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Deprecate Package form
        Assert.Contains("DeprecatePackage", html, StringComparison.OrdinalIgnoreCase);

        // Visibility form
        Assert.Contains("SetVisibility", html, StringComparison.OrdinalIgnoreCase);

        // Versions table with deprecate version form
        Assert.Contains("DeprecateVersion", html, StringComparison.OrdinalIgnoreCase);
    }

    // ── GET: package not found (registry 404) ─────────────────────────────────

    [Fact]
    public async Task Manage_AuthenticatedGet_RegistryReturns404_Returns404()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = null,  // null = registry 404
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(ManageUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Manage_AuthenticatedGet_RegistryReturns404_RendersNotFoundPage()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = null,
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(ManageUrl);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Not Found", html, StringComparison.OrdinalIgnoreCase);
    }

    // ── GET: registry 401 → redirect to /login?expired=1 ─────────────────────

    [Fact]
    public async Task Manage_AuthenticatedGet_Registry401_RedirectsToLoginExpired()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageException = new RegistryClientException(HttpStatusCode.Unauthorized),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(ManageUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains("/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expired=1", location, StringComparison.OrdinalIgnoreCase);
    }

    // ── Visibility dropdown: no magic string literals ──────────────────────────

    /// <summary>
    /// The rendered visibility dropdown must contain the enum member names ("Public", "Private",
    /// "Internal") as option values — not hardcoded wire literals ("public", "private", "internal").
    /// This enforces the bounded-domain rule from C6 and the done_when grep requirement.
    /// </summary>
    [Fact]
    public async Task Manage_VisibilityDropdown_UsesEnumMemberNames_NotWireStrings()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = SamplePackageDetail(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(ManageUrl);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The visibility form must be present.
        Assert.Contains("SetVisibility", html, StringComparison.OrdinalIgnoreCase);

        // Enum member names should appear as option values.
        Assert.Contains("value=\"Public\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("value=\"Private\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("value=\"Internal\"", html, StringComparison.OrdinalIgnoreCase);

        // The literal wire strings as stand-alone option VALUES must NOT appear
        // (they may appear in label text, but not as value="..." attributes).
        // This is a belt-and-suspenders check; the done_when grep is the primary gate.
        Assert.DoesNotContain("value=\"public\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"private\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"internal\"", html, StringComparison.Ordinal);
    }

    // ── POST: deprecate package — success ─────────────────────────────────────

    [Fact]
    public async Task Manage_PostDeprecatePackage_Success_Returns200WithSuccessBanner()
    {
        var pkg = SamplePackageDetail();
        // After the action, GetPackageAsync re-reads and returns deprecated package.
        var deprecatedPkg = SamplePackageDetail(deprecated: true, deprecationMessage: "Use v2 instead.");

        var stub = new StubAuthenticatedRegistryClient
        {
            DeprecatePackageResult = new DeprecationResponse
            {
                Package = $"{TestScope}/{TestName}",
                Deprecated = true,
            },
        };
        // First call (POST handler re-reads): return deprecated state.
        stub.GetPackageResult = deprecatedPkg;

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        // First GET to obtain anti-forgery token.
        stub.GetPackageResult = pkg;
        var getResponse = await client.GetAsync(ManageUrl);
        var html = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);
        var csrfCookie = ExtractAntiForgeryCooke(getResponse);

        // POST.
        stub.GetPackageResult = deprecatedPkg;
        var formContent = new FormUrlEncodedContent(
        [
            new("DeprecationMessage", "Use v2 instead."),
            new("DeprecationAlternative", ""),
            new("__RequestVerificationToken", token),
        ]);

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/manage/@{TestScope}/{TestName}?handler=DeprecatePackage")
        {
            Content = formContent,
        };
        request.Headers.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}; {csrfCookie}");

        var response = await client.SendAsync(request);
        var responseHtml = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("deprecated successfully", responseHtml, StringComparison.OrdinalIgnoreCase);
    }

    // ── POST: deprecate package — registry 403 → inline error banner ─────────

    /// <summary>
    /// C5 — no client-side ownership check. A non-owner's POST results in a registry 403,
    /// and the BFF surfaces the registry's error message inline. The page does NOT pre-check
    /// ownership.
    /// </summary>
    [Fact]
    public async Task Manage_PostDeprecatePackage_Registry403_RendersInlineErrorBanner()
    {
        var pkg = SamplePackageDetail();
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = pkg,
            DeprecatePackageException = new RegistryClientException(
                HttpStatusCode.Forbidden,
                errorCode: "FORBIDDEN",
                errorMessage: "You do not own this package."),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        // GET to obtain anti-forgery token.
        var getResponse = await client.GetAsync(ManageUrl);
        var html = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);
        var csrfCookie = ExtractAntiForgeryCooke(getResponse);

        var formContent = new FormUrlEncodedContent(
        [
            new("DeprecationMessage", "Attempting unauthorized deprecation."),
            new("__RequestVerificationToken", token),
        ]);

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/manage/@{TestScope}/{TestName}?handler=DeprecatePackage")
        {
            Content = formContent,
        };
        request.Headers.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}; {csrfCookie}");

        var response = await client.SendAsync(request);
        var responseHtml = await response.Content.ReadAsStringAsync();

        // Page must remain 200 (not 302, not 403 from the BFF itself).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The registry's error message must appear inline.
        Assert.Contains("You do not own this package", responseHtml, StringComparison.OrdinalIgnoreCase);

        // Must NOT redirect to login (the 403 is not an expired session).
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    // ── POST: set visibility — success ────────────────────────────────────────

    [Fact]
    public async Task Manage_PostSetVisibility_Success_Returns200WithSuccessBanner()
    {
        var pkg = SamplePackageDetail();
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = pkg,
            SetVisibilityResult = new SetVisibilityResponse
            {
                Package = $"{TestScope}/{TestName}",
                Visibility = Visibilities.Private,
            },
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        // GET first.
        var getResponse = await client.GetAsync(ManageUrl);
        var html = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);
        var csrfCookie = ExtractAntiForgeryCooke(getResponse);

        // POST — value is enum member name.
        var formContent = new FormUrlEncodedContent(
        [
            new("NewVisibility", "Private"),
            new("__RequestVerificationToken", token),
        ]);

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/manage/@{TestScope}/{TestName}?handler=SetVisibility")
        {
            Content = formContent,
        };
        request.Headers.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}; {csrfCookie}");

        var response = await client.SendAsync(request);
        var responseHtml = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("visibility updated", responseHtml, StringComparison.OrdinalIgnoreCase);
    }

    // ── POST: set visibility — registry 401 → redirect expired ───────────────

    [Fact]
    public async Task Manage_PostSetVisibility_Registry401_RedirectsToLoginExpired()
    {
        var pkg = SamplePackageDetail();
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = pkg,
            SetVisibilityException = new RegistryClientException(HttpStatusCode.Unauthorized),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        // GET first.
        var getResponse = await client.GetAsync(ManageUrl);
        var html = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);
        var csrfCookie = ExtractAntiForgeryCooke(getResponse);

        // POST — the SetVisibility call returns 401.
        var formContent = new FormUrlEncodedContent(
        [
            new("NewVisibility", "Private"),
            new("__RequestVerificationToken", token),
        ]);

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/manage/@{TestScope}/{TestName}?handler=SetVisibility")
        {
            Content = formContent,
        };
        request.Headers.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}; {csrfCookie}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains("/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expired=1", location, StringComparison.OrdinalIgnoreCase);
    }

    // ── POST: without anti-forgery token → 400 ───────────────────────────────

    [Fact]
    public async Task Manage_PostDeprecatePackage_WithoutAntiForgeryToken_Returns400()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = SamplePackageDetail(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var formContent = new FormUrlEncodedContent(
        [
            new("DeprecationMessage", "Some message."),
            // Deliberately no __RequestVerificationToken.
        ]);

        var response = await client.PostAsync(
            $"/manage/@{TestScope}/{TestName}?handler=DeprecatePackage",
            formContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Manage_PostSetVisibility_WithoutAntiForgeryToken_Returns400()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = SamplePackageDetail(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var formContent = new FormUrlEncodedContent(
        [
            new("NewVisibility", "Private"),
            // Deliberately no __RequestVerificationToken.
        ]);

        var response = await client.PostAsync(
            $"/manage/@{TestScope}/{TestName}?handler=SetVisibility",
            formContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Differential visibility test ──────────────────────────────────────────

    /// <summary>
    /// Visibility-aware read: a private package owned by the authed user is accessible
    /// via the authenticated <c>GET /manage/@scope/name</c>; the same package returns 404
    /// to the anonymous <c>GET /packages/@scope/name</c> (anonymous client returns <c>null</c>).
    /// </summary>
    [Fact]
    public async Task Manage_AuthedVsAnonymous_PrivatePackageVisibility()
    {
        var privatePkg = SamplePackageDetail();

        // Authenticated client: returns the private package.
        var authStub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = privatePkg,
        };

        // Anonymous client: returns null (= registry 404 for hidden packages).
        var anonStub = new StubRegistryClient
        {
            GetPackageResult = null,
        };

        using var factory = CreateFactory(authStub, anonStub);

        // ── Authed GET /manage: should render the package (200) ───────────────
        using var authClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        authClient.DefaultRequestHeaders.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}");

        var manageResponse = await authClient.GetAsync(ManageUrl);
        Assert.Equal(HttpStatusCode.OK, manageResponse.StatusCode);
        var manageHtml = await manageResponse.Content.ReadAsStringAsync();
        Assert.Contains($"{TestScope}/{TestName}", manageHtml, StringComparison.OrdinalIgnoreCase);

        // ── Anonymous GET /packages/@scope/name: should return 404 ────────────
        var anonClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var packageResponse = await anonClient.GetAsync($"/packages/@{TestScope}/{TestName}");
        Assert.Equal(HttpStatusCode.NotFound, packageResponse.StatusCode);
    }

    // ── POST: undeprecate package ─────────────────────────────────────────────

    [Fact]
    public async Task Manage_PostUndeprecatePackage_Success_Returns200WithSuccessBanner()
    {
        var deprecatedPkg = SamplePackageDetail(deprecated: true, deprecationMessage: "Old reason.");
        var undeprecatedPkg = SamplePackageDetail(deprecated: false);

        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = deprecatedPkg,
            UndeprecatePackageResult = new DeprecationResponse
            {
                Package = $"{TestScope}/{TestName}",
                Deprecated = false,
            },
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var getResponse = await client.GetAsync(ManageUrl);
        var html = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);
        var csrfCookie = ExtractAntiForgeryCooke(getResponse);

        stub.GetPackageResult = undeprecatedPkg;

        var formContent = new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", token),
        ]);

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/manage/@{TestScope}/{TestName}?handler=UndeprecatePackage")
        {
            Content = formContent,
        };
        request.Headers.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}; {csrfCookie}");

        var response = await client.SendAsync(request);
        var responseHtml = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("deprecation removed", responseHtml, StringComparison.OrdinalIgnoreCase);
    }

    // ── POST: deprecate version ───────────────────────────────────────────────

    [Fact]
    public async Task Manage_PostDeprecateVersion_Success_Returns200WithSuccessBanner()
    {
        var pkg = SamplePackageDetail();
        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = pkg,
            DeprecateVersionResult = new DeprecationResponse
            {
                Package = $"{TestScope}/{TestName}",
                Version = "1.0.0",
                Deprecated = true,
            },
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var getResponse = await client.GetAsync(ManageUrl);
        var html = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);
        var csrfCookie = ExtractAntiForgeryCooke(getResponse);

        var formContent = new FormUrlEncodedContent(
        [
            new("TargetVersion", "1.0.0"),
            new("VersionDeprecationMessage", "Security vulnerability in 1.0.0."),
            new("__RequestVerificationToken", token),
        ]);

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/manage/@{TestScope}/{TestName}?handler=DeprecateVersion")
        {
            Content = formContent,
        };
        request.Headers.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}; {csrfCookie}");

        var response = await client.SendAsync(request);
        var responseHtml = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("deprecated", responseHtml, StringComparison.OrdinalIgnoreCase);
    }

    // ── POST: undeprecate version ─────────────────────────────────────────────

    [Fact]
    public async Task Manage_PostUndeprecateVersion_Success_Returns200WithSuccessBanner()
    {
        var pkg = new PackageDetailResponse
        {
            Name = $"{TestScope}/{TestName}",
            Description = "A test library.",
            Keywords = new List<string>(),
            Latest = "1.2.0",
            CreatedAt = "2026-01-01T00:00:00Z",
            UpdatedAt = "2026-06-04T12:00:00Z",
            Versions = new Dictionary<string, VersionDetailResponse>
            {
                ["1.0.0"] = new VersionDetailResponse
                {
                    Version = "1.0.0",
                    PublishedAt = "2026-01-01T10:00:00Z",
                    PublishedBy = "alice",
                    Dependencies = new Dictionary<string, object>(),
                    Integrity = "sha256-fake",
                    Deprecated = true,
                    DeprecationMessage = "Old bug.",
                },
            },
        };

        var stub = new StubAuthenticatedRegistryClient
        {
            GetPackageResult = pkg,
            UndeprecateVersionResult = new DeprecationResponse
            {
                Package = $"{TestScope}/{TestName}",
                Version = "1.0.0",
                Deprecated = false,
            },
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var getResponse = await client.GetAsync(ManageUrl);
        var html = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(html);
        var csrfCookie = ExtractAntiForgeryCooke(getResponse);

        var formContent = new FormUrlEncodedContent(
        [
            new("TargetVersion", "1.0.0"),
            new("__RequestVerificationToken", token),
        ]);

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/manage/@{TestScope}/{TestName}?handler=UndeprecateVersion")
        {
            Content = formContent,
        };
        request.Headers.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}; {csrfCookie}");

        var response = await client.SendAsync(request);
        var responseHtml = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Deprecation removed from version", responseHtml, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the anti-forgery token from the rendered HTML.
    /// Reuses the same logic as <see cref="LoginPageTests.ExtractAntiForgeryToken"/>.
    /// </summary>
    internal static string ExtractAntiForgeryToken(string html)
    {
        return LoginPageTests.ExtractAntiForgeryToken(html);
    }

    /// <summary>Extracts the anti-forgery cookie header from a response.</summary>
    private static string ExtractAntiForgeryCooke(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return string.Empty;

        var parts = new System.Collections.Generic.List<string>();
        foreach (var cookie in cookies)
        {
            if (cookie.StartsWith(".AspNetCore.Antiforgery", StringComparison.OrdinalIgnoreCase))
                parts.Add(cookie.Split(';')[0]);
        }

        return string.Join("; ", parts);
    }
}
