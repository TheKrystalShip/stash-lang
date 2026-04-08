using System.Runtime.InteropServices;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Inline cache slot for GetField operations. Stores the result of a
/// previous field lookup to avoid repeated hash-table probes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ICSlot
{
    /// <summary>The cached receiver object (namespace reference for guard check).</summary>
    public object? Guard;

    /// <summary>The cached result — the resolved StashValue.</summary>
    public StashValue CachedValue;

    /// <summary>IC state: 0 = uninitialized, 1 = monomorphic, 2 = megamorphic.</summary>
    public byte State;
}
