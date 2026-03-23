namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing the ownership relationship between a user and a package.
/// </summary>
/// <remarks>
/// The composite primary key is (<see cref="PackageName"/>, <see cref="Username"/>).
/// A user listed as an owner may publish new versions and unpublish existing ones for
/// the associated package. Ownership records are managed by
/// <see cref="IRegistryDatabase.AddOwnerAsync"/> and
/// <see cref="IRegistryDatabase.RemoveOwnerAsync"/>. Column names use <c>snake_case</c>.
/// </remarks>
public sealed class OwnerEntry
{
    /// <summary>The package name (part of the composite primary key, foreign key to <see cref="PackageRecord.Name"/>).</summary>
    public string PackageName { get; set; } = "";

    /// <summary>The username of the owner (part of the composite primary key).</summary>
    public string Username { get; set; } = "";
}
