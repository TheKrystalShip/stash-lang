namespace Stash.Registry.Configuration;

public sealed class AuthConfig
{
    public string Type { get; set; } = "local";
    public bool RegistrationEnabled { get; set; } = true;
    public string TokenExpiry { get; set; } = "90d";
    public string? LdapServer { get; set; }
    public int LdapPort { get; set; } = 389;
    public string? LdapBaseDn { get; set; }
    public string? LdapUserFilter { get; set; }
    public string? OidcAuthority { get; set; }
    public string? OidcClientId { get; set; }
    public string? OidcClientSecret { get; set; }
}
