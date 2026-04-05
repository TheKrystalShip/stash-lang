using System;
using System.Collections.Generic;

namespace Stash.Bytecode;

/// <summary>
/// Represents a single local variable tracked during bytecode compilation.
/// </summary>
/// <param name="Name">The variable name as it appears in source.</param>
/// <param name="Depth">The scope depth at which the local was declared (0 = function-level).</param>
/// <param name="IsConst">Whether the variable was declared with <c>const</c>.</param>
/// <param name="Initialized">
/// <c>false</c> between declaration and initialization, preventing self-referencing initializers.
/// </param>
public readonly record struct Local(string Name, int Depth, bool IsConst, bool Initialized);

/// <summary>
/// Tracks local variables for a single function during bytecode compilation.
/// All locals within a function share a flat list where the index is the stack slot.
/// Block scopes are modelled via a depth counter rather than separate scope objects.
/// </summary>
internal sealed class CompilerScope
{
    private readonly List<Local> _locals = new();

    /// <summary>Gets the current block-nesting depth (0 = function-level).</summary>
    public int ScopeDepth { get; private set; }

    /// <summary>Gets the total number of locals currently tracked.</summary>
    public int LocalCount => _locals.Count;

    // ---- Scope Management ----

    /// <summary>Enters a new block scope by incrementing the depth counter.</summary>
    public void BeginScope()
    {
        ScopeDepth++;
    }

    /// <summary>
    /// Exits the current block scope: removes all locals declared at the current depth,
    /// decrements the depth counter, and returns the number of locals removed so the
    /// compiler can emit the corresponding <c>OP_POP</c> instructions.
    /// </summary>
    /// <returns>The number of locals that were popped from the scope.</returns>
    public int EndScope()
    {
        int popped = 0;
        while (_locals.Count > 0 && _locals[_locals.Count - 1].Depth == ScopeDepth)
        {
            _locals.RemoveAt(_locals.Count - 1);
            popped++;
        }

        ScopeDepth--;
        return popped;
    }

    // ---- Local Variable Management ----

    /// <summary>
    /// Declares a new local variable at the current scope depth and returns its stack slot index.
    /// The local is initially marked as uninitialized to guard against self-referencing initializers.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="isConst">Whether the variable is declared with <c>const</c>.</param>
    /// <returns>The flat stack slot index assigned to the new local.</returns>
    public int DeclareLocal(string name, bool isConst)
    {
        int slot = _locals.Count;
        _locals.Add(new Local(name, ScopeDepth, isConst, Initialized: false));
        return slot;
    }

    /// <summary>
    /// Marks the local at <paramref name="slot"/> as fully initialized.
    /// Called after the initializer expression has been compiled.
    /// </summary>
    /// <param name="slot">The stack slot index of the local to mark.</param>
    public void MarkInitialized(int slot)
    {
        var local = _locals[slot];
        _locals[slot] = local with { Initialized = true };
    }

    // ---- Variable Resolution ----

    /// <summary>
    /// Searches for a local variable by name, walking backwards through the list so that
    /// inner-scope declarations shadow outer ones.
    /// </summary>
    /// <param name="name">The variable name to look up.</param>
    /// <returns>The stack slot index of the innermost matching local, or <c>-1</c> if not found.</returns>
    public int ResolveLocal(string name)
    {
        for (int i = _locals.Count - 1; i >= 0; i--)
        {
            if (_locals[i].Name == name)
                return i;
        }

        return -1;
    }

    // ---- Queries ----

    /// <summary>Returns <c>true</c> if the local at <paramref name="slot"/> was declared with <c>const</c>.</summary>
    /// <param name="slot">The stack slot index.</param>
    public bool IsLocalConst(int slot) => _locals[slot].IsConst;

    /// <summary>Returns the <see cref="Local"/> record at the given stack slot index.</summary>
    /// <param name="slot">The stack slot index.</param>
    public Local GetLocal(int slot) => _locals[slot];

    /// <summary>Returns the names of all currently tracked locals, indexed by slot.</summary>
    public string[] GetLocalNames()
    {
        var names = new string[_locals.Count];
        for (int i = 0; i < _locals.Count; i++)
            names[i] = _locals[i].Name;
        return names;
    }

    /// <summary>Returns the const flags of all currently tracked locals, indexed by slot.</summary>
    public bool[] GetLocalIsConst()
    {
        var flags = new bool[_locals.Count];
        for (int i = 0; i < _locals.Count; i++)
            flags[i] = _locals[i].IsConst;
        return flags;
    }
}
