using System;
using System.Threading.Tasks;

namespace Stash.Registry.Auth;

public sealed class OidcAuthProvider : IAuthProvider
{
    private readonly string _authority;
    private readonly string _clientId;

    public OidcAuthProvider(string authority, string clientId)
    {
        _authority = authority;
        _clientId = clientId;
    }

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

    public Task CreateUserAsync(string username, string password)
    {
        throw new NotSupportedException(
            "User creation is managed by the OIDC identity provider, not the registry.");
    }

    public Task<bool> UserExistsAsync(string username)
    {
        throw new NotSupportedException(
            "OIDC user lookup is not yet implemented.");
    }
}
