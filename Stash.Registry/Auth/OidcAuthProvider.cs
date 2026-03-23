using System;
using System.Threading.Tasks;

namespace Stash.Registry.Auth;

/// <summary>
/// Stub <see cref="IAuthProvider"/> implementation for OpenID Connect (OIDC) authentication.
/// </summary>
/// <remarks>
/// <para>
/// <b>⚠ Not yet implemented.</b> All methods throw <see cref="NotSupportedException"/>.
/// To use the registry, set <c>Registry:Auth:Type</c> to <c>"local"</c> in
/// <c>appsettings.json</c> and use <see cref="LocalAuthProvider"/> instead.
/// </para>
/// <para>
/// This provider is selected when <c>Registry:Auth:Type</c> is <c>"oidc"</c> and is
/// instantiated by <see cref="Startup.ConfigureServices"/> with the authority URL and
/// client identifier sourced from <see cref="Configuration.AuthConfig"/>.
/// </para>
/// <para>
/// <b>Design intent:</b> OIDC uses redirect-based flows (Authorization Code + PKCE) rather
/// than direct username/password exchange. When implemented, the registry will act as a
/// relying party: the CLI will execute the browser-based authorization-code flow and exchange
/// the resulting code for an identity token, which the registry will verify against the
/// identity provider's JWKS endpoint. The <see cref="AuthenticateAsync"/> signature is
/// therefore not appropriate for a native OIDC integration and this interface may need to be
/// extended before this provider can be completed.
/// </para>
/// </remarks>
public sealed class OidcAuthProvider : IAuthProvider
{
    /// <summary>The base URL of the OIDC identity provider (authority), e.g. <c>https://login.example.com</c>.</summary>
    private readonly string _authority;

    /// <summary>The client identifier registered with the OIDC provider for the Stash Registry application.</summary>
    private readonly string _clientId;

    /// <summary>
    /// Initialises a new <see cref="OidcAuthProvider"/> with the given OIDC configuration.
    /// </summary>
    /// <param name="authority">
    /// The OIDC authority base URL. The discovery document is expected at
    /// <c>{authority}/.well-known/openid-configuration</c>.
    /// </param>
    /// <param name="clientId">The client ID registered with the identity provider.</param>
    public OidcAuthProvider(string authority, string clientId)
    {
        _authority = authority;
        _clientId = clientId;
    }

    /// <summary>
    /// Not implemented. OIDC uses redirect-based authorization flows, not direct
    /// username/password authentication.
    /// </summary>
    /// <param name="username">Ignored.</param>
    /// <param name="password">Ignored.</param>
    /// <returns>This method never returns a value.</returns>
    /// <exception cref="NotSupportedException">
    /// Always thrown. Configure <c>Registry:Auth:Type</c> as <c>"local"</c> until this
    /// provider is implemented, or implement the authorization-code + PKCE flow in the CLI.
    /// </exception>
    public Task<bool> AuthenticateAsync(string username, string password)
    {
        // OIDC authentication uses redirect flows and token exchange,
        // not direct username/password. The CLI must implement the
        // authorization code flow with PKCE.
        throw new NotSupportedException(
            $"OIDC authentication is not yet implemented. " +
            $"Configure Auth:Type as 'local' in appsettings.json. " +
            $"(Authority: {_authority}, ClientId: {_clientId})");
    }

    /// <summary>
    /// Not implemented. User accounts are managed by the external OIDC identity provider,
    /// not by the Stash Registry.
    /// </summary>
    /// <param name="username">Ignored.</param>
    /// <param name="password">Ignored.</param>
    /// <returns>This method never returns a value.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task CreateUserAsync(string username, string password)
    {
        throw new NotSupportedException(
            "User creation is managed by the OIDC identity provider, not the registry.");
    }

    /// <summary>
    /// Not implemented. OIDC user existence checks are not yet supported.
    /// </summary>
    /// <param name="username">Ignored.</param>
    /// <returns>This method never returns a value.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task<bool> UserExistsAsync(string username)
    {
        throw new NotSupportedException(
            "OIDC user lookup is not yet implemented.");
    }
}
