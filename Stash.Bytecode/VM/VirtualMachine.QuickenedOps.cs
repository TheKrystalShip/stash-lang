using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Specialized (quickened) opcode handlers for integer-typed operands.
/// Each handler has a fast path for the specialized type and falls back
/// to de-specialization + generic handler on type guard failure.
/// </summary>
public sealed partial class VirtualMachine
{
    // ══════════════════════════ Quickened Arithmetic ══════════════════════════

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteAddII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            _stack[@base + a] = StashValue.FromInt(rb.AsInt + rc.AsInt);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Add);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSubII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            _stack[@base + a] = StashValue.FromInt(rb.AsInt - rc.AsInt);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Sub);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteMulII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            _stack[@base + a] = StashValue.FromInt(rb.AsInt * rc.AsInt);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Mul);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteDivII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            long dv = rc.AsInt;
            if (dv == 0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
            _stack[@base + a] = StashValue.FromInt(rb.AsInt / dv);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Div);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteModII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            long dv = rc.AsInt;
            if (dv == 0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
            _stack[@base + a] = StashValue.FromInt(rb.AsInt % dv);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Mod);
    }

    // ══════════════════════════ Quickened Comparison ══════════════════════════

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteLtII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            _stack[@base + a] = StashValue.FromBool(rb.AsInt < rc.AsInt);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Lt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteLeII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            _stack[@base + a] = StashValue.FromBool(rb.AsInt <= rc.AsInt);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Le);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteGtII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            _stack[@base + a] = StashValue.FromBool(rb.AsInt > rc.AsInt);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Gt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteGeII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            _stack[@base + a] = StashValue.FromBool(rb.AsInt >= rc.AsInt);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Ge);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteEqII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            _stack[@base + a] = StashValue.FromBool(rb.AsInt == rc.AsInt);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Eq);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteNeII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];

        if (rb.IsInt && rc.IsInt)
        {
            _stack[@base + a] = StashValue.FromBool(rb.AsInt != rc.AsInt);
            return;
        }

        DeSpecAndFallback(ref frame, inst, OpCode.Ne);
    }

    // ══════════════════════════ Quickened Iteration ══════════════════════════

    /// <summary>
    /// Specialized ForPrep for integer for-loops. Guards on counter and step being int.
    /// On guard failure, de-specializes both ForPrepII and ForLoopII, then falls back to generic.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteForPrepII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        int @base = frame.BaseSlot;

        StashValue counter = _stack[@base + a];
        StashValue step = _stack[@base + a + 2];

        if (counter.IsInt && _stack[@base + a + 1].IsInt && step.IsInt)
        {
            _stack[@base + a] = StashValue.FromInt(counter.AsInt - step.AsInt);
            frame.IP += Instruction.GetSBx(inst);
            return;
        }

        // Guard failure: de-specialize ForPrep AND ForLoop
        DeSpecializeForLoop(frame.Chunk, frame.IP - 1, Instruction.GetSBx(inst));
        ExecuteForPrep(ref frame, inst);
    }

    /// <summary>
    /// Guard-free integer for-loop step. No type checks — trusts ForPrepII's type verification.
    /// This is the highest-value optimization: eliminates all type checks from the inner loop.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteForLoopII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        int @base = frame.BaseSlot;

        long step = _stack[@base + a + 2].AsInt;
        long newCounter = _stack[@base + a].AsInt + step;
        _stack[@base + a] = StashValue.FromInt(newCounter);

        long limit = _stack[@base + a + 1].AsInt;
        if (step > 0 ? newCounter <= limit : newCounter >= limit)
        {
            frame.IP += Instruction.GetSBx(inst);
            _stack[@base + a + 3] = StashValue.FromInt(newCounter);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DeSpecAndFallback(ref CallFrame frame, uint inst, OpCode genericOp)
    {
        DeSpecialize(frame.Chunk, frame.IP - 1, genericOp);
        switch (genericOp)
        {
            case OpCode.Add: ExecuteAdd(ref frame, inst); break;
            case OpCode.Sub: ExecuteSub(ref frame, inst); break;
            case OpCode.Mul: ExecuteMul(ref frame, inst); break;
            case OpCode.Div: ExecuteDiv(ref frame, inst); break;
            case OpCode.Mod: ExecuteMod(ref frame, inst); break;
            case OpCode.Lt:  ExecuteLt(ref frame, inst); break;
            case OpCode.Le:  ExecuteLe(ref frame, inst); break;
            case OpCode.Gt:  ExecuteGt(ref frame, inst); break;
            case OpCode.Ge:  ExecuteGe(ref frame, inst); break;
            case OpCode.Eq:  ExecuteEq(ref frame, inst); break;
            case OpCode.Ne:  ExecuteNe(ref frame, inst); break;
        }
    }
}
