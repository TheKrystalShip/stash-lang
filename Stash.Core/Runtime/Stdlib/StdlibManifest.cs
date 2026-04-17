namespace Stash.Runtime.Stdlib;

using System.Collections.Generic;

/// <summary>
/// Declares the stdlib globals that a compiled chunk expects.
/// </summary>
public sealed record StdlibManifest(
    /// <summary>Namespace names the bytecode references (e.g., ["arr", "io", "fs"]).</summary>
    IReadOnlyList<string> RequiredNamespaces,

    /// <summary>Standalone global names the bytecode references (e.g., ["len", "typeof"]).</summary>
    IReadOnlyList<string> RequiredGlobals,

    /// <summary>Minimum capability set needed to satisfy all requirements.</summary>
    StashCapabilities MinimumCapabilities
);
