using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Services;

/// <summary>
/// Service layer for package role assignment and revocation.
/// </summary>
/// <remarks>
/// <para>
/// This service owns the <b>last-owner orphan-protection invariant</b> (D18): a
/// revoke that would drop the package's direct owner count to zero is refused with
/// <see cref="LastOwnerException"/> (HTTP 409).  The invariant is enforced here
/// rather than in the PDP so that it also covers admin-revoke paths and any future
/// reassign-owner flow.
/// </para>
/// <para>
/// The PDP is responsible for deciding whether the <em>caller</em> has the right to
/// mutate roles; this service performs the mutation and enforces integrity constraints.
/// </para>
/// </remarks>
public sealed class PackageRoleService
{
    private readonly IRegistryDatabase _db;

    /// <summary>
    /// Initialises the service with the registry database.
    /// </summary>
    public PackageRoleService(IRegistryDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Revokes the role of a principal on a package.
    /// </summary>
    /// <param name="packageName">The full scoped package name (e.g. <c>@alice/widgets</c>).</param>
    /// <param name="principalType">The principal type: <c>user</c>, <c>team</c>, or <c>org</c>.</param>
    /// <param name="principalId">The principal identifier (username, team ID, or org ID).</param>
    /// <returns><c>true</c> if the role row existed and was removed.</returns>
    /// <exception cref="RoleNotFoundException">
    /// Thrown when the principal holds no role on the package (HTTP 404).
    /// </exception>
    /// <exception cref="LastOwnerException">
    /// Thrown when removing the role would leave the package with zero direct owners (HTTP 409).
    /// </exception>
    public async Task RevokeRoleAsync(string packageName, string principalType, string principalId)
    {
        // Verify the role actually exists.
        List<PackageRoleEntry> roles = await _db.GetPackageRolesAsync(packageName);
        var target = roles.FirstOrDefault(r =>
            r.PrincipalType == principalType &&
            r.PrincipalId == principalId);

        if (target == null)
            throw new RoleNotFoundException(packageName, principalType, principalId);

        // Last-owner protection: count direct owner entries (principal_type == user, role == owner).
        // The invariant covers any principal type: if the target is the last owner, refuse.
        if (target.Role == "owner")
        {
            int ownerCount = roles.Count(r => r.Role == "owner");
            if (ownerCount <= 1)
                throw new LastOwnerException(packageName);
        }

        await _db.RevokePackageRoleAsync(packageName, principalType, principalId);
    }
}

/// <summary>
/// Thrown when a revoke operation targets a principal who holds no role on the package.
/// Maps to HTTP 404 at the controller layer.
/// </summary>
public sealed class RoleNotFoundException : System.Exception
{
    public string PackageName { get; }
    public string PrincipalType { get; }
    public string PrincipalId { get; }

    public RoleNotFoundException(string packageName, string principalType, string principalId)
        : base($"Principal '{principalType}:{principalId}' holds no role on package '{packageName}'.")
    {
        PackageName = packageName;
        PrincipalType = principalType;
        PrincipalId = principalId;
    }
}

/// <summary>
/// Thrown when a revoke would leave a package with zero direct owners.
/// Maps to HTTP 409 Conflict at the controller layer.
/// </summary>
public sealed class LastOwnerException : System.Exception
{
    public string PackageName { get; }

    public LastOwnerException(string packageName)
        : base("cannot remove the last owner of a package")
    {
        PackageName = packageName;
    }
}
