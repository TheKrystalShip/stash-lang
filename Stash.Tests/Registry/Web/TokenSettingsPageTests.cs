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
using Stash.Registry.Web.Areas.Maintainer.Pages.Settings;
using Stash.Registry.Web.Constants;
using Stash.Registry.Web.Pages;
using Stash.Registry.Web.Services;
using Stash.Tests.Registry.Web.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Integration tests for <c>GET /settings/tokens</c>, <c>POST /settings/tokens</c> (create),
/// and <c>POST /settings/tokens/{id}/revoke</c>.
/// </summary>
/// <remarks>
/// <para>
/// Tests use <see cref="WebApplicationFactory{TEntryPoint}"/> with a fixture session seeded
/// into the singleton <see cref="ISessionStore"/>. The
/// <see cref="StubAuthenticatedRegistryClient"/> replaces the real DI registration so no
/// real registry is needed.
/// </para>
/// </remarks>
public sealed class TokenSettingsPageTests
{
    private const string FixtureSessionId = "test-session-id-tokens";
    private const string FixtureUsername = "alice";
    private const string FixtureJwt = "fixture.tokens.jwt";
    private const string FixturePublishTokenId = "tok-session-001";
    private const string TokensUrl = "/settings/tokens";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BffSession MakeSession() => new()
    {
        Username = FixtureUsername,
        PublishTokenJwt = FixtureJwt,
        PublishTokenId = FixturePublishTokenId,
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
                services.AddSingleton<ISessionStore>(sp =>
                {
                    var store = new InMemorySessionStore();
                    store.SetAsync(FixtureSessionId, MakeSession(), DateTimeOffset.UtcNow.AddHours(8))
                         .GetAwaiter().GetResult();
                    return store;
                });

                services.AddScoped<IAuthenticatedRegistryClient>(_ => authStub);
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

    /// <summary>Builds a sample <see cref="TokenListResponse"/> with one non-session token.</summary>
    private static TokenListResponse SampleTokenList(
        string? extraTokenId = null,
        string? extraDescription = null)
    {
        var tokens = new List<TokenListItem>
        {
            // The current session token
            new()
            {
                TokenId = FixturePublishTokenId,
                Scope = TokenScopes.Publish,
                Description = "stash-web session",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                ExpiresAt = DateTime.UtcNow.AddHours(7),
            },
        };

        if (extraTokenId is not null)
        {
            tokens.Add(new TokenListItem
            {
                TokenId = extraTokenId,
                Scope = TokenScopes.Read,
                Description = extraDescription,
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                ExpiresAt = DateTime.UtcNow.AddDays(60),
            });
        }

        return new TokenListResponse { Tokens = tokens };
    }

    // ── Anonymous redirect ────────────────────────────────────────────────────

    /// <summary>
    /// An anonymous <c>GET /settings/tokens</c> returns 302 to <c>/login?returnUrl=…</c>,
    /// NOT 500 (the DI factory throw is never reached on the anonymous path).
    /// </summary>
    [Fact]
    public async Task Tokens_AnonymousGet_Returns302ToLogin()
    {
        using var factory = new WebApplicationFactory<HealthModel>().WithWebHostBuilder(builder =>
        {
            builder.UseSolutionRelativeContentRoot("Stash.Registry.Web");
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var response = await client.GetAsync(TokensUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains("/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("returnUrl", location, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ── GET: token list ────────────────────────────────────────────────────────

    /// <summary>
    /// An authenticated <c>GET /settings/tokens</c> calls <see cref="IAuthenticatedRegistryClient.ListTokensAsync"/>
    /// and returns 200 with the token list rendered.
    /// </summary>
    [Fact]
    public async Task Tokens_AuthenticatedGet_Returns200()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            ListTokensResult = SampleTokenList(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(TokensUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Tokens_AuthenticatedGet_RendersTokenIds()
    {
        const string ExtraTokenId = "tok-extra-001";
        var stub = new StubAuthenticatedRegistryClient
        {
            ListTokensResult = SampleTokenList(extraTokenId: ExtraTokenId, extraDescription: "CI token"),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(TokensUrl);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(ExtraTokenId, html, StringComparison.Ordinal);
        Assert.Contains(FixturePublishTokenId, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tokens_AuthenticatedGet_NeverRendersTokenValue()
    {
        // The token VALUE is never returned by the registry GET /auth/tokens endpoint,
        // so the list page must never display one. This test pins that no "Bearer" or
        // JWT-format string leaks through.
        const string SentinelValue = "SHOULD_NOT_APPEAR_tok_secret_abc123";
        var stub = new StubAuthenticatedRegistryClient
        {
            ListTokensResult = new TokenListResponse
            {
                Tokens =
                [
                    new TokenListItem
                    {
                        TokenId = "tok-no-value",
                        Scope = TokenScopes.Read,
                        Description = SentinelValue,  // description visible; the secret value should not be
                        CreatedAt = DateTime.UtcNow,
                        ExpiresAt = DateTime.UtcNow.AddDays(1),
                    },
                ],
            },
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(TokensUrl);
        var html = await response.Content.ReadAsStringAsync();

        // Regression anchor: the description IS visible…
        Assert.Contains(SentinelValue, html, StringComparison.Ordinal);
        // …but the fixture JWT (the session's publish token) is NOT rendered.
        Assert.DoesNotContain(FixtureJwt, html, StringComparison.Ordinal);
    }

    // ── Current-session badge + no-revoke constraint ───────────────────────────

    /// <summary>
    /// The row for the current session's publish token renders the "current session" badge
    /// and does NOT include a revoke button, pinning the unrevocable-session rule.
    /// </summary>
    [Fact]
    public async Task Tokens_CurrentSessionToken_HasBadgeAndNoRevokeButton()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            ListTokensResult = SampleTokenList(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(TokensUrl);
        var html = await response.Content.ReadAsStringAsync();

        // The "current session" badge must appear.
        Assert.Contains(ViewLabels.CurrentSessionBadgeLabel, html, StringComparison.OrdinalIgnoreCase);

        // The revoke button must NOT be present for the session token.
        // We test by asserting the revoke form for the session token's id doesn't appear.
        // Since the session token id is FixturePublishTokenId, its revoke route would be
        // /settings/tokens/{FixturePublishTokenId}/revoke.
        Assert.DoesNotContain(
            $"{FixturePublishTokenId}/revoke",
            html,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A non-session token row includes a revoke button; the revoke form's action points to
    /// the expected route (sanity-check that the revoke route is rendered for non-session tokens).
    /// </summary>
    [Fact]
    public async Task Tokens_NonSessionToken_HasRevokeButton()
    {
        const string ExtraTokenId = "tok-non-session-revocable";
        var stub = new StubAuthenticatedRegistryClient
        {
            ListTokensResult = SampleTokenList(extraTokenId: ExtraTokenId),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var response = await client.GetAsync(TokensUrl);
        var html = await response.Content.ReadAsStringAsync();

        // The revoke route for the non-session token must appear in the page.
        Assert.Contains(ExtraTokenId, html, StringComparison.OrdinalIgnoreCase);
    }

    // ── Server-side revoke guard ───────────────────────────────────────────────

    /// <summary>
    /// A crafted POST to revoke the current session token is rejected server-side
    /// (the handler refuses, even if the revoke button was not rendered).
    /// </summary>
    [Fact]
    public async Task Tokens_RevokeCurrentSessionToken_IsRejectedServerSide()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            ListTokensResult = SampleTokenList(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        // Obtain a valid anti-forgery token.
        var getResponse = await client.GetAsync(TokensUrl);
        var antiForgeryToken = LoginPageTests.ExtractAntiForgeryToken(
            await getResponse.Content.ReadAsStringAsync());
        var afCookie = ExtractAntiForgerySetCookie(getResponse);

        // Craft a POST to revoke the session token.
        var revokeUrl = $"{TokensUrl}/{FixturePublishTokenId}/revoke";

        var request = new HttpRequestMessage(HttpMethod.Post, revokeUrl)
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", antiForgeryToken)]),
        };
        request.Headers.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}; {afCookie}");

        var response = await client.SendAsync(request);

        // The handler must NOT have called RevokeTokenAsync on the session token.
        Assert.Null(stub.LastRevokedTokenId);

        // The response must be a page (200) showing an error — not a 500.
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ── POST: create token ─────────────────────────────────────────────────────

    /// <summary>
    /// A valid POST to create a token redirects to <c>/settings/tokens</c> and the
    /// subsequent GET renders the just-created token value (single-use TempData reveal).
    /// </summary>
    [Fact]
    public async Task Tokens_CreateToken_RedirectsAndShowsValueOnce()
    {
        const string NewTokenValue = "newly-minted-token-value-xyz";
        const string NewTokenId = "tok-new-001";

        var stub = new StubAuthenticatedRegistryClient
        {
            ListTokensResult = SampleTokenList(),
            CreateTokenResult = new TokenCreateResponse
            {
                Token = NewTokenValue,
                TokenId = NewTokenId,
                Scope = TokenScopes.Read,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
            },
        };

        using var factory = CreateFactory(stub);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // Step 1: GET to obtain anti-forgery token
        var getRequest = new HttpRequestMessage(HttpMethod.Get, TokensUrl);
        getRequest.Headers.Add("Cookie", $"{SessionCookie.CookieName}={FixtureSessionId}");
        var getResponse = await client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();

        var antiForgeryToken = LoginPageTests.ExtractAntiForgeryToken(
            await getResponse.Content.ReadAsStringAsync());
        var afCookie = ExtractAntiForgerySetCookie(getResponse);
        var sessionCookie = $"{SessionCookie.CookieName}={FixtureSessionId}";

        // Step 2: POST to create the token
        var createRequest = new HttpRequestMessage(HttpMethod.Post, TokensUrl)
        {
            Content = new FormUrlEncodedContent(
            [
                new("Ceiling", TokenScopes.Read.ToString()),
                new("ExpiresIn", "30d"),
                new("Description", "My read token"),
                new("__RequestVerificationToken", antiForgeryToken),
            ]),
        };
        createRequest.Headers.Add("Cookie", $"{sessionCookie}; {afCookie}");
        var createResponse = await client.SendAsync(createRequest);

        // Step 3: Must redirect (302/303).
        Assert.True(
            createResponse.StatusCode == HttpStatusCode.Redirect ||
            createResponse.StatusCode == HttpStatusCode.SeeOther,
            $"Expected redirect after token create, got {createResponse.StatusCode}.");

        // Capture TempData cookie from the redirect response.
        var tempDataCookies = string.Join("; ", GetSetCookieValues(createResponse));

        // Step 4: Follow the redirect manually (GET /settings/tokens) with TempData cookie.
        var followRequest = new HttpRequestMessage(HttpMethod.Get, TokensUrl);
        followRequest.Headers.Add("Cookie", $"{sessionCookie}; {afCookie}; {tempDataCookies}");
        var followResponse = await client.SendAsync(followRequest);
        var html = await followResponse.Content.ReadAsStringAsync();

        // The newly-minted token value must be visible exactly once.
        Assert.Contains(NewTokenValue, html, StringComparison.Ordinal);

        // Step 5: Second GET must NOT show the value (TempData is consumed once).
        // After the first follow-up GET, the server issues updated Set-Cookie headers that
        // clear the TempData entry. Use those cookies for the refresh to simulate a real browser.
        var updatedCookies = string.Join("; ", GetSetCookieValues(followResponse));

        var refreshRequest = new HttpRequestMessage(HttpMethod.Get, TokensUrl);
        // Use the updated cookies (which have TempData cleared) rather than the original POST cookies.
        if (!string.IsNullOrEmpty(updatedCookies))
            refreshRequest.Headers.Add("Cookie", $"{sessionCookie}; {updatedCookies}");
        else
            refreshRequest.Headers.Add("Cookie", sessionCookie);
        var refreshResponse = await client.SendAsync(refreshRequest);
        var html2 = await refreshResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain(NewTokenValue, html2, StringComparison.Ordinal);
    }

    /// <summary>
    /// POST without anti-forgery token returns 400 (CSRF guard is active on <c>POST /settings/tokens</c>).
    /// </summary>
    [Fact]
    public async Task Tokens_PostCreate_WithoutAntiForgeryToken_Returns400()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            ListTokensResult = SampleTokenList(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var formContent = new FormUrlEncodedContent(
        [
            new("Ceiling", "Read"),
            new("ExpiresIn", "30d"),
            // Deliberately no __RequestVerificationToken.
        ]);

        var response = await client.PostAsync(TokensUrl, formContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// POST /settings/tokens/{id}/revoke without anti-forgery token returns 400.
    /// </summary>
    [Fact]
    public async Task Tokens_PostRevoke_WithoutAntiForgeryToken_Returns400()
    {
        var stub = new StubAuthenticatedRegistryClient
        {
            ListTokensResult = SampleTokenList(),
        };

        using var factory = CreateFactory(stub);
        using var client = CreateAuthenticatedHttpClient(factory);

        var revokeUrl = $"{TokensUrl}/tok-some-id/revoke";
        var formContent = new FormUrlEncodedContent(
        [
            // Deliberately no __RequestVerificationToken.
        ]);

        var response = await client.PostAsync(revokeUrl, formContent);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── POST: revoke ───────────────────────────────────────────────────────────

    /// <summary>
    /// A valid revoke POST calls <see cref="IAuthenticatedRegistryClient.RevokeTokenAsync"/>
    /// with the correct token id and re-renders the page with a success banner.
    /// </summary>
    [Fact]
    public async Task Tokens_RevokeNonSessionToken_CallsRevokeAndShowsSuccess()
    {
        const string TokenToRevoke = "tok-revocable-001";

        var stub = new StubAuthenticatedRegistryClient
        {
            ListTokensResult = SampleTokenList(extraTokenId: TokenToRevoke),
        };

        using var factory = CreateFactory(stub);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        // GET to obtain anti-forgery token.
        var getRequest = new HttpRequestMessage(HttpMethod.Get, TokensUrl);
        getRequest.Headers.Add("Cookie", $"{SessionCookie.CookieName}={FixtureSessionId}");
        var getResponse = await client.SendAsync(getRequest);

        var antiForgeryToken = LoginPageTests.ExtractAntiForgeryToken(
            await getResponse.Content.ReadAsStringAsync());
        var afCookie = ExtractAntiForgerySetCookie(getResponse);

        // POST to revoke the non-session token.
        var revokeUrl = $"{TokensUrl}/{TokenToRevoke}/revoke";
        var request = new HttpRequestMessage(HttpMethod.Post, revokeUrl)
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", antiForgeryToken)]),
        };
        request.Headers.Add("Cookie",
            $"{SessionCookie.CookieName}={FixtureSessionId}; {afCookie}");

        var response = await client.SendAsync(request);

        // RevokeTokenAsync must have been called with the correct id.
        Assert.Equal(TokenToRevoke, stub.LastRevokedTokenId);

        // Response must be 200 (re-rendered page with success banner), not a redirect or error.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the first <c>.AspNetCore.Antiforgery.*</c> cookie from a response's
    /// <c>Set-Cookie</c> headers and returns it as a single "<c>name=value</c>" string
    /// suitable for use in subsequent request <c>Cookie</c> headers.
    /// </summary>
    private static string ExtractAntiForgerySetCookie(HttpResponseMessage response)
    {
        foreach (var header in response.Headers)
        {
            if (!string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var cookieValue in header.Value)
            {
                if (cookieValue.StartsWith(".AspNetCore.Antiforgery", StringComparison.OrdinalIgnoreCase))
                    return cookieValue.Split(';')[0]; // "name=value" only
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns ALL <c>Set-Cookie</c> values from a response as "<c>name=value</c>" strings
    /// (without attributes). Used to thread TempData cookie back into follow-up requests.
    /// </summary>
    private static IEnumerable<string> GetSetCookieValues(HttpResponseMessage response)
    {
        foreach (var header in response.Headers)
        {
            if (!string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var cookieValue in header.Value)
            {
                var nameValue = cookieValue.Split(';')[0];
                if (!string.IsNullOrEmpty(nameValue))
                    yield return nameValue;
            }
        }
    }
}
