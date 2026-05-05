using System;

namespace Stash.Registry.Services;

/// <summary>
/// Thrown when a publish request supplies a version that already exists for the package.
/// Maps to HTTP 409 Conflict at the controller layer.
/// </summary>
public sealed class VersionConflictException : Exception
{
    public string PackageName { get; }
    public string Version { get; }

    public VersionConflictException(string packageName, string version)
        : base($"Version {version} already exists for package {packageName}.")
    {
        PackageName = packageName;
        Version = version;
    }
}
