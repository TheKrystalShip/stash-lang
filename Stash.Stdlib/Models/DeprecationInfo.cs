namespace Stash.Stdlib.Models;

/// <summary>
/// Marks a built-in member as deprecated and points users at its replacement.
/// Surfaced through the SA0830 analysis diagnostic.
/// </summary>
/// <param name="ReplacementQualifiedName">The fully-qualified name to use instead, e.g. "env.chdir" or "Signal.Term".</param>
public record DeprecationInfo(string ReplacementQualifiedName);
