using System;

namespace Stash.Registry.Services;

// BLOCKED: PA-1 — deprecation mechanism TBD.
// This service will handle package and version deprecation once the
// deprecation spec is finalized. Currently a stub.

/// <summary>
/// Stub service for package and version deprecation operations.
/// </summary>
/// <remarks>
/// <para>
/// All methods in this class throw <see cref="NotSupportedException"/> because the
/// deprecation mechanism has not yet been specified. Tracked under issue PA-1.
/// </para>
/// <para>
/// Once the deprecation spec is finalised, this service will coordinate with
/// <see cref="IRegistryDatabase"/> to set and clear deprecation messages on package
/// and version records, and expose the results through the package metadata endpoints.
/// </para>
/// </remarks>
public sealed class DeprecationService
{
    /// <summary>
    /// Marks an entire package as deprecated with the supplied message.
    /// </summary>
    /// <remarks>
    /// Not yet implemented — tracked under PA-1. Always throws <see cref="NotSupportedException"/>.
    /// </remarks>
    /// <param name="packageName">The name of the package to deprecate.</param>
    /// <param name="message">A human-readable deprecation message to store alongside the package.</param>
    /// <exception cref="NotSupportedException">Always thrown; deprecation is not yet implemented.</exception>
    public void DeprecatePackage(string packageName, string message)
    {
        // BLOCKED: PA-1
        throw new NotSupportedException("Package deprecation is not yet implemented.");
    }

    /// <summary>
    /// Marks a specific version of a package as deprecated with the supplied message.
    /// </summary>
    /// <remarks>
    /// Not yet implemented — tracked under PA-1. Always throws <see cref="NotSupportedException"/>.
    /// </remarks>
    /// <param name="packageName">The name of the package containing the version to deprecate.</param>
    /// <param name="version">The version string to deprecate.</param>
    /// <param name="message">A human-readable deprecation message to store alongside the version.</param>
    /// <exception cref="NotSupportedException">Always thrown; deprecation is not yet implemented.</exception>
    public void DeprecateVersion(string packageName, string version, string message)
    {
        // BLOCKED: PA-1
        throw new NotSupportedException("Version deprecation is not yet implemented.");
    }

    /// <summary>
    /// Removes the deprecation status from a previously deprecated package.
    /// </summary>
    /// <remarks>
    /// Not yet implemented — tracked under PA-1. Always throws <see cref="NotSupportedException"/>.
    /// </remarks>
    /// <param name="packageName">The name of the package to undeprecate.</param>
    /// <exception cref="NotSupportedException">Always thrown; deprecation is not yet implemented.</exception>
    public void UndeprecatePackage(string packageName)
    {
        // BLOCKED: PA-1
        throw new NotSupportedException("Package undeprecation is not yet implemented.");
    }
}
