using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Stash.Registry.Contracts;
using static Stash.Registry.Auth.TokenCeilingConverter;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Production implementation of <see cref="IRegistryAuthzPrincipalFactory"/>.
/// </summary>
/// <remarks>
/// The body of <see cref="Build"/> is byte-identical to the <c>BuildPrincipal</c>
/// helper that exists in all six registry controllers today.  Registered Singleton
/// (pure function, no scoped state).
/// </remarks>
public sealed class RegistryAuthzPrincipalFactory : IRegistryAuthzPrincipalFactory
{
    /// <inheritdoc />
    public Principal Build(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return new AnonymousPrincipal();

        string username = user.Identity!.Name!;
        bool isAdmin    = user.IsInRole(UserRoles.Admin);
        TokenCeiling ceiling = FromClaimValue(user.FindFirst(RegistryClaims.TokenScope)?.Value);
        UserRole role   = isAdmin ? UserRole.Admin : UserRole.User;
        string tokenId  = user.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? "";
        return new UserPrincipal(username, role, ceiling, tokenId);
    }
}
