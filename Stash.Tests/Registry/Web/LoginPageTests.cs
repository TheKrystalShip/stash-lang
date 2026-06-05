using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Contracts;
using Stash.Registry.Web.Auth;
using Stash.Registry.Web.Pages;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Integration tests for <c>GET /login</c> and <c>POST /login</c>.
/// Uses <see cref="WebApplicationFactory{TProgram}"/> against stub HTTP message handlers
/// for the AuthLogin and AuthMint named clients.
/// </summary>
public sealed class LoginPageTests
{
    // ── Stub handler ──────────────────────────────────────────────────────────

    private sealed class FakeMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(Handler(request));
    }

    private static WebApplicationFactory<HealthModel> CreateFactory(
        FakeMessageHandler? loginHandler = null,
        FakeMessageHandler? mintHandler = null)
    {
        var actualLoginHandler = loginHandler ?? new FakeMessageHandler
        {
            Handler = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new LoginResponse
                {
                    AccessToken = "read-jwt-value",
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                }),
            },
        };

        var actualMintHandler = mintHandler ?? new FakeMessageHandler
        {
            Handler = _ => new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new TokenCreateResponse
                {
                    Token = "publish-jwt-value",
                    TokenId = "token-id-abc",
                    Scope = TokenScopes.Publish,
                    ExpiresAt = DateTime.UtcNow.AddHours(8),
                }),
            },
        };

        return new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");

            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(LoginHttpClients.AuthLogin)
                    .ConfigurePrimaryHttpMessageHandler(() => actualLoginHandler);

                services.AddHttpClient(LoginHttpClients.AuthMint)
                    .ConfigurePrimaryHttpMessageHandler(() => actualMintHandler);
            });
        });
    }

    private static HttpClient CreateManualCookieClient(WebApplicationFactory<HealthModel> factory,
        bool allowAutoRedirect = true)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            HandleCookies = false,
        });
    }

    // ── GET /login ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLogin_ReturnsOk_WithForm()
    {
        using var factory = CreateFactory();
        using var client = CreateManualCookieClient(factory);

        var response = await client.GetAsync("/login");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<form", html);
        Assert.Contains("Username", html);
        Assert.Contains("Password", html);
    }

    [Fact]
    public async Task GetLogin_RendersAntiForgeryToken()
    {
        using var factory = CreateFactory();
        using var client = CreateManualCookieClient(factory);

        var response = await client.GetAsync("/login");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("__RequestVerificationToken", html);
    }

    // ── POST /login — valid credentials ──────────────────────────────────────

    [Fact]
    public async Task PostLogin_WithValidCredentials_SetsCookieAndRedirects()
    {
        using var factory = CreateFactory();
        using var client = CreateManualCookieClient(factory, allowAutoRedirect: false);

        var (antiForgeryToken, cookies) = await GetFormTokensAsync(client);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "alice",
            ["Password"] = "correct-password",
            ["__RequestVerificationToken"] = antiForgeryToken,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/login") { Content = formContent };
        request.Headers.Add("Cookie", FormatCookies(cookies));

        var response = await client.SendAsync(request);

        // Should redirect (302) and set the session cookie.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var setCookieHeaders = response.Headers
            .Where(h => string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        // Must have a session cookie.
        Assert.Contains(setCookieHeaders, c => c.Contains(SessionCookie.CookieName));
        // Cookie must be HttpOnly.
        Assert.Contains(setCookieHeaders,
            c => c.Contains(SessionCookie.CookieName) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        // Cookie must have SameSite=Strict.
        Assert.Contains(setCookieHeaders,
            c => c.Contains(SessionCookie.CookieName) && c.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PostLogin_WithValidCredentials_RedirectsToDashboard()
    {
        using var factory = CreateFactory();
        using var client = CreateManualCookieClient(factory, allowAutoRedirect: false);

        var (antiForgeryToken, cookies) = await GetFormTokensAsync(client);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "alice",
            ["Password"] = "correct",
            ["__RequestVerificationToken"] = antiForgeryToken,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/login") { Content = formContent };
        request.Headers.Add("Cookie", FormatCookies(cookies));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // No returnUrl → defaults to /dashboard
        Assert.Equal("/dashboard", response.Headers.Location?.ToString());
    }

    /// <summary>
    /// Verifies that a backslash returnUrl (<c>/\evil.com</c>) is rejected by
    /// <c>Url.IsLocalUrl</c> in <c>LoginModel</c> and the user is redirected to
    /// <c>/dashboard</c> instead of receiving a 500 from <c>LocalRedirect</c>.
    /// </summary>
    [Fact]
    public async Task Login_WithBackslashReturnUrl_RedirectsToDashboard()
    {
        using var factory = CreateFactory();
        using var client = CreateManualCookieClient(factory, allowAutoRedirect: false);

        // Fetch GET /login to obtain the anti-forgery token and cookies.
        var getResponse = await client.GetAsync("/login");
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();
        var antiForgeryToken = ExtractAntiForgeryToken(html);
        var cookies = GetCookiesFromResponse(getResponse);

        // Submit with a backslash returnUrl that would bypass a naive StartsWith('/') check.
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "alice",
            ["Password"] = "correct",
            ["ReturnUrl"] = @"/\evil.com",
            ["__RequestVerificationToken"] = antiForgeryToken,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/login") { Content = formContent };
        request.Headers.Add("Cookie", FormatCookies(cookies));

        var response = await client.SendAsync(request);

        // Must redirect (not 500) and must go to /dashboard, not the attacker's domain.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/dashboard", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PostLogin_WithValidCredentials_SessionIsStoredServerSide_NotJwtInCookie()
    {
        using var factory = CreateFactory();
        using var client = CreateManualCookieClient(factory, allowAutoRedirect: false);

        var (antiForgeryToken, cookies) = await GetFormTokensAsync(client);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "alice",
            ["Password"] = "correct",
            ["__RequestVerificationToken"] = antiForgeryToken,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/login") { Content = formContent };
        request.Headers.Add("Cookie", FormatCookies(cookies));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var sessionCookieHeader = response.Headers
            .Where(h => string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .FirstOrDefault(c => c.Contains(SessionCookie.CookieName));

        Assert.NotNull(sessionCookieHeader);
        // The cookie value is the opaque session id — MUST NOT be the JWT.
        Assert.DoesNotContain("publish-jwt-value", sessionCookieHeader);
        Assert.DoesNotContain("read-jwt-value", sessionCookieHeader);
    }

    // ── POST /login — invalid credentials ────────────────────────────────────

    [Fact]
    public async Task PostLogin_WithInvalidCredentials_RendersErrorAndSetsNoCookie()
    {
        var loginHandler = new FakeMessageHandler
        {
            Handler = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
        };

        using var factory = CreateFactory(loginHandler: loginHandler);
        using var client = CreateManualCookieClient(factory, allowAutoRedirect: false);

        var (antiForgeryToken, cookies) = await GetFormTokensAsync(client);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "alice",
            ["Password"] = "wrong-password",
            ["__RequestVerificationToken"] = antiForgeryToken,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/login") { Content = formContent };
        request.Headers.Add("Cookie", FormatCookies(cookies));

        var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Must return 200 (re-render the page with error), not a redirect.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Must NOT set a session cookie.
        var setCookieHeaders = response.Headers
            .Where(h => string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        Assert.DoesNotContain(setCookieHeaders, c => c.Contains(SessionCookie.CookieName));

        // Must render an error message.
        Assert.Contains("Invalid username or password", html);
    }

    [Fact]
    public async Task PostLogin_WithMintFailure_RendersErrorAndSetsNoCookie()
    {
        var mintHandler = new FakeMessageHandler
        {
            Handler = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };

        using var factory = CreateFactory(mintHandler: mintHandler);
        using var client = CreateManualCookieClient(factory, allowAutoRedirect: false);

        var (antiForgeryToken, cookies) = await GetFormTokensAsync(client);

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "alice",
            ["Password"] = "correct",
            ["__RequestVerificationToken"] = antiForgeryToken,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/login") { Content = formContent };
        request.Headers.Add("Cookie", FormatCookies(cookies));

        var response = await client.SendAsync(request);

        // No session cookie must be set on mint failure.
        var setCookieHeaders = response.Headers
            .Where(h => string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        Assert.DoesNotContain(setCookieHeaders, c => c.Contains(SessionCookie.CookieName));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static string ExtractAntiForgeryToken(string html)
    {
        var start = html.IndexOf("__RequestVerificationToken", StringComparison.Ordinal);
        Assert.True(start >= 0, "Anti-forgery token input not found in HTML.");

        var valueStart = html.IndexOf("value=\"", start, StringComparison.Ordinal);
        Assert.True(valueStart >= 0, "Could not find value attribute for anti-forgery token.");
        valueStart += "value=\"".Length;
        var valueEnd = html.IndexOf('"', valueStart);
        Assert.True(valueEnd > valueStart, "Could not parse anti-forgery token value.");

        return html[valueStart..valueEnd];
    }

    internal static Dictionary<string, string> GetCookiesFromResponse(HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers
                     .Where(h => string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                     .SelectMany(h => h.Value))
        {
            var parts = header.Split(';');
            var nameValue = parts[0].Split('=', 2);
            if (nameValue.Length == 2)
                cookies[nameValue[0].Trim()] = nameValue[1].Trim();
        }
        return cookies;
    }

    private static string FormatCookies(Dictionary<string, string> cookies) =>
        string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));

    private static async Task<(string AntiForgeryToken, Dictionary<string, string> Cookies)> GetFormTokensAsync(
        HttpClient client)
    {
        var getResponse = await client.GetAsync("/login");
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();
        return (ExtractAntiForgeryToken(html), GetCookiesFromResponse(getResponse));
    }
}
