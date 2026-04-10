namespace Stash.Analysis;

using System.Collections.Generic;

/// <summary>
/// Registry of built-in domains with their file patterns and rule adjustments.
/// </summary>
public static class DomainRegistry
{
    /// <summary>
    /// Returns the built-in domain configuration for the given domain name and profile.
    /// Returns <see langword="null"/> if the profile is "off" or the domain is unknown.
    /// </summary>
    public static DomainConfig? GetDomain(string name, string profile)
    {
        if (profile == "off") return null;

        return name.ToLowerInvariant() switch
        {
            "test" => new DomainConfig(
                name: "test",
                profile: profile,
                filePatterns: new[] { "**/*.test.stash", "**/*.spec.stash" },
                disabledCodes: profile == "strict"
                    ? (IReadOnlySet<string>)new HashSet<string>()
                    : new HashSet<string> { "SA0206" },  // Relax unused params in test helpers
                enabledCodes: new HashSet<string> { "SA0709" }  // Enable retry-no-throwable in tests
            ),
            "scripts" => new DomainConfig(
                name: "scripts",
                profile: profile,
                filePatterns: new[] { "**/bin/*.stash", "**/scripts/*.stash" },
                disabledCodes: profile == "strict"
                    ? (IReadOnlySet<string>)new HashSet<string>()
                    : new HashSet<string> { "SA0201", "SA0206" },  // Relax unused vars/params in scripts
                enabledCodes: new HashSet<string>()
            ),
            _ => null
        };
    }
}
