namespace Stash.Registry.Configuration;

/// <summary>
/// Authentication configuration for the Stash package registry.
/// </summary>
public sealed class AuthConfig
{
    /// <summary>The authentication provider type (<c>"local"</c>, <c>"ldap"</c>, or <c>"oidc"</c>).</summary>
    public string Type { get; set; } = "local";

    /// <summary>Whether self-service user registration is enabled.</summary>
    public bool RegistrationEnabled { get; set; } = true;

    /// <summary>
    /// Lifetime of manually created API tokens issued via <c>POST /auth/tokens</c>.
    /// Format: number + unit suffix (<c>h</c> = hours, <c>d</c> = days). Default: <c>"90d"</c>.
    /// </summary>
    public string ApiTokenExpiry { get; set; } = "90d";

    /// <summary>
    /// Lifetime of short-lived access tokens issued during login and refresh.
    /// Format: number + unit suffix (<c>h</c> = hours, <c>d</c> = days). Default: <c>"1h"</c>.
    /// </summary>
    public string AccessTokenExpiry { get; set; } = "1h";

    /// <summary>
    /// Lifetime of long-lived refresh tokens issued during login.
    /// Format: number + unit suffix (<c>h</c> = hours, <c>d</c> = days). Default: <c>"90d"</c>.
    /// </summary>
    public string RefreshTokenExpiry { get; set; } = "90d";

    /// <summary>LDAP server hostname (when <see cref="Type"/> is <c>"ldap"</c>).</summary>
    public string? LdapServer { get; set; }

    /// <summary>LDAP server port. Default: <c>389</c>.</summary>
    public int LdapPort { get; set; } = 389;

    /// <summary>LDAP base distinguished name for user lookups.</summary>
    public string? LdapBaseDn { get; set; }

    /// <summary>LDAP search filter for user authentication.</summary>
    public string? LdapUserFilter { get; set; }

    /// <summary>OIDC authority URL (when <see cref="Type"/> is <c>"oidc"</c>).</summary>
    public string? OidcAuthority { get; set; }

    /// <summary>OIDC client identifier.</summary>
    public string? OidcClientId { get; set; }

    /// <summary>OIDC client secret.</summary>
    public string? OidcClientSecret { get; set; }
}
