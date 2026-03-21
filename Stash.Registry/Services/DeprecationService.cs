using System;

namespace Stash.Registry.Services;

// BLOCKED: PA-1 — deprecation mechanism TBD.
// This service will handle package and version deprecation once the
// deprecation spec is finalized. Currently a stub.
public sealed class DeprecationService
{
    public void DeprecatePackage(string packageName, string message)
    {
        // BLOCKED: PA-1
        throw new NotSupportedException("Package deprecation is not yet implemented.");
    }

    public void DeprecateVersion(string packageName, string version, string message)
    {
        // BLOCKED: PA-1
        throw new NotSupportedException("Version deprecation is not yet implemented.");
    }

    public void UndeprecatePackage(string packageName)
    {
        // BLOCKED: PA-1
        throw new NotSupportedException("Package undeprecation is not yet implemented.");
    }
}
