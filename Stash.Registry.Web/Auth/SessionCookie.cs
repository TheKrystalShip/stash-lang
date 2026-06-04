namespace Stash.Registry.Web.Auth;

/// <summary>
/// Single source of truth for the BFF session cookie name, ASP.NET Core
/// authentication scheme name, and DataProtection purpose string.
/// </summary>
/// <remarks>
/// <para>
/// The cookie carries <b>only</b> an opaque session id (a randomly generated token);
/// it is HTTP-only, SameSite=Strict, and Secure in non-Development environments.
/// </para>
/// <para>
/// The JWT token values live exclusively in <see cref="BffSession"/> inside the
/// server-side <see cref="ISessionStore"/>. They are never written to the cookie.
/// </para>
/// </remarks>
public static class SessionCookie
{
    /// <summary>
    /// The name of the HTTP-only session cookie sent to the browser.
    /// The value of this cookie is an opaque session id — never the JWT.
    /// </summary>
    public const string CookieName = "stash_web_session";

    /// <summary>
    /// The ASP.NET Core authentication scheme name used to register
    /// <see cref="SessionCookieAuthenticationHandler"/>.
    /// Shared by <c>Program.cs</c>, the handler, and the <c>[Authorize]</c>
    /// page conventions in A2.
    /// </summary>
    public const string AuthScheme = "BffCookie";

    /// <summary>
    /// DataProtection purpose string used to protect session-related data.
    /// </summary>
    public const string DataProtectionPurpose = "Stash.Registry.Web.Session";

    /// <summary>
    /// Configuration key for the BFF session lifetime (e.g. <c>"8h"</c>).
    /// Surfaced as <c>Bff:SessionLifetime</c> in <c>appsettings.json</c>.
    /// </summary>
    public const string SessionLifetimeConfigKey = "Bff:SessionLifetime";

    /// <summary>
    /// Default session / publish-token lifetime string sent as <c>expiresIn</c>
    /// to <c>POST /api/v1/auth/tokens</c>. Self-hosted operators may override
    /// via <see cref="SessionLifetimeConfigKey"/>.
    /// </summary>
    public const string DefaultSessionLifetime = "8h";

    /// <summary>
    /// Human-readable name used for the eagerly-minted publish token.
    /// Sent as <c>name</c> in the <c>POST /api/v1/auth/tokens</c> request body.
    /// </summary>
    public const string PublishTokenName = "stash-web session";
}
