using System.Collections.Generic;
using Stash.Common;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Minimal abstraction over the registry needed by <see cref="Commands.OutdatedCommand"/>
/// so tests can supply an in-memory implementation.
/// </summary>
public interface IVersionLookup
{
    /// <summary>
    /// Fetches the published versions and the <c>latest</c> pointer for a package.
    /// </summary>
    (List<SemVer> Versions, SemVer? Latest) GetVersionsAndLatest(string packageName);
}
