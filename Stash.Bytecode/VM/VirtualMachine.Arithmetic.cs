using System;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Arithmetic and bitwise opcode handler methods.
/// </summary>
public sealed partial class VirtualMachine
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteAdd(ref CallFrame frame)
    {
        StashValue b = Pop();
        StashValue a = Pop();
        if (a.IsInt && b.IsInt)
        {
            Push(StashValue.FromInt(a.AsInt + b.AsInt));
        }
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromFloat(ad + bd));
        }
        else
        {
            Push(RuntimeOps.Add(a, b, GetCurrentSpan(ref frame)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSubtract(ref CallFrame frame)
    {
        StashValue b = Pop();
        StashValue a = Pop();
        if (a.IsInt && b.IsInt)
        {
            Push(StashValue.FromInt(a.AsInt - b.AsInt));
        }
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromFloat(ad - bd));
        }
        else
        {
            Push(RuntimeOps.Subtract(a, b, GetCurrentSpan(ref frame)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteMultiply(ref CallFrame frame)
    {
        StashValue b = Pop();
        StashValue a = Pop();
        if (a.IsInt && b.IsInt)
        {
            Push(StashValue.FromInt(a.AsInt * b.AsInt));
        }
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromFloat(ad * bd));
        }
        else
        {
            Push(RuntimeOps.Multiply(a, b, GetCurrentSpan(ref frame)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteDivide(ref CallFrame frame)
    {
        StashValue b = Pop();
        StashValue a = Pop();
        if (a.IsInt && b.IsInt)
        {
            long bv = b.AsInt;
            if (bv == 0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
            Push(StashValue.FromInt(a.AsInt / bv));
        }
        else if (a.IsNumeric && b.IsNumeric)
        {
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            if (bd == 0.0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            Push(StashValue.FromFloat(ad / bd));
        }
        else
        {
            Push(RuntimeOps.Divide(a, b, GetCurrentSpan(ref frame)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteModulo(ref CallFrame frame)
    {
        StashValue b = Pop();
        StashValue a = Pop();
        if (a.IsInt && b.IsInt)
        {
            long bv = b.AsInt;
            if (bv == 0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
            Push(StashValue.FromInt(a.AsInt % bv));
        }
        else if (a.IsNumeric && b.IsNumeric)
        {
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            if (bd == 0.0) throw new RuntimeError("Division by zero.", GetCurrentSpan(ref frame));
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            Push(StashValue.FromFloat(ad % bd));
        }
        else
        {
            Push(RuntimeOps.Modulo(a, b, GetCurrentSpan(ref frame)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteNegate(ref CallFrame frame)
    {
        StashValue val = Pop();
        if (val.IsInt)
        {
            Push(StashValue.FromInt(-val.AsInt));
        }
        else if (val.IsFloat)
        {
            Push(StashValue.FromFloat(-val.AsFloat));
        }
        else
        {
            Push(RuntimeOps.Negate(val, GetCurrentSpan(ref frame)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecutePower(ref CallFrame frame)
    {
        StashValue right = Pop();
        StashValue left = Pop();
        if (left.IsInt && right.IsInt)
        {
            Push(StashValue.FromInt((long)Math.Pow(left.AsInt, right.AsInt)));
        }
        else if (left.IsNumeric && right.IsNumeric)
        {
            double ld = left.IsInt ? (double)left.AsInt : left.AsFloat;
            double rd = right.IsInt ? (double)right.AsInt : right.AsFloat;
            Push(StashValue.FromFloat(Math.Pow(ld, rd)));
        }
        else
        {
            Push(RuntimeOps.Power(left, right, GetCurrentSpan(ref frame)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteBitAnd(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        if (a.IsInt && b.IsInt)
        {
            Push(StashValue.FromInt(a.AsInt & b.AsInt));
        }
        else
        {
            Push(RuntimeOps.BitAnd(a, b, GetCurrentSpan(ref frame)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteBitOr(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        if (a.IsInt && b.IsInt)
        {
            Push(StashValue.FromInt(a.AsInt | b.AsInt));
        }
        else
        {
            Push(RuntimeOps.BitOr(a, b, GetCurrentSpan(ref frame)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteBitXor(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        if (a.IsInt && b.IsInt)
        {
            Push(StashValue.FromInt(a.AsInt ^ b.AsInt));
        }
        else
        {
            Push(RuntimeOps.BitXor(a, b, GetCurrentSpan(ref frame)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteIn(ref CallFrame frame)
    {
        StashValue right = Pop();
        StashValue left = Pop();
        Push(StashValue.FromBool(RuntimeOps.Contains(left, right, GetCurrentSpan(ref frame))));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteEqual(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        Push(StashValue.FromBool(RuntimeOps.IsEqual(a, b)));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteNotEqual(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        Push(StashValue.FromBool(!RuntimeOps.IsEqual(a, b)));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteLessThan(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        if (a.IsInt && b.IsInt)
        {
            Push(StashValue.FromBool(a.AsInt < b.AsInt));
        }
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromBool(ad < bd));
        }
        else
        {
            Push(StashValue.FromBool(RuntimeOps.LessThan(a, b, GetCurrentSpan(ref frame))));
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteLessEqual(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        if (a.IsInt && b.IsInt)
        {
            Push(StashValue.FromBool(a.AsInt <= b.AsInt));
        }
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromBool(ad <= bd));
        }
        else
        {
            Push(StashValue.FromBool(RuntimeOps.LessEqual(a, b, GetCurrentSpan(ref frame))));
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteGreaterThan(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        if (a.IsInt && b.IsInt)
        {
            Push(StashValue.FromBool(a.AsInt > b.AsInt));
        }
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromBool(ad > bd));
        }
        else
        {
            Push(StashValue.FromBool(RuntimeOps.GreaterThan(a, b, GetCurrentSpan(ref frame))));
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteGreaterEqual(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        if (a.IsInt && b.IsInt)
        {
            Push(StashValue.FromBool(a.AsInt >= b.AsInt));
        }
        else if (a.IsNumeric && b.IsNumeric)
        {
            double ad = a.IsInt ? (double)a.AsInt : a.AsFloat;
            double bd = b.IsInt ? (double)b.AsInt : b.AsFloat;
            Push(StashValue.FromBool(ad >= bd));
        }
        else
        {
            Push(StashValue.FromBool(RuntimeOps.GreaterEqual(a, b, GetCurrentSpan(ref frame))));
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteShiftLeft(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        if (a.IsInt && b.IsInt)
        {
            long bv = b.AsInt;
            if (bv < 0 || bv > 63) throw new RuntimeError("Shift count must be in the range 0..63.", GetCurrentSpan(ref frame));
            Push(StashValue.FromInt(a.AsInt << (int)bv));
        }
        else
        {
            Push(RuntimeOps.ShiftLeft(a, b, GetCurrentSpan(ref frame)));
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteShiftRight(ref CallFrame frame)
    {
        StashValue b = Pop(), a = Pop();
        if (a.IsInt && b.IsInt)
        {
            long bv = b.AsInt;
            if (bv < 0 || bv > 63) throw new RuntimeError("Shift count must be in the range 0..63.", GetCurrentSpan(ref frame));
            Push(StashValue.FromInt(a.AsInt >> (int)bv));
        }
        else
        {
            Push(RuntimeOps.ShiftRight(a, b, GetCurrentSpan(ref frame)));
        }
    }
}
