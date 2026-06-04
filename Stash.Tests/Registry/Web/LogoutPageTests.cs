using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Pages;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Integration tests for <c>POST /logout</c>.
/// </summary>
public sealed class LogoutPageTests
{
    private sealed class FakeRevokeHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }
        public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.NoContent;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new HttpResponseMessage(ResponseStatusCode));
        }
    }

    private static WebApplicationFactory<HealthModel> CreateFactory(
        InMemorySessionStore store,
        FakeRevokeHandler revokeHandler)
    {
        return new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISessionStore>(store);

                services.AddHttpClient(LogoutHttpClients.AuthRevoke)
                    .ConfigurePrimaryHttpMessageHandler(() => revokeHandler);
            });
        });
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with automatic cookie handling DISABLED.
    /// This allows explicit control over which cookies are sent in each request,
    /// avoiding conflicts between the automatic cookie container and manually-set headers.
    /// </summary>
    private static HttpClient CreateManualCookieClient(WebApplicationFactory<HealthModel> factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,  // Manage cookies manually to avoid container conflicts.
        });
    }

    /// <summary>
    /// Gets an anti-forgery token by doing a GET to /login (or /logout GET for an authenticated user).
    /// When a session cookie is provided, the GET is authenticated so the returned token will be
    /// valid for authenticated POSTs.
    /// </summary>
    private static async Task<(string antiForgeryToken, string antiForgeryCooke)> GetAntiForgeryTokenAsync(
        HttpClient client,
        string? sessionCookieToSend = null)
    {
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/login");
        if (sessionCookieToSend is not null)
            getRequest.Headers.Add("Cookie", $"{SessionCookie.CookieName}={sessionCookieToSend}");

        var response = await client.SendAsync(getRequest);
        var html = await response.Content.ReadAsStringAsync();

        var token = LoginPageTests.ExtractAntiForgeryToken(html);

        var antiForgeryCooke = response.Headers
            .Where(h => string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .Where(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Split(';')[0])
            .FirstOrDefault() ?? "";

        return (token, antiForgeryCooke);
    }

    // ── GET /logout ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLogout_ReturnsOk()
    {
        var store = new InMemorySessionStore();
        var revokeHandler = new FakeRevokeHandler();
        using var factory = CreateFactory(store, revokeHandler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/logout");

        response.EnsureSuccessStatusCode();
    }

    // ── POST /logout ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PostLogout_WithSession_RevokesTokenAndClearsCookieAndRedirects()
    {
        var store = new InMemorySessionStore();
        var revokeHandler = new FakeRevokeHandler();
        using var factory = CreateFactory(store, revokeHandler);
        using var client = CreateManualCookieClient(factory);

        // Seed a session.
        const string sessionId = "logout-test-session";
        var session = new BffSession
        {
            Username = "alice",
            PublishTokenJwt = "publish-jwt-value",
            PublishTokenId = "publish-token-to-revoke",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        };
        await store.SetAsync(sessionId, session, session.ExpiresAt);

        // Get the anti-forgery token AS the authenticated user (session cookie included in GET).
        // This ensures the token is valid for an authenticated POST.
        var (antiForgeryToken, antiForgeryCooke) = await GetAntiForgeryTokenAsync(client, sessionId);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/logout") { Content = formContent };
        // Send both session cookie and anti-forgery cookie explicitly.
        request.Headers.Add("Cookie", $"{antiForgeryCooke}; {SessionCookie.CookieName}={sessionId}");

        var response = await client.SendAsync(request);

        // Must redirect to /.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());

        // Session must be removed from the store.
        var remainingSession = await store.GetAsync(sessionId);
        Assert.Null(remainingSession);

        // Revoke must have been called.
        Assert.True(revokeHandler.WasCalled);
    }

    [Fact]
    public async Task PostLogout_WithNoSession_StillRedirectsToRoot()
    {
        var store = new InMemorySessionStore();
        var revokeHandler = new FakeRevokeHandler();
        using var factory = CreateFactory(store, revokeHandler);
        using var client = CreateManualCookieClient(factory);

        var (antiForgeryToken, antiForgeryCooke) = await GetAntiForgeryTokenAsync(client);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/logout") { Content = formContent };
        request.Headers.Add("Cookie", antiForgeryCooke);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PostLogout_WhenRevokeFails_StillClearsSessionAndRedirects()
    {
        var store = new InMemorySessionStore();
        var revokeHandler = new FakeRevokeHandler { ResponseStatusCode = HttpStatusCode.InternalServerError };
        using var factory = CreateFactory(store, revokeHandler);
        using var client = CreateManualCookieClient(factory);

        const string sessionId = "logout-revoke-fail-session";
        var session = new BffSession
        {
            Username = "bob",
            PublishTokenJwt = "jwt-value",
            PublishTokenId = "token-id-to-fail",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        };
        await store.SetAsync(sessionId, session, session.ExpiresAt);

        var (antiForgeryToken, antiForgeryCooke) = await GetAntiForgeryTokenAsync(client, sessionId);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/logout") { Content = formContent };
        request.Headers.Add("Cookie", $"{antiForgeryCooke}; {SessionCookie.CookieName}={sessionId}");

        var response = await client.SendAsync(request);

        // Even with a failed revoke, the logout must succeed.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Session is still removed.
        var remainingSession = await store.GetAsync(sessionId);
        Assert.Null(remainingSession);
    }
}
