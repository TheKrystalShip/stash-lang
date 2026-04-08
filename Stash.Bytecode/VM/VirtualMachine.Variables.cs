using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Variable load/store opcode handlers for globals, locals, and upvalues.
/// </summary>
public sealed partial class VirtualMachine
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteLoadGlobal(ref CallFrame frame)
    {
        ushort slot = ReadU16(ref frame);
        Dictionary<string, StashValue>? mg = frame.ModuleGlobals;
        if (mg == null || mg == _globals)
        {
            // Fast path: main-script global — direct array access
            StashValue val = _globalSlots[slot];
            if (val.AsObj == _undefinedSentinel)
            {
                ThrowUndefinedGlobal(ref frame, slot);
            }
            Push(val);
        }
        else
        {
            // Module function — fall back to dict lookup using name table
            string name = frame.Chunk.GlobalNameTable![slot];
            if (!mg.TryGetValue(name, out StashValue value))
            {
                throw new RuntimeError($"Undefined variable '{name}'.", GetCurrentSpan(ref frame));
            }
            Push(value);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowUndefinedGlobal(ref CallFrame frame, ushort slot)
    {
        string name = _globalNameTable.Length > slot ? _globalNameTable[slot] : $"<slot {slot}>";
        throw new RuntimeError($"Undefined variable '{name}'.", GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteStoreGlobal(ref CallFrame frame)
    {
        ushort slot = ReadU16(ref frame);
        Dictionary<string, StashValue>? mg = frame.ModuleGlobals;
        if (mg == null || mg == _globals)
        {
            // Fast path: main-script global
            if (_constGlobalSlots.Length > slot && _constGlobalSlots[slot])
            {
                throw new RuntimeError("Assignment to constant variable.", GetCurrentSpan(ref frame));
            }
            StashValue val = Pop();
            _globalSlots[slot] = val;
            // Write-through to dict for module loading, debugger, REPL compatibility
            string name = _globalNameTable[slot];
            _globals[name] = val;
        }
        else
        {
            // Module function — dict-based
            string name = frame.Chunk.GlobalNameTable![slot];
            if (_constGlobals.Contains(name))
            {
                throw new RuntimeError("Assignment to constant variable.", GetCurrentSpan(ref frame));
            }
            mg[name] = Pop();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteInitConstGlobal(ref CallFrame frame)
    {
        ushort slot = ReadU16(ref frame);
        StashValue val = Pop();
        // Slot-based path
        if (_globalSlots.Length > slot)
        {
            _globalSlots[slot] = val;
            _constGlobalSlots[slot] = true;
        }
        // Write-through to dict + constGlobals set
        string name = _globalNameTable.Length > slot ? _globalNameTable[slot] : frame.Chunk.GlobalNameTable![slot];
        _globals[name] = val;
        _constGlobals.Add(name);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteConst(ref CallFrame frame)
    {
        ushort idx = ReadU16(ref frame);
        Push(frame.Chunk.Constants[idx]);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteLoadLocal(ref CallFrame frame)
    {
        byte slot = ReadByte(ref frame);
        Push(_stack[frame.BaseSlot + slot]);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteStoreLocal(ref CallFrame frame)
    {
        byte slot = ReadByte(ref frame);
        _stack[frame.BaseSlot + slot] = Pop();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteLoadUpvalue(ref CallFrame frame)
    {
        byte idx = ReadByte(ref frame);
        Push(frame.Upvalues![idx].Value);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteStoreUpvalue(ref CallFrame frame)
    {
        byte idx = ReadByte(ref frame);
        frame.Upvalues![idx].Value = Pop();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteCheckNumeric(ref CallFrame frame)
    {
        if (!Peek().IsNumeric)
        {
            throw new RuntimeError("Operand of '++' or '--' must be a number.", GetCurrentSpan(ref frame));
        }
    }
}
