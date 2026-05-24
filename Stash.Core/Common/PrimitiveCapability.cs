namespace Stash.Common;

using System;

/// <summary>
/// Capability flags for Stash primitive types. Use <see cref="HasFlag"/> to test individual bits.
/// </summary>
[Flags]
public enum PrimitiveCapability
{
    /// <summary>No special capabilities.</summary>
    None = 0,

    /// <summary>
    /// The type can be the target of an <c>extend</c> block. The bytecode compiler
    /// accepts exactly the primitives flagged with this bit; drift is guarded by
    /// <c>PrimitiveCapabilityInvariantTests</c>.
    /// </summary>
    Extendable = 1 << 0,
}
