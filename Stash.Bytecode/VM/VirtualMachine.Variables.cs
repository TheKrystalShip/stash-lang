using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Variable load/store opcode handlers for globals and upvalues.
/// </summary>
public sealed partial class VirtualMachine
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteGetGlobal(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        ushort slot = Instruction.GetBx(inst);
        Dictionary<string, StashValue>? mg = frame.ModuleGlobals;
        if (mg == null || mg == _globals)
        {
            StashValue val = _globalSlots[slot];
            if (val.AsObj != _undefinedSentinel)
            {
                _stack[frame.BaseSlot + a] = val;
                return;
            }
        }
        ExecuteGetGlobalSlow(ref frame, a, slot);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteGetGlobalSlow(ref CallFrame frame, byte a, ushort slot)
    {
        Dictionary<string, StashValue>? mg = frame.ModuleGlobals;
        if (mg == null || mg == _globals)
        {
            ThrowUndefinedGlobal(ref frame, slot);
        }
        else
        {
            string name = frame.Chunk.GlobalNameTable![slot];
            if (!mg.TryGetValue(name, out StashValue value))
                throw new RuntimeError($"Undefined variable '{name}'.", GetCurrentSpan(ref frame));
            _stack[frame.BaseSlot + a] = value;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowUndefinedGlobal(ref CallFrame frame, ushort slot)
    {
        string name = _globalNameTable.Length > slot ? _globalNameTable[slot] : $"<slot {slot}>";
        throw new RuntimeError($"Undefined variable '{name}'.", GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSetGlobal(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        ushort slot = Instruction.GetBx(inst);
        Dictionary<string, StashValue>? mg = frame.ModuleGlobals;
        if (mg == null || mg == _globals)
        {
            ExecuteSetMainGlobal(ref frame, a, slot);
            return;
        }

        ExecuteSetModuleGlobal(ref frame, a, slot, mg);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSetMainGlobal(ref CallFrame frame, byte a, ushort slot)
    {
        if (_constGlobalSlots.Length > slot && _constGlobalSlots[slot])
        {
            string name = _globalNameTable.Length > slot ? _globalNameTable[slot] : $"<slot {slot}>";
            throw new RuntimeError($"Cannot assign to constant '{name}'.", GetCurrentSpan(ref frame));
        }

        StashValue val = _stack[frame.BaseSlot + a];
        _globalSlots[slot] = val;
        _globals[_globalNameTable[slot]] = val;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteSetModuleGlobal(ref CallFrame frame, byte a, ushort slot, Dictionary<string, StashValue> moduleGlobals)
    {
        string name = frame.Chunk.GlobalNameTable![slot];
        if (_constGlobals.Contains(name))
            throw new RuntimeError($"Cannot assign to constant '{name}'.", GetCurrentSpan(ref frame));
        moduleGlobals[name] = _stack[frame.BaseSlot + a];
    }

    private void ExecuteInitConstGlobal(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        ushort slot = Instruction.GetBx(inst);
        StashValue val = _stack[frame.BaseSlot + a];
        _globalSlots[slot] = val;
        _constGlobalSlots[slot] = true;
        string name = _globalNameTable.Length > slot
            ? _globalNameTable[slot]
            : frame.Chunk.GlobalNameTable![slot];
        _globals[name] = val;
        _constGlobals.Add(name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteGetUpval(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte idx = Instruction.GetB(inst);
        _stack[frame.BaseSlot + a] = frame.Upvalues![idx].Value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSetUpval(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte idx = Instruction.GetB(inst);
        frame.Upvalues![idx].Value = _stack[frame.BaseSlot + a];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteCloseUpval(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        CloseUpvalues(frame.BaseSlot + a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteCheckNumeric(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        if (!_stack[frame.BaseSlot + a].IsNumeric)
            throw new RuntimeError("Operand of '++' or '--' must be a number.", GetCurrentSpan(ref frame));
    }
}

