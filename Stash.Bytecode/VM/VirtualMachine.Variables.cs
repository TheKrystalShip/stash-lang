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
        ushort nameIdx = ReadU16(ref frame);
        string name = (string)frame.Chunk.Constants[nameIdx].AsObj!;
        Dictionary<string, object?> globals = frame.ModuleGlobals ?? _globals;
        if (!globals.TryGetValue(name, out object? value))
        {
            throw new RuntimeError($"Undefined variable '{name}'.", GetCurrentSpan(ref frame));
        }

        Push(StashValue.FromObject(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteStoreGlobal(ref CallFrame frame)
    {
        ushort nameIdx = ReadU16(ref frame);
        string name = (string)frame.Chunk.Constants[nameIdx].AsObj!;
        if (_constGlobals.Contains(name))
        {
            throw new RuntimeError("Assignment to constant variable.", GetCurrentSpan(ref frame));
        }

        Dictionary<string, object?> globals = frame.ModuleGlobals ?? _globals;
        globals[name] = Pop().ToObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteInitConstGlobal(ref CallFrame frame)
    {
        ushort nameIdx = ReadU16(ref frame);
        string name = (string)frame.Chunk.Constants[nameIdx].AsObj!;
        _globals[name] = Pop().ToObject();
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
