using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Debugging;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Fused superinstruction handlers that combine multiple opcodes into single operations.
/// </summary>
public sealed partial class VirtualMachine
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteLL_Add(ref CallFrame frame)
    {
        byte slot1 = ReadByte(ref frame);
        byte slot2 = ReadByte(ref frame);
        StashValue a = _stack[frame.BaseSlot + slot1];
        StashValue b = _stack[frame.BaseSlot + slot2];
        if (a.IsInt && b.IsInt)
            Push(StashValue.FromInt(a.AsInt + b.AsInt));
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromFloat(ad + bd));
        }
        else
            Push(RuntimeOps.Add(a, b, GetCurrentSpan(ref frame)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteLC_Add(ref CallFrame frame)
    {
        byte slot = ReadByte(ref frame);
        ushort constIdx = ReadU16(ref frame);
        StashValue a = _stack[frame.BaseSlot + slot];
        StashValue b = frame.Chunk.Constants[constIdx];
        if (a.IsInt && b.IsInt)
            Push(StashValue.FromInt(a.AsInt + b.AsInt));
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromFloat(ad + bd));
        }
        else
            Push(RuntimeOps.Add(a, b, GetCurrentSpan(ref frame)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteLC_LessThan(ref CallFrame frame)
    {
        byte slot = ReadByte(ref frame);
        ushort constIdx = ReadU16(ref frame);
        StashValue a = _stack[frame.BaseSlot + slot];
        StashValue b = frame.Chunk.Constants[constIdx];
        if (a.IsInt && b.IsInt)
            Push(StashValue.FromBool(a.AsInt < b.AsInt));
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromBool(ad < bd));
        }
        else
            Push(StashValue.FromBool(RuntimeOps.LessThan(a, b, GetCurrentSpan(ref frame))));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteDupStoreLocalPop(ref CallFrame frame)
    {
        byte slot = ReadByte(ref frame);
        _stack[frame.BaseSlot + slot] = _stack[_sp - 1];
        _sp--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteLL_LessThan(ref CallFrame frame)
    {
        byte slot1 = ReadByte(ref frame);
        byte slot2 = ReadByte(ref frame);
        StashValue a = _stack[frame.BaseSlot + slot1];
        StashValue b = _stack[frame.BaseSlot + slot2];
        if (a.IsInt && b.IsInt)
            Push(StashValue.FromBool(a.AsInt < b.AsInt));
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromBool(ad < bd));
        }
        else
            Push(StashValue.FromBool(RuntimeOps.LessThan(a, b, GetCurrentSpan(ref frame))));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteLC_Subtract(ref CallFrame frame)
    {
        byte slot = ReadByte(ref frame);
        ushort constIdx = ReadU16(ref frame);
        StashValue a = _stack[frame.BaseSlot + slot];
        StashValue b = frame.Chunk.Constants[constIdx];
        if (a.IsInt && b.IsInt)
            Push(StashValue.FromInt(a.AsInt - b.AsInt));
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromFloat(ad - bd));
        }
        else
            Push(RuntimeOps.Subtract(a, b, GetCurrentSpan(ref frame)));
    }

    /// <summary>
    /// Fused LoadLocal + Return: reads local directly and returns without Push+Pop cycle.
    /// Returns true if execution should exit RunInner (same contract as ExecuteReturn).
    /// </summary>
    private bool ExecuteL_Return(ref CallFrame frame, int targetFrameCount, IDebugger? debugger, out object? result)
    {
        byte slot = ReadByte(ref frame);
        StashValue retVal = _stack[frame.BaseSlot + slot];
        return ExecuteReturnValue(retVal, targetFrameCount, debugger, out result);
    }
}
