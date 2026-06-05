using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
/// Integration tests for <c>GET /dashboard</c>.
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
public sealed class DashboardPageTests
{
    private const string FixtureSessionId = "test-session-id-dashboard";
    private const string FixtureUsername = "alice";
    private const string FixtureJwt = "fixture.jwt.value";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BffSession MakeSession() => new()
    {
        Username = FixtureUsername,
        PublishTokenJwt = FixtureJwt,
        PublishTokenId = "tok-test-001",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
    };

    private static WebApplicationFactory<HealthModel> CreateFactory(
        StubAuthenticatedRegistryClient authStub)
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
            });
        });
    }

    private static System.Net.Http.HttpClient CreateAuthenticatedHttpClient(
        WebApplicationFactory<HealthModel> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        // Send the session cookie so the BffCookie auth handler authenticates the request.
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.CookieName}={FixtureSessionId}");
        return client;
    }

    // ── Anonymous redirect ────────────────────────────────────────────────────

    /// <summary>
    /// An anonymous <c>GET /dashboard</c> returns 302 to <c>/login?returnUrl=/dashboard</c>,
    /// NOT 500 (the DI factory throw is never reached on the anonymous path).
    /// </summary>
    [Fact]
    public async Task Dashboard_AnonymousRequest_Returns302ToLogin()
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

        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains("/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("returnUrl", location, StringComparison.OrdinalIgnoreCase);

        // Must NOT be a 500 — the DI factory's NoActiveSessionException should never be reached here.
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ── Authenticated: renders owned packages ─────────────────────────────────

    [Fact]
    public async Task Dashboard_AuthenticatedRequest_Returns200()
    {
        var stub = StubAuthenticatedRegistryClient.WithPackages([
            SamplePackage("org/my-public-pkg"),
        ]);

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_AuthenticatedRequest_PassesOwnerFilter()
    {
        var stub = new StubAuthenticatedRegistryClient();

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        await client.GetAsync("/dashboard");

        // The page model must pass owner = session.Username to SearchOwnedAsync.
        Assert.NotNull(stub.LastSearchQuery);
        Assert.Equal(FixtureUsername, stub.LastSearchQuery!.owner);
    }

    [Fact]
    public async Task Dashboard_AuthenticatedRequest_PassesUpdatedSort()
    {
        var stub = new StubAuthenticatedRegistryClient();

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        await client.GetAsync("/dashboard");

        Assert.NotNull(stub.LastSearchQuery);
        Assert.Equal(PackageSortOrder.Updated, stub.LastSearchQuery!.sort);
    }

    /// <summary>
    /// Differential visibility test: the authed client returns both public and private
    /// packages; the anonymous <c>/search?owner=alice</c> returns only public ones.
    /// This pins the structural fact that the dashboard routes through the authed client.
    /// </summary>
    [Fact]
    public async Task Dashboard_AuthedVsAnonymous_AuthedShowsBothPackages()
    {
        var publicPkg = SamplePackage("org/public-pkg", "Public package");
        var privatePkg = SamplePackage("org/private-pkg", "Private package");

        // The authed client returns BOTH packages.
        var authStub = StubAuthenticatedRegistryClient.WithPackages([publicPkg, privatePkg]);

        // The anonymous client returns ONLY the public one.
        var anonStub = StubRegistryClient.WithPackages([publicPkg]);

        using var factory = new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");

            builder.ConfigureTestServices(services =>
            {
                // Seed the session store.
                services.AddSingleton<ISessionStore>(sp =>
                {
                    var store = new InMemorySessionStore();
                    store.SetAsync(FixtureSessionId, MakeSession(), DateTimeOffset.UtcNow.AddHours(8))
                         .GetAwaiter().GetResult();
                    return store;
                });

                // Authed client stub: returns both.
                services.AddScoped<IAuthenticatedRegistryClient>(_ => authStub);

                // Anonymous client stub: returns only public.
                services.AddScoped<IRegistryClient>(_ => anonStub);
            });
        });

        // ── Authed GET /dashboard: should list both packages ──────────────────
        using var authClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        authClient.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.CookieName}={FixtureSessionId}");

        var dashResponse = await authClient.GetAsync("/dashboard");
        Assert.Equal(HttpStatusCode.OK, dashResponse.StatusCode);
        var dashHtml = await dashResponse.Content.ReadAsStringAsync();
        Assert.Contains("public-pkg", dashHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private-pkg", dashHtml, StringComparison.OrdinalIgnoreCase);

        // ── Anonymous GET /search?owner=alice: should list only public ─────────
        var anonClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var searchResponse = await anonClient.GetAsync($"/search?owner={FixtureUsername}");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchHtml = await searchResponse.Content.ReadAsStringAsync();
        Assert.Contains("public-pkg", searchHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-pkg", searchHtml, StringComparison.OrdinalIgnoreCase);
    }

    // ── Registry error renders gracefully ─────────────────────────────────────

    [Fact]
    public async Task Dashboard_RegistryError_Returns200WithErrorBanner()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            SearchOwnedException = new RegistryClientException(
                HttpStatusCode.ServiceUnavailable, null, "Registry unavailable."),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Registry unavailable", html, StringComparison.OrdinalIgnoreCase);
    }

    // ── GET: registry 401 → clear session + redirect to /login?expired=1 ─────

    /// <summary>
    /// C4 — session expiry contract: a registry 401 on the dashboard (e.g. publish token
    /// revoked out-of-band) must clear the server-side session, delete the session cookie,
    /// and redirect to <c>/login?expired=1</c>.
    /// </summary>
    [Fact]
    public async Task Dashboard_AuthenticatedGet_Registry401_ClearsSessionAndRedirectsToLoginExpired()
    {
        // Arrange: stub returns 401 from SearchOwnedAsync.
        var stub = new StubAuthenticatedRegistryClient
        {
            SearchOwnedException = new RegistryClientException(HttpStatusCode.Unauthorized),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        // Act.
        var response = await client.GetAsync("/dashboard");

        // Assert 1: 302 redirect to /login?expired=1.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains("/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expired=1", location, StringComparison.OrdinalIgnoreCase);

        // Assert 2: session cookie is deleted — the Set-Cookie header for the session
        // cookie must be present with an expiry in the past (Max-Age=0 or Expires in the past).
        Assert.True(
            response.Headers.TryGetValues("Set-Cookie", out var cookies),
            "Expected a Set-Cookie header to delete the session cookie.");
        var cookieList = cookies!.ToList();
        var sessionCookieHeader = cookieList.FirstOrDefault(c =>
            c.StartsWith(SessionCookie.CookieName, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sessionCookieHeader);
        // ASP.NET Core cookie deletion sets the value to empty and expires it.
        Assert.True(
            sessionCookieHeader!.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
            sessionCookieHeader!.Contains("max-age=0", StringComparison.OrdinalIgnoreCase),
            $"Session cookie header did not indicate deletion: {sessionCookieHeader}");

        // Assert 3: server-side session is removed from ISessionStore.
        var sessionStore = factory.Services.GetRequiredService<ISessionStore>();
        var session = await sessionStore.GetAsync(FixtureSessionId);
        Assert.Null(session);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PackageSummaryResponse SamplePackage(
        string name = "org/pkg",
        string? description = null) =>
        new()
        {
            Name = name,
            Description = description ?? $"Description for {name}",
            Keywords = [],
            UpdatedAt = "2026-06-04T12:00:00Z",
        };
}
