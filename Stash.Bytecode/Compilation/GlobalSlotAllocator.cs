using System;
using System.Collections.Generic;

namespace Stash.Bytecode;

/// <summary>
/// Assigns monotonically increasing integer slot indices to global variable names.
/// Shared across all Compiler instances in a single compilation unit so that
/// all chunks (top-level script + nested functions) use consistent slot assignments.
/// </summary>
internal sealed class GlobalSlotAllocator
{
    private readonly Dictionary<string, ushort> _nameToSlot = new(StringComparer.Ordinal);
    private ushort _nextSlot;
    private readonly Dictionary<string, object?> _constValues = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the slot index for <paramref name="name"/>, allocating a new one if first seen.
    /// </summary>
    public ushort GetOrAllocate(string name)
    {
        if (!_nameToSlot.TryGetValue(name, out ushort slot))
        {
            slot = _nextSlot++;
            _nameToSlot[name] = slot;
        }
        return slot;
    }

    /// <summary>Records the compile-time literal value of a const global.</summary>
    public void TrackConstValue(string name, object? value) => _constValues[name] = value;

    /// <summary>Returns true and the tracked value if <paramref name="name"/> is a folded const global.</summary>
    public bool TryGetConstValue(string name, out object? value) => _constValues.TryGetValue(name, out value);

    /// <summary>Total number of global slots allocated so far.</summary>
    public int Count => _nextSlot;

    /// <summary>
    /// Builds the reverse mapping: slot index → variable name.
    /// Used by the VM for error messages and module compatibility.
    /// </summary>
    public string[] BuildNameTable()
    {
        var names = new string[_nextSlot];
        foreach (var (name, slot) in _nameToSlot)
            names[slot] = name;
        return names;
    }
}
