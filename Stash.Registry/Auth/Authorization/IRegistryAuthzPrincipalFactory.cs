using System.Security.Claims;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Shared factory that converts an ASP.NET Core <see cref="ClaimsPrincipal"/> (from
/// <c>HttpContext.User</c>) into the PDP's typed <see cref="Principal"/> hierarchy.
/// </summary>
/// <remarks>
/// Registered as Singleton because <see cref="Build"/> is a pure function over the claims
/// principal — it holds no scoped state and reads no database.
/// </remarks>
public interface IRegistryAuthzPrincipalFactory
{
    /// <summary>
    /// Builds a <see cref="Principal"/> from the supplied <paramref name="user"/>.
    /// Returns <see cref="AnonymousPrincipal"/> for unauthenticated users,
    /// or a <see cref="UserPrincipal"/> for authenticated ones.
    /// </summary>
    /// <param name="user">The <see cref="ClaimsPrincipal"/> from <c>HttpContext.User</c>.</param>
    Principal Build(ClaimsPrincipal user);
}
