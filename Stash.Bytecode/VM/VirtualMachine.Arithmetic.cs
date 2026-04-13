using System;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Arithmetic, bitwise, and comparison opcode handlers (register-based).
/// Each method decodes A, B, C or A, sBx from the 32-bit instruction word.
/// </summary>
public sealed partial class VirtualMachine
{
    // ══════════════════════════ Arithmetic ══════════════════════════

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteAdd(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromInt(rb.AsInt + rc.AsInt);
        else
            ExecuteAddSlow(ref frame, a, @base, rb, rc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteAddSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
    {
        if (rb.IsNumeric && rc.IsNumeric)
            _stack[@base + a] = StashValue.FromFloat(
                (rb.IsInt ? (double)rb.AsInt : rb.AsFloat) +
                (rc.IsInt ? (double)rc.AsInt : rc.AsFloat));
        else
            _stack[@base + a] = RuntimeOps.Add(rb, rc, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSub(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromInt(rb.AsInt - rc.AsInt);
        else
            ExecuteSubSlow(ref frame, a, @base, rb, rc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteSubSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
    {
        if (rb.IsNumeric && rc.IsNumeric)
            _stack[@base + a] = StashValue.FromFloat(
                (rb.IsInt ? (double)rb.AsInt : rb.AsFloat) -
                (rc.IsInt ? (double)rc.AsInt : rc.AsFloat));
        else
            _stack[@base + a] = RuntimeOps.Subtract(rb, rc, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteMul(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromInt(rb.AsInt * rc.AsInt);
        else
            ExecuteMulSlow(ref frame, a, @base, rb, rc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteMulSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
    {
        if (rb.IsNumeric && rc.IsNumeric)
            _stack[@base + a] = StashValue.FromFloat(
                (rb.IsInt ? (double)rb.AsInt : rb.AsFloat) *
                (rc.IsInt ? (double)rc.AsInt : rc.AsFloat));
        else
            _stack[@base + a] = RuntimeOps.Multiply(rb, rc, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteDiv(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
        {
            long dv = rc.AsInt;
            if (dv == 0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
            _stack[@base + a] = StashValue.FromInt(rb.AsInt / dv);
        }
        else
            ExecuteDivSlow(ref frame, a, @base, rb, rc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteDivSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
    {
        if (rb.IsNumeric && rc.IsNumeric)
        {
            double bd = rb.IsInt ? (double)rb.AsInt : rb.AsFloat;
            double cd = rc.IsInt ? (double)rc.AsInt : rc.AsFloat;
            if (cd == 0.0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
            _stack[@base + a] = StashValue.FromFloat(bd / cd);
        }
        else
            _stack[@base + a] = RuntimeOps.Divide(rb, rc, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteMod(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
        {
            long dv = rc.AsInt;
            if (dv == 0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
            _stack[@base + a] = StashValue.FromInt(rb.AsInt % dv);
        }
        else
            ExecuteModSlow(ref frame, a, @base, rb, rc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteModSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
    {
        if (rb.IsNumeric && rc.IsNumeric)
        {
            double bd = rb.IsInt ? (double)rb.AsInt : rb.AsFloat;
            double cd = rc.IsInt ? (double)rc.AsInt : rc.AsFloat;
            if (cd == 0.0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
            _stack[@base + a] = StashValue.FromFloat(bd % cd);
        }
        else
            _stack[@base + a] = RuntimeOps.Modulo(rb, rc, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecutePow(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromInt((long)Math.Pow(rb.AsInt, rc.AsInt));
        else
            ExecutePowSlow(ref frame, a, @base, rb, rc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecutePowSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
    {
        if (rb.IsNumeric && rc.IsNumeric)
        {
            double bd = rb.IsInt ? (double)rb.AsInt : rb.AsFloat;
            double cd = rc.IsInt ? (double)rc.AsInt : rc.AsFloat;
            _stack[@base + a] = StashValue.FromFloat(Math.Pow(bd, cd));
        }
        else
            _stack[@base + a] = RuntimeOps.Power(rb, rc, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteNeg(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b];
        if (rb.IsInt)
            _stack[@base + a] = StashValue.FromInt(-rb.AsInt);
        else if (rb.IsFloat)
            _stack[@base + a] = StashValue.FromFloat(-rb.AsFloat);
        else
            _stack[@base + a] = RuntimeOps.Negate(rb, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteAddI(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        int sbx = Instruction.GetSBx(inst);
        int @base = frame.BaseSlot;
        StashValue val = _stack[@base + a];
        if (val.IsInt)
            _stack[@base + a] = StashValue.FromInt(val.AsInt + sbx);
        else if (val.IsFloat)
            _stack[@base + a] = StashValue.FromFloat(val.AsFloat + sbx);
        else
            throw new RuntimeError("Operand of '++' or '--' must be a number.", GetCurrentSpan(ref frame));
    }

    // ══════════════════════════ Bitwise ══════════════════════════

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteBAnd(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromInt(rb.AsInt & rc.AsInt);
        else
            _stack[@base + a] = RuntimeOps.BitAnd(rb, rc, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteBOr(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromInt(rb.AsInt | rc.AsInt);
        else
            _stack[@base + a] = RuntimeOps.BitOr(rb, rc, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteBXor(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromInt(rb.AsInt ^ rc.AsInt);
        else
            _stack[@base + a] = RuntimeOps.BitXor(rb, rc, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteBNot(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b];
        if (rb.IsInt)
            _stack[@base + a] = StashValue.FromInt(~rb.AsInt);
        else
            _stack[@base + a] = RuntimeOps.BitNot(rb, GetCurrentSpan(ref frame));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteShl(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        SourceSpan? span = GetCurrentSpan(ref frame);
        if (rb.IsInt && rc.IsInt)
        {
            long shift = rc.AsInt;
            if (shift < 0 || shift > 63)
                throw new RuntimeError("Shift count must be in the range 0..63.", span);
            _stack[@base + a] = StashValue.FromInt(rb.AsInt << (int)shift);
        }
        else
            _stack[@base + a] = RuntimeOps.ShiftLeft(rb, rc, span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteShr(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        SourceSpan? span = GetCurrentSpan(ref frame);
        if (rb.IsInt && rc.IsInt)
        {
            long shift = rc.AsInt;
            if (shift < 0 || shift > 63)
                throw new RuntimeError("Shift count must be in the range 0..63.", span);
            _stack[@base + a] = StashValue.FromInt(rb.AsInt >> (int)shift);
        }
        else
            _stack[@base + a] = RuntimeOps.ShiftRight(rb, rc, span);
    }

    // ══════════════════════════ Comparison ══════════════════════════

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteEq(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromBool(rb.AsInt == rc.AsInt);
        else
            _stack[@base + a] = StashValue.FromBool(RuntimeOps.IsEqual(rb, rc));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteNe(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromBool(rb.AsInt != rc.AsInt);
        else
            _stack[@base + a] = StashValue.FromBool(!RuntimeOps.IsEqual(rb, rc));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteLt(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromBool(rb.AsInt < rc.AsInt);
        else
            ExecuteLtSlow(ref frame, a, @base, rb, rc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteLtSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
    {
        if (rb.IsNumeric && rc.IsNumeric)
            _stack[@base + a] = StashValue.FromBool(
                (rb.IsInt ? (double)rb.AsInt : rb.AsFloat) <
                (rc.IsInt ? (double)rc.AsInt : rc.AsFloat));
        else
            _stack[@base + a] = StashValue.FromBool(RuntimeOps.LessThan(rb, rc, GetCurrentSpan(ref frame)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteLe(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromBool(rb.AsInt <= rc.AsInt);
        else
            ExecuteLeSlow(ref frame, a, @base, rb, rc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteLeSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
    {
        if (rb.IsNumeric && rc.IsNumeric)
            _stack[@base + a] = StashValue.FromBool(
                (rb.IsInt ? (double)rb.AsInt : rb.AsFloat) <=
                (rc.IsInt ? (double)rc.AsInt : rc.AsFloat));
        else
            _stack[@base + a] = StashValue.FromBool(RuntimeOps.LessEqual(rb, rc, GetCurrentSpan(ref frame)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteGt(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromBool(rb.AsInt > rc.AsInt);
        else
            ExecuteGtSlow(ref frame, a, @base, rb, rc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteGtSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
    {
        if (rb.IsNumeric && rc.IsNumeric)
            _stack[@base + a] = StashValue.FromBool(
                (rb.IsInt ? (double)rb.AsInt : rb.AsFloat) >
                (rc.IsInt ? (double)rc.AsInt : rc.AsFloat));
        else
            _stack[@base + a] = StashValue.FromBool(RuntimeOps.GreaterThan(rb, rc, GetCurrentSpan(ref frame)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteGe(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        StashValue rb = _stack[@base + b], rc = _stack[@base + c];
        if (rb.IsInt && rc.IsInt)
            _stack[@base + a] = StashValue.FromBool(rb.AsInt >= rc.AsInt);
        else
            ExecuteGeSlow(ref frame, a, @base, rb, rc);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteGeSlow(ref CallFrame frame, byte a, int @base, StashValue rb, StashValue rc)
    {
        if (rb.IsNumeric && rc.IsNumeric)
            _stack[@base + a] = StashValue.FromBool(
                (rb.IsInt ? (double)rb.AsInt : rb.AsFloat) >=
                (rc.IsInt ? (double)rc.AsInt : rc.AsFloat));
        else
            _stack[@base + a] = StashValue.FromBool(RuntimeOps.GreaterEqual(rb, rc, GetCurrentSpan(ref frame)));
    }

    private void ExecuteIn(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst), b = Instruction.GetB(inst), c = Instruction.GetC(inst);
        int @base = frame.BaseSlot;
        _stack[@base + a] = StashValue.FromBool(
            RuntimeOps.Contains(_stack[@base + b], _stack[@base + c], GetCurrentSpan(ref frame)));
    }
}
