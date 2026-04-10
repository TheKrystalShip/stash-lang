namespace Stash.Analysis;

using System.Collections.Generic;

/// <summary>
/// Defines a domain: a named rule profile that auto-applies to files matching specific patterns.
/// </summary>
public sealed class DomainConfig
{
    /// <summary>Domain name (e.g., "test").</summary>
    public string Name { get; }

    /// <summary>Profile level: "recommended", "strict", or "off".</summary>
    public string Profile { get; }

    /// <summary>Glob patterns that match files in this domain.</summary>
    public IReadOnlyList<string> FilePatterns { get; }

    /// <summary>Diagnostic codes to disable in this domain.</summary>
    public IReadOnlySet<string> DisabledCodes { get; }

    /// <summary>Diagnostic codes to enable in this domain.</summary>
    public IReadOnlySet<string> EnabledCodes { get; }

    public DomainConfig(string name, string profile, IReadOnlyList<string> filePatterns,
        IReadOnlySet<string> disabledCodes, IReadOnlySet<string> enabledCodes)
    {
        Name = name;
        Profile = profile;
        FilePatterns = filePatterns;
        DisabledCodes = disabledCodes;
        EnabledCodes = enabledCodes;
    }
}
