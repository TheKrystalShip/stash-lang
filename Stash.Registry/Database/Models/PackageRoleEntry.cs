using Stash.Registry.Contracts;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a principal's role on a specific package.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the old <c>owners</c> table with a fully role-aware model.
/// The composite primary key is (<see cref="PackageName"/>, <see cref="PrincipalType"/>,
/// <see cref="PrincipalId"/>).
/// </para>
/// <para>
/// <see cref="PrincipalType"/> is constrained to <c>user</c>, <c>team</c>, or <c>org</c>.
/// <see cref="Role"/> is constrained to <c>owner</c>, <c>maintainer</c>, <c>publisher</c>,
/// or <c>reader</c>.
/// </para>
/// <para>Column names use <c>snake_case</c> in the database.</para>
/// <para>EF value converters map <see cref="PrincipalType"/> and <see cref="Role"/> to lowercase wire strings.</para>
/// </remarks>
public sealed class PackageRoleEntry
{
    /// <summary>The package name (part of composite primary key, FK to <see cref="PackageRecord.Name"/>).</summary>
    public string PackageName { get; set; } = "";

    /// <summary>The type of principal: <c>user</c>, <c>team</c>, or <c>org</c>.</summary>
    public PrincipalTypes PrincipalType { get; set; }

    /// <summary>
    /// The identifier of the principal — a username for <c>user</c> principals,
    /// a team ID for <c>team</c> principals, an org ID for <c>org</c> principals.
    /// </summary>
    public string PrincipalId { get; set; } = "";

    /// <summary>
    /// The assigned role: <c>owner</c>, <c>maintainer</c>, <c>publisher</c>,
    /// or <c>reader</c>.
    /// </summary>
    public PackageRoles Role { get; set; }
}
