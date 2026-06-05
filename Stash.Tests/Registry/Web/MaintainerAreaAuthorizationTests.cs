using System;
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
/// Integration tests asserting the Maintainer area authorization convention:
/// anonymous requests get 302 (not 500), and authenticated requests get 200.
/// </summary>
public sealed class MaintainerAreaAuthorizationTests
{
    private const string FixtureSessionId = "test-session-id-authz";
    private const string FixtureUsername = "bob";
    private const string FixtureJwt = "fixture.jwt.authz";

    private static BffSession MakeSession() => new()
    {
        Username = FixtureUsername,
        PublishTokenJwt = FixtureJwt,
        PublishTokenId = "tok-authz-001",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
    };

    private static WebApplicationFactory<HealthModel> CreateFactory(
        bool seedSession = false)
    {
        return new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");

            builder.ConfigureTestServices(services =>
            {
                if (seedSession)
                {
                    services.AddSingleton<ISessionStore>(sp =>
                    {
                        var store = new InMemorySessionStore();
                        store.SetAsync(FixtureSessionId, MakeSession(), DateTimeOffset.UtcNow.AddHours(8))
                             .GetAwaiter().GetResult();
                        return store;
                    });
                }

                // Replace the authenticated registry client with a stub so authenticated
                // requests reach the page successfully (no real registry needed).
                services.AddScoped<IAuthenticatedRegistryClient>(_ =>
                    new StubAuthenticatedRegistryClient());
            });
        });
    }

    // ── Anonymous → 302, not 500 ──────────────────────────────────────────────

    /// <summary>
    /// <b>Load-bearing.</b> <c>GET /dashboard</c> with NO session cookie returns
    /// 302 to <c>/login?returnUrl=…</c> and NOT 500.
    /// The 302 proves the auth handler fires BEFORE the page model is constructed,
    /// so the <c>IAuthenticatedRegistryClient</c> DI factory throw is never reached.
    /// </summary>
    [Fact]
    public async Task GetDashboard_AnonymousRequest_Returns302NotLoginPage()
    {
        using var factory = CreateFactory(seedSession: false);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var response = await client.GetAsync("/dashboard");

        // Must be a redirect (302), NOT a server error (500).
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        // Must redirect toward /login.
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.StartsWith("/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("returnUrl", location, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>GET /dashboard</c> with an invalid/unknown session cookie also returns 302.
    /// (The session lookup returns null → auth result is NoResult → challenge → 302.)
    /// </summary>
    [Fact]
    public async Task GetDashboard_InvalidSessionCookie_Returns302NotLoginPage()
    {
        using var factory = CreateFactory(seedSession: false);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie",
            $"{SessionCookie.CookieName}=nonexistent-session-id");

        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ── Authenticated → 200 ───────────────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_AuthenticatedRequest_Returns200()
    {
        using var factory = CreateFactory(seedSession: true);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}");

        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Return URL is included ────────────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_AnonymousRequest_RedirectIncludesReturnUrl()
    {
        using var factory = CreateFactory(seedSession: false);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        // The returnUrl should encode /dashboard
        Assert.Contains("dashboard", location, StringComparison.OrdinalIgnoreCase);
    }
}
