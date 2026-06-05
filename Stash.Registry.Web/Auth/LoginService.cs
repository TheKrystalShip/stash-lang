using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stash.Registry.Contracts;

namespace Stash.Registry.Web.Auth;

/// <summary>
/// Named HTTP clients used by <see cref="LoginService"/>.
/// These are the only places in the codebase that use anonymous <see cref="HttpClient"/>
/// calls outside of the Phase-2 <c>HttpRegistryClient</c>. The chokepoint scan in A2
/// (<c>AuthClientChokepointMetaTests</c>) lists them as an explicit allowlist.
/// </summary>
public static class LoginHttpClients
{
    /// <summary>
    /// Named client used by <see cref="LoginService"/> to call
    /// <c>POST /api/v1/auth/login</c>. Sends NO <c>Authorization</c> header.
    /// </summary>
    public const string AuthLogin = "AuthLogin";

    /// <summary>
    /// Named client used by <see cref="LoginService"/> to eagerly mint the publish token
    /// via <c>POST /api/v1/auth/tokens</c>. The read JWT from login is attached per-call
    /// inside <see cref="LoginService.LoginAsync"/> — this is the ONLY place it is used,
    /// after which it is discarded and never persisted.
    /// </summary>
    public const string AuthMint = "AuthMint";
}

/// <summary>
/// Owns the complete login workflow for the BFF.
/// </summary>
/// <remarks>
/// <para>
/// <b>Login pipeline:</b>
/// <list type="number">
///   <item>Call <c>POST /api/v1/auth/login</c> with credentials (via the <c>"AuthLogin"</c>
///   named client). On 401 → return a failed <see cref="LoginResult"/>.</item>
///   <item>On 200 → eager-mint a <c>publish</c>-ceiling token via
///   <c>POST /api/v1/auth/tokens</c> (via the <c>"AuthMint"</c> named client, threading
///   the read JWT as bearer). The read JWT is discarded after this call.</item>
///   <item>Build <see cref="BffSession"/> from the <see cref="TokenCreateResponse"/>.
///   Persist to <see cref="ISessionStore"/> with <c>ExpiresAt = TokenCreateResponse.ExpiresAt</c>.
///   Set the HTTP-only session cookie. Redirect to <paramref name="returnUrl"/> or
///   <c>/dashboard</c>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class LoginService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISessionStore _sessionStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LoginService> _logger;

    public LoginService(
        IHttpClientFactory httpClientFactory,
        ISessionStore sessionStore,
        IConfiguration configuration,
        ILogger<LoginService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sessionStore = sessionStore;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to log in the user with the supplied credentials.
    /// On success: creates the server-side session, sets the cookie, and returns
    /// a <see cref="LoginResult"/> with <c>Success = true</c> and the redirect URL.
    /// On failure: returns a <see cref="LoginResult"/> with <c>Success = false</c>
    /// and an error message. No cookie is set.
    /// </summary>
    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        string? returnUrl,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        // ── Step 1: authenticate against the registry ─────────────────────────
        var loginClient = _httpClientFactory.CreateClient(LoginHttpClients.AuthLogin);

        HttpResponseMessage loginResponse;
        try
        {
            loginResponse = await loginClient.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LoginRequest { Username = username, Password = password },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network error calling POST /api/v1/auth/login");
            return LoginResult.Failure("The registry is temporarily unreachable. Please try again.");
        }

        if (loginResponse.StatusCode == HttpStatusCode.Unauthorized)
            return LoginResult.Failure("Invalid username or password.");

        if (!loginResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("POST /api/v1/auth/login returned {StatusCode}", loginResponse.StatusCode);
            return LoginResult.Failure("Login failed. Please try again.");
        }

        LoginResponse? loginBody;
        try
        {
            loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize LoginResponse");
            return LoginResult.Failure("Login failed. Please try again.");
        }

        if (loginBody is null)
            return LoginResult.Failure("Login failed. Please try again.");

        var readJwt = loginBody.AccessToken;

        // ── Step 2: eager-mint the publish-ceiling session token ──────────────
        var sessionLifetime = _configuration[SessionCookie.SessionLifetimeConfigKey]
            ?? SessionCookie.DefaultSessionLifetime;

        var mintClient = _httpClientFactory.CreateClient(LoginHttpClients.AuthMint);
        // Thread the read JWT as bearer — the ONLY place it is used.
        mintClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", readJwt);

        HttpResponseMessage mintResponse;
        try
        {
            mintResponse = await mintClient.PostAsJsonAsync(
                "/api/v1/auth/tokens",
                new TokenCreateRequest
                {
                    // Use the BoundedDomain wire value — no inlined literal.
                    Ceiling = TokenScopes.Publish.ToWire(),
                    Name = SessionCookie.PublishTokenName,
                    ExpiresIn = sessionLifetime,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network error calling POST /api/v1/auth/tokens");
            // Best-effort: read JWT has no revoke path; it will expire naturally.
            return LoginResult.Failure("Could not establish session. Please try again.");
        }

        if (!mintResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("POST /api/v1/auth/tokens returned {StatusCode}", mintResponse.StatusCode);
            return LoginResult.Failure("Could not establish session. Please try again.");
        }

        TokenCreateResponse? mintBody;
        try
        {
            mintBody = await mintResponse.Content.ReadFromJsonAsync<TokenCreateResponse>(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize TokenCreateResponse");
            return LoginResult.Failure("Could not establish session. Please try again.");
        }

        if (mintBody is null)
            return LoginResult.Failure("Could not establish session. Please try again.");

        // ── Step 3: persist session + set cookie ──────────────────────────────
        // Generate a cryptographically random opaque session id — NEVER the JWT.
        var sessionId = GenerateSessionId();

        var session = new BffSession
        {
            Username = username,
            PublishTokenJwt = mintBody.Token,   // server-side only, never sent to browser
            PublishTokenId = mintBody.TokenId,
            ExpiresAt = new DateTimeOffset(mintBody.ExpiresAt, TimeSpan.Zero),
        };

        var expiresAt = session.ExpiresAt;
        await _sessionStore.SetAsync(sessionId, session, expiresAt, cancellationToken)
            .ConfigureAwait(false);

        // Cookie: HTTP-only, SameSite=Strict, Secure in production.
        var isSecure = !IsEnvironmentDevelopment(httpContext);
        httpContext.Response.Cookies.Append(
            SessionCookie.CookieName,
            sessionId,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = isSecure,
                Expires = expiresAt,
            });

        // The caller is responsible for validating that returnUrl is a same-origin relative
        // path (open-redirect prevention). Pass null here if the URL is not local; the
        // service falls back to /dashboard.
        var redirectTo = returnUrl ?? "/dashboard";
        return LoginResult.Ok(redirectTo);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GenerateSessionId()
    {
        // 256 bits of cryptographic randomness encoded as base64url.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static bool IsEnvironmentDevelopment(HttpContext httpContext)
    {
        var env = httpContext.RequestServices
            .GetService(typeof(Microsoft.AspNetCore.Hosting.IWebHostEnvironment))
            as Microsoft.AspNetCore.Hosting.IWebHostEnvironment;
        return env?.IsDevelopment() ?? false;
    }
}

/// <summary>
/// Result of a <see cref="LoginService.LoginAsync"/> call.
/// </summary>
public sealed record LoginResult
{
    /// <summary><c>true</c> if login succeeded and a session was established.</summary>
    public bool Success { get; init; }

    /// <summary>User-facing error message when <see cref="Success"/> is <c>false</c>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The redirect URL when <see cref="Success"/> is <c>true</c>.
    /// Honors the <c>returnUrl</c> parameter when it is a same-origin relative path;
    /// falls back to <c>/dashboard</c>.
    /// </summary>
    public string? RedirectUrl { get; init; }

    internal static LoginResult Ok(string redirectUrl) =>
        new() { Success = true, RedirectUrl = redirectUrl };

    internal static LoginResult Failure(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}
