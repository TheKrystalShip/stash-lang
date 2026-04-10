namespace Stash.Analysis;

using System.Collections.Generic;

/// <summary>
/// Represents per-file rule overrides defined in the <c>[per-file-overrides]</c> section
/// of a <c>.stashcheck</c> configuration file.
/// </summary>
public sealed class PerFileOverride
{
    /// <summary>Whether ALL diagnostics are suppressed for files matching this glob pattern.</summary>
    public bool DisableAll { get; init; }

    /// <summary>Specific diagnostic codes (or prefixes) to suppress for matching files.</summary>
    public IReadOnlySet<string> DisabledCodes { get; init; } = new HashSet<string>();
}
