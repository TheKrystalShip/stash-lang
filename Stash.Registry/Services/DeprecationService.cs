using System;
using System.Threading.Tasks;
using Stash.Registry.Database;

namespace Stash.Registry.Services;

/// <summary>
/// Service for package and version deprecation operations.
/// </summary>
/// <remarks>
/// Coordinates with <see cref="IRegistryDatabase"/> to set and clear deprecation
/// messages on package and version records. Deprecation is purely informational —
/// deprecated versions remain fully resolvable and installable, but consumers see
/// a warning.
/// </remarks>
public sealed class DeprecationService
{
    private readonly IRegistryDatabase _db;

    public DeprecationService(IRegistryDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Marks an entire package as deprecated with the supplied message and optional alternative.
    /// </summary>
    /// <param name="packageName">The name of the package to deprecate.</param>
    /// <param name="message">A human-readable deprecation message.</param>
    /// <param name="alternative">The suggested replacement package name, or <c>null</c>.</param>
    /// <param name="deprecatedBy">The username performing the deprecation.</param>
    /// <exception cref="InvalidOperationException">Thrown if the package does not exist.</exception>
    public async Task DeprecatePackageAsync(string packageName, string message, string? alternative, string deprecatedBy)
    {
        if (!await _db.PackageExistsAsync(packageName))
        {
            throw new InvalidOperationException($"Package '{packageName}' not found.");
        }

        await _db.DeprecatePackageAsync(packageName, message, alternative, deprecatedBy);
    }

    /// <summary>
    /// Removes the deprecation status from a previously deprecated package.
    /// </summary>
    /// <param name="packageName">The name of the package to undeprecate.</param>
    /// <exception cref="InvalidOperationException">Thrown if the package does not exist.</exception>
    public async Task UndeprecatePackageAsync(string packageName)
    {
        if (!await _db.PackageExistsAsync(packageName))
        {
            throw new InvalidOperationException($"Package '{packageName}' not found.");
        }

        await _db.UndeprecatePackageAsync(packageName);
    }

    /// <summary>
    /// Marks a specific version of a package as deprecated with the supplied message.
    /// </summary>
    /// <param name="packageName">The name of the package containing the version to deprecate.</param>
    /// <param name="version">The version string to deprecate.</param>
    /// <param name="message">A human-readable deprecation message.</param>
    /// <param name="deprecatedBy">The username performing the deprecation.</param>
    /// <exception cref="InvalidOperationException">Thrown if the package or version does not exist.</exception>
    public async Task DeprecateVersionAsync(string packageName, string version, string message, string deprecatedBy)
    {
        if (!await _db.VersionExistsAsync(packageName, version))
        {
            throw new InvalidOperationException($"Version '{version}' of package '{packageName}' not found.");
        }

        await _db.DeprecateVersionAsync(packageName, version, message, deprecatedBy);
    }

    /// <summary>
    /// Removes the deprecation status from a specific version.
    /// </summary>
    /// <param name="packageName">The name of the package containing the version.</param>
    /// <param name="version">The version string to undeprecate.</param>
    /// <exception cref="InvalidOperationException">Thrown if the package or version does not exist.</exception>
    public async Task UndeprecateVersionAsync(string packageName, string version)
    {
        if (!await _db.VersionExistsAsync(packageName, version))
        {
            throw new InvalidOperationException($"Version '{version}' of package '{packageName}' not found.");
        }

        await _db.UndeprecateVersionAsync(packageName, version);
    }
}
