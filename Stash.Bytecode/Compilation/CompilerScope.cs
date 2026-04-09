using System;
using System.Collections.Generic;

namespace Stash.Bytecode;

/// <summary>
/// Tracks register allocation for a single function during compilation.
/// Registers are laid out as: [params][locals][temporaries].
/// </summary>
internal sealed class CompilerScope
{
    private readonly record struct Local(string Name, int Depth, bool IsConst, bool Initialized);

    private readonly List<Local> _locals = new();
    private readonly Dictionary<int, string> _localNamesByReg = new();
    private readonly Dictionary<int, bool> _localConstByReg = new();
    private readonly Stack<int> _scopeNextFreeRegs = new();
    private readonly HashSet<int> _knownNumericRegs = new();
    private int _nextFreeReg;  // Next available register (after all locals + temps in use)
    private int _maxRegs;      // High water mark of _nextFreeReg

    public int ScopeDepth { get; private set; }

    /// <summary>Peak register count needed by this function.</summary>
    public int MaxRegs => _maxRegs;

    /// <summary>Number of currently declared locals (including params).</summary>
    public int LocalCount => _locals.Count;

    // ==================================================================
    // Locals (permanent registers for the lifetime of their scope)
    // ==================================================================

    /// <summary>
    /// Declare a local variable, assigning it a register.
    /// Returns the register number.
    /// </summary>
    public byte DeclareLocal(string name, bool isConst = false)
    {
        int reg = _locals.Count;
        if (reg > 255)
            throw new InvalidOperationException("Too many local variables (>255).");

        _locals.Add(new Local(name, ScopeDepth, isConst, Initialized: false));
        _localNamesByReg[reg] = name;
        _localConstByReg[reg] = isConst;

        // Locals always occupy registers in order, so the next free reg is at least past this local
        if (reg + 1 > _nextFreeReg)
            _nextFreeReg = reg + 1;
        if (_nextFreeReg > _maxRegs)
            _maxRegs = _nextFreeReg;

        return (byte)reg;
    }

    /// <summary>Mark the most recently declared local as initialized.</summary>
    public void MarkInitialized()
    {
        if (_locals.Count > 0)
        {
            Local last = _locals[^1];
            _locals[^1] = last with { Initialized = true };
        }
    }

    /// <summary>
    /// Resolve a local variable by name. Returns register number, or -1 if not found.
    /// Searches from most recent to oldest (for shadowing).
    /// </summary>
    public int ResolveLocal(string name)
    {
        for (int i = _locals.Count - 1; i >= 0; i--)
        {
            if (_locals[i].Name == name)
                return i;
        }
        return -1;
    }

    /// <summary>Check if a register holds a const local.</summary>
    public bool IsLocalConst(int reg)
    {
        if (reg >= 0 && reg < _locals.Count)
            return _locals[reg].IsConst;
        return false;
    }

    /// <summary>Mark a local register as known to contain a numeric value.</summary>
    public void MarkNumeric(int reg)
    {
        if (reg >= 0 && reg < _locals.Count)
            _knownNumericRegs.Add(reg);
    }

    /// <summary>Check if a local register is known to contain a numeric value.</summary>
    public bool IsKnownNumeric(int reg) => _knownNumericRegs.Contains(reg);

    /// <summary>Clear the known-numeric flag for a register (e.g., on reassignment from unknown source).</summary>
    public void ClearNumeric(int reg) => _knownNumericRegs.Remove(reg);

    // ==================================================================
    // Temporaries (LIFO allocation above locals)
    // ==================================================================

    /// <summary>
    /// Allocate a temporary register. Must be freed with FreeTemp in LIFO order.
    /// </summary>
    public byte AllocTemp()
    {
        int reg = _nextFreeReg++;
        if (reg > 255)
            throw new InvalidOperationException("Register overflow (>255).");
        if (_nextFreeReg > _maxRegs)
            _maxRegs = _nextFreeReg;
        return (byte)reg;
    }

    /// <summary>
    /// Free a temporary register. Must be the most recently allocated temp (LIFO).
    /// </summary>
    public void FreeTemp(byte reg)
    {
        // Only free if it's the top of the temp stack and above the local region
        if (reg == _nextFreeReg - 1 && reg >= _locals.Count)
            _nextFreeReg--;
    }

    /// <summary>
    /// Free all temporary registers from a given register upward.
    /// </summary>
    public void FreeTempFrom(byte reg)
    {
        if (reg >= _locals.Count && reg < _nextFreeReg)
            _nextFreeReg = reg;
    }

    /// <summary>Reserve N consecutive registers starting at the current free position.</summary>
    public byte ReserveRegs(int count)
    {
        byte start = (byte)_nextFreeReg;
        _nextFreeReg += count;
        if (_nextFreeReg > 256)
            throw new InvalidOperationException("Register overflow (>255).");
        if (_nextFreeReg > _maxRegs)
            _maxRegs = _nextFreeReg;
        return start;
    }

    /// <summary>The next register that would be allocated.</summary>
    public byte NextFreeReg => (byte)_nextFreeReg;

    // ==================================================================
    // Scope Management
    // ==================================================================

    public void BeginScope()
    {
        _scopeNextFreeRegs.Push(_nextFreeReg);
        ScopeDepth++;
    }

    /// <summary>
    /// End the current scope. Returns the number of locals that went out of scope
    /// (for upvalue closing). Frees their registers.
    /// </summary>
    public int EndScope()
    {
        int freed = 0;
        while (_locals.Count > 0 && _locals[^1].Depth == ScopeDepth)
        {
            _locals.RemoveAt(_locals.Count - 1);
            _knownNumericRegs.Remove(_locals.Count); // reg index = count after removal
            freed++;
        }
        // Restore _nextFreeReg: use max of remaining locals and the saved value
        // from before this scope opened, to protect temps still live in enclosing code.
        int savedNextFreeReg = _scopeNextFreeRegs.Pop();
        _nextFreeReg = Math.Max(_locals.Count, savedNextFreeReg);
        ScopeDepth--;
        return freed;
    }

    // ==================================================================
    // Debug Metadata
    // ==================================================================

    /// <summary>Build register→name mapping for the debugger.</summary>
    public string[]? GetLocalNames()
    {
        if (_localNamesByReg.Count == 0) return null;
        int max = 0;
        foreach (int key in _localNamesByReg.Keys)
            if (key > max) max = key;
        string[] names = new string[max + 1];
        foreach (var (reg, name) in _localNamesByReg)
            names[reg] = name;
        return names;
    }

    /// <summary>Build register→const flag mapping for the debugger.</summary>
    public bool[]? GetLocalIsConst()
    {
        if (_localConstByReg.Count == 0) return null;
        int max = 0;
        foreach (int key in _localConstByReg.Keys)
            if (key > max) max = key;
        bool[] flags = new bool[max + 1];
        foreach (var (reg, isConst) in _localConstByReg)
            flags[reg] = isConst;
        return flags;
    }
}
