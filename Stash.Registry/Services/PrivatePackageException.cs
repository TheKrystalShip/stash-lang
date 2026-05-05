using System;

namespace Stash.Registry.Services;

/// <summary>
/// Thrown when a publish request supplies a manifest with <c>"private": true</c>.
/// Maps to HTTP 403 Forbidden at the controller layer.
/// </summary>
public sealed class PrivatePackageException : Exception
{
    public string PackageName { get; }

    public PrivatePackageException(string packageName)
        : base($"Package '{packageName}' is marked as private and cannot be published.")
    {
        PackageName = packageName;
    }
}
