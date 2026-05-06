namespace Stash.Runtime;

using System.Collections.Generic;

/// <summary>
/// Cross-layer hook for shell expansion helpers used by <c>process.exec</c>.
/// </summary>
/// <remarks>
/// <para>
/// Tilde expansion is implemented directly in the callers (trivial path rewriting).
/// Glob expansion requires <see cref="GlobExpandHandler"/> which Stash.Bytecode wires
/// up at startup so that Stash.Stdlib (which cannot reference Stash.Bytecode directly)
/// can still benefit from the full glob-matching implementation.
/// </para>
/// <para>
/// In Phase A this handler is not wired; the <c>process.exec</c> function falls back to
/// treating unquoted <see cref="Types.StashLiteralArg"/> tokens as literal strings.
/// Phase B compiler emission of LiteralArg constants will wire this handler.
/// </para>
/// </remarks>
public static class ShellExpansion
{
    /// <summary>
    /// Optional glob expansion hook. When set, called with a glob pattern and a
    /// working directory; returns the sorted list of matching file paths, or an
    /// empty list when there are no matches (the caller decides the fallback).
    /// </summary>
    public static System.Func<string, string, List<string>>? GlobExpandHandler { get; set; }
}
