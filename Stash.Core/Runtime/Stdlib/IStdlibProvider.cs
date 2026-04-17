namespace Stash.Runtime.Stdlib;

using System.Collections.Generic;

/// <summary>
/// A source of globals (namespaces and/or standalone functions)
/// that can be injected into the bytecode VM.
/// </summary>
public interface IStdlibProvider
{
    /// <summary>Returns the namespaces this provider contributes.</summary>
    IReadOnlyList<StdlibNamespaceEntry> GetNamespaces(StashCapabilities capabilities);

    /// <summary>
    /// Returns standalone global functions/values this provider contributes
    /// (e.g., "len", "typeof" — not inside a namespace).
    /// </summary>
    IReadOnlyList<StdlibGlobalEntry> GetGlobals(StashCapabilities capabilities);
}
