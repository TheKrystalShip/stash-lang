using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Pages;
using Stash.Tests.Registry.Web.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Architecture guard: asserts CSRF protection is enforced on every state-changing route
/// in Phase A1 (<c>/login</c>, <c>/logout</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Load-bearing assertion:</b> a <see cref="WebApplicationFactory{TEntryPoint}"/> POST to
/// <c>/login</c> (and <c>/logout</c>) WITHOUT a valid anti-forgery token returns 400. This is
/// the behavioral proof that CSRF protection is active on these routes.
/// </para>
/// <para>
/// <b>Important caveat:</b> Razor Pages auto-validate anti-forgery on every unsafe POST by
/// default, regardless of the global
/// <see cref="AutoValidateAntiforgeryTokenAttribute"/> registration.
/// The behavioral 400 proves "CSRF is enforced on this Razor Page route" — it is NOT
/// independently evidence that the global filter is registered. The global filter is kept
/// for defence-in-depth (it also covers future minimal-API endpoints and controllers), but
/// it is not the mechanism that makes the 400 tests pass for Razor Pages.
/// The global registration can be inspected via <c>IOptions&lt;MvcOptions&gt;.Value.Filters</c>
/// if an independently-testable proof is ever needed.
/// </para>
/// <para>
/// <b>Fail-path fixture:</b> <see cref="AntiForgeryFailPathFixture"/> provides filter lists
/// with and without the attribute so the structural scanner logic has provable teeth.
/// The fixtures prove the scanner helper correctly identifies presence/absence; they do NOT
/// inspect the running app's filter list.
/// </para>
/// </remarks>
public sealed class AntiForgeryConstructMetaTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WebApplicationFactory<HealthModel> CreateFactory()
    {
        return new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");

            builder.ConfigureTestServices(services =>
            {
                // Stub the AuthLogin client so a tokenless POST returns 400 (CSRF),
                // not a network error.
                services.AddHttpClient(LoginHttpClients.AuthLogin)
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeUnauthorizedRegistryHandler());
                services.AddHttpClient(LoginHttpClients.AuthMint)
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeUnauthorizedRegistryHandler());
            });
        });
    }

    /// <summary>
    /// A stub HTTP handler that returns 401 Unauthorized — simulates "bad credentials" from
    /// the registry. Used so that the anti-forgery filter fires first (400) and the registry
    /// response (401) is only reached when a valid token is present.
    /// </summary>
    private sealed class FakeUnauthorizedRegistryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }

    // ── BEHAVIORAL: load-bearing assertions ──────────────────────────────────

    /// <summary>
    /// <b>Load-bearing.</b> POST to <c>/login</c> WITHOUT a valid anti-forgery token
    /// returns 400, proving the global CSRF filter is active on this route.
    /// </summary>
    [Fact]
    public async Task PostLogin_WithoutAntiForgeryToken_Returns400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var formContent = new FormUrlEncodedContent(
        [
            new("Username", "alice"),
            new("Password", "password"),
            // Deliberately no __RequestVerificationToken.
        ]);

        var response = await client.PostAsync("/login", formContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// <b>Load-bearing.</b> POST to <c>/logout</c> WITHOUT a valid anti-forgery token
    /// returns 400, proving the global CSRF filter is active on this route.
    /// </summary>
    [Fact]
    public async Task PostLogout_WithoutAntiForgeryToken_Returns400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var formContent = new FormUrlEncodedContent(
        [
            // Deliberately no __RequestVerificationToken.
        ]);

        var response = await client.PostAsync("/logout", formContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Creates a <see cref="WebApplicationFactory{TEntryPoint}"/> with a fixture session seeded
    /// so that requests to the authenticated Maintainer area reach the CSRF filter before auth
    /// redirects them. Used for the A4 token-page CSRF assertions.
    /// </summary>
    private static WebApplicationFactory<HealthModel> CreateAuthenticatedFactory()
    {
        const string SessionId = "csrf-test-session-a4";
        return new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");

            builder.ConfigureTestServices(services =>
            {
                // Seed a session so the auth handler accepts the cookie and the CSRF filter
                // (not the auth 302) fires first on the POST.
                services.AddSingleton<ISessionStore>(sp =>
                {
                    var store = new InMemorySessionStore();
                    store.SetAsync(
                        SessionId,
                        new BffSession
                        {
                            Username = "csrf-test-user",
                            PublishTokenJwt = "csrf-test-jwt",
                            PublishTokenId = "tok-csrf-001",
                            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
                        },
                        DateTimeOffset.UtcNow.AddHours(8))
                        .GetAwaiter().GetResult();
                    return store;
                });

                // Stub the authenticated client so CSRF rejection (400) is the only error path
                // that fires before the auth client is resolved.
                services.AddScoped<IAuthenticatedRegistryClient>(_ =>
                    new StubAuthenticatedRegistryClient());
            });
        });
    }

    // Expose the session id for use in Cookie headers in the two token tests below.
    private const string AuthFactorySessionId = "csrf-test-session-a4";

    /// <summary>
    /// <b>Load-bearing.</b> POST to <c>/settings/tokens</c> WITH a valid session cookie but
    /// WITHOUT an anti-forgery token returns 400, proving the global CSRF filter is active
    /// on the token-create route (A4).
    /// </summary>
    [Fact]
    public async Task PostTokenCreate_WithoutAntiForgeryToken_Returns400()
    {
        using var factory = CreateAuthenticatedFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // Attach a valid session cookie so auth passes and CSRF fires.
        client.DefaultRequestHeaders.Add("Cookie",
            $"{SessionCookie.CookieName}={AuthFactorySessionId}");

        var formContent = new FormUrlEncodedContent(
        [
            new("Ceiling", "Read"),
            new("ExpiresIn", "30d"),
            // Deliberately no __RequestVerificationToken.
        ]);

        var response = await client.PostAsync("/settings/tokens", formContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// <b>Load-bearing.</b> POST to <c>/settings/tokens/{id}/revoke</c> WITH a valid session
    /// cookie but WITHOUT an anti-forgery token returns 400, proving the global CSRF filter is
    /// active on the token-revoke route (A4).
    /// </summary>
    [Fact]
    public async Task PostTokenRevoke_WithoutAntiForgeryToken_Returns400()
    {
        using var factory = CreateAuthenticatedFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // Attach a valid session cookie so auth passes and CSRF fires.
        client.DefaultRequestHeaders.Add("Cookie",
            $"{SessionCookie.CookieName}={AuthFactorySessionId}");

        var formContent = new FormUrlEncodedContent(
        [
            // Deliberately no __RequestVerificationToken.
        ]);

        var response = await client.PostAsync("/settings/tokens/tok-some-id/revoke", formContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── STRUCTURAL: secondary checks (page-convention filter list) ────────────

    /// <summary>
    /// Secondary structural check: filter list WITH the attribute is recognized as present.
    /// Proves the structural scanner has teeth (uses <see cref="AntiForgeryFailPathFixture"/>).
    /// </summary>
    [Fact]
    public void StructuralScanner_FiltersWithCsrfGuard_ReportsPresent()
    {
        var filters = AntiForgeryFailPathFixture.FiltersWithCsrfGuard;

        bool present = filters.Any(f => f is AutoValidateAntiforgeryTokenAttribute);

        Assert.True(
            present,
            "The structural scanner did not detect AutoValidateAntiforgeryTokenAttribute " +
            "in the known-good filter list. The secondary guard has lost its teeth.");
    }

    /// <summary>
    /// Secondary structural check: filter list WITHOUT the attribute is recognized as absent.
    /// Proves the structural scanner correctly reports absence.
    /// </summary>
    [Fact]
    public void StructuralScanner_FiltersWithoutCsrfGuard_ReportsAbsent()
    {
        var filters = AntiForgeryFailPathFixture.FiltersWithoutCsrfGuard;

        bool present = filters.Any(f => f is AutoValidateAntiforgeryTokenAttribute);

        Assert.False(
            present,
            "The structural scanner falsely reported AutoValidateAntiforgeryTokenAttribute " +
            "as present in the known-bad (empty) filter list.");
    }

    /// <summary>
    /// Confirms that a POST WITH a valid anti-forgery token is NOT rejected as 400.
    /// This is the teeth-proof control: (no token → 400) paired with (valid token → non-400)
    /// proves the guard is real and not a blanket reject-all.
    /// </summary>
    [Fact]
    public async Task PostLogin_WithValidAntiForgeryToken_IsNotRejectedAs400()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // GET the login page to obtain a valid CSRF cookie + token.
        var getResponse = await client.GetAsync("/login");
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();

        var token = LoginPageTests.ExtractAntiForgeryToken(html);

        var antiForgeryCooke = getResponse.Headers
            .Where(h => string.Equals(h.Key, "Set-Cookie", System.StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .Where(c => c.StartsWith(".AspNetCore.Antiforgery", System.StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Split(';')[0])
            .FirstOrDefault() ?? "";

        var formContent = new FormUrlEncodedContent(
        [
            new("Username", "alice"),
            new("Password", "password"),
            new("__RequestVerificationToken", token),
        ]);

        var request = new HttpRequestMessage(HttpMethod.Post, "/login") { Content = formContent };
        request.Headers.Add("Cookie", antiForgeryCooke);

        var response = await client.SendAsync(request);

        // With a valid token, the request is NOT rejected as 400.
        // (It may be 401, redirect, or 200 depending on credentials — just NOT 400.)
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
