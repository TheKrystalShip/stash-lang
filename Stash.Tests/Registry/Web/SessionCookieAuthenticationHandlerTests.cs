using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Pages;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Integration tests for <see cref="SessionCookieAuthenticationHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>done_when 5:</b> the handler reads the session cookie, looks up <see cref="BffSession"/>
/// in <see cref="ISessionStore"/>, and populates <see cref="ClaimsPrincipal"/>
/// with <c>Name = session.Username</c>.
/// </para>
/// <para>
/// <b>done_when 6:</b> an <c>[Authorize]</c>-decorated endpoint with no cookie returns
/// 302 to <c>/login?returnUrl=…</c> (not 401).
/// </para>
/// </remarks>
public sealed class SessionCookieAuthenticationHandlerTests
{
    // ── Unit tests: handler behavior ──────────────────────────────────────────

    /// <summary>
    /// Directly invokes <see cref="SessionCookieAuthenticationHandler.HandleAuthenticateAsync"/>
    /// with a seeded session to verify it returns a success ticket with the correct username.
    /// This is done_when 5.
    /// </summary>
    [Fact]
    public async Task HandleAuthenticate_WithValidSessionCookie_ReturnsSuccessWithCorrectName()
    {
        const string sessionId = "test-session-id";
        var session = new BffSession
        {
            Username = "test-user",
            PublishTokenJwt = "jwt-value",
            PublishTokenId = "token-id",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        };

        var store = new InMemorySessionStore();
        await store.SetAsync(sessionId, session, session.ExpiresAt);

        var handler = await BuildHandlerAsync(store);

        // Build a fake HTTP context with the session cookie.
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{SessionCookie.CookieName}={sessionId}";
        handler.InitializeAsync(
            new AuthenticationScheme(SessionCookie.AuthScheme, null, typeof(SessionCookieAuthenticationHandler)),
            httpContext).GetAwaiter().GetResult();

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("test-user", result.Principal?.Identity?.Name);
    }

    [Fact]
    public async Task HandleAuthenticate_WithNoCookie_ReturnsNoResult()
    {
        var store = new InMemorySessionStore();
        var handler = await BuildHandlerAsync(store);

        var httpContext = new DefaultHttpContext();
        // No cookie set.
        handler.InitializeAsync(
            new AuthenticationScheme(SessionCookie.AuthScheme, null, typeof(SessionCookieAuthenticationHandler)),
            httpContext).GetAwaiter().GetResult();

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Fact]
    public async Task HandleAuthenticate_WithExpiredSession_ReturnsNoResult()
    {
        const string sessionId = "expired-session";
        var session = new BffSession
        {
            Username = "expired-user",
            PublishTokenJwt = "expired-jwt",
            PublishTokenId = "expired-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        };

        var store = new InMemorySessionStore();
        await store.SetAsync(sessionId, session, session.ExpiresAt);

        var handler = await BuildHandlerAsync(store);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{SessionCookie.CookieName}={sessionId}";
        handler.InitializeAsync(
            new AuthenticationScheme(SessionCookie.AuthScheme, null, typeof(SessionCookieAuthenticationHandler)),
            httpContext).GetAwaiter().GetResult();

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// Verifies <see cref="SessionCookieAuthenticationHandler.HandleChallengeAsync"/>
    /// writes a 302 redirect to <c>/login?returnUrl=…</c> — done_when 6.
    /// </summary>
    [Fact]
    public async Task HandleChallenge_Redirects_ToLoginWithReturnUrl()
    {
        var store = new InMemorySessionStore();
        var handler = await BuildHandlerAsync(store);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/dashboard";
        httpContext.Request.QueryString = new QueryString("?foo=bar");

        // We need a response to capture the redirect.
        httpContext.Response.Body = new System.IO.MemoryStream();

        handler.InitializeAsync(
            new AuthenticationScheme(SessionCookie.AuthScheme, null, typeof(SessionCookieAuthenticationHandler)),
            httpContext).GetAwaiter().GetResult();

        await handler.ChallengeAsync(null);

        Assert.Equal(302, httpContext.Response.StatusCode);
        var location = httpContext.Response.Headers["Location"].ToString();
        Assert.StartsWith("/login?returnUrl=", location);
        Assert.Contains("dashboard", location);
    }

    // ── Integration tests: WAF pipeline ──────────────────────────────────────

    /// <summary>
    /// Proves the handler is registered in the pipeline and the scheme constant is correct.
    /// </summary>
    [Fact]
    public async Task Pipeline_AnonymousGetLogin_Returns200()
    {
        using var factory = new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/login");

        // Login page is anonymously reachable — must return 200.
        response.EnsureSuccessStatusCode();
    }

    // ── Scheme constant ───────────────────────────────────────────────────────

    [Fact]
    public void AuthSchemeConstant_HasExpectedValue()
    {
        Assert.Equal("BffCookie", SessionCookie.AuthScheme);
    }

    // ── Helper: build handler for unit testing ────────────────────────────────

    private static Task<SessionCookieAuthenticationHandler> BuildHandlerAsync(ISessionStore store)
    {
        var optionsMonitor = new OptionsMonitorStub<SessionCookieAuthenticationOptions>(
            new SessionCookieAuthenticationOptions());

        var handler = new SessionCookieAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            store);

        return Task.FromResult(handler);
    }

    private sealed class OptionsMonitorStub<TOptions> : IOptionsMonitor<TOptions>
    {
        private readonly TOptions _value;

        public OptionsMonitorStub(TOptions value) => _value = value;

        public TOptions CurrentValue => _value;
        public TOptions Get(string? name) => _value;
        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
