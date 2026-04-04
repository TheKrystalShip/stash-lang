using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Static methods implementing Stash runtime operations for the bytecode VM.
/// These exactly replicate the tree-walk interpreter's semantics.
/// </summary>
internal static class RuntimeOps
{
    // --- Truthiness ---

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool IsFalsy(object? value) => !RuntimeValues.IsTruthy(value);

    // --- Equality ---

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool IsEqual(object? left, object? right) => RuntimeValues.IsEqual(left, right);

    // --- Stringify ---

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static string Stringify(object? value) => RuntimeValues.Stringify(value);

    // --- Arithmetic ---

    public static object? Add(object? left, object? right, SourceSpan? span)
    {
        // Fast paths first
        if (left is long la && right is long lb) return la + lb;
        if (RuntimeValues.IsNumeric(left) && RuntimeValues.IsNumeric(right))
            return RuntimeValues.ToDouble(left) + RuntimeValues.ToDouble(right);
        // String concatenation — if either side is a string
        if (left is string || right is string)
            return RuntimeValues.Stringify(left) + RuntimeValues.Stringify(right);
        // IP address + offset
        if (left is StashIpAddress ipL && right is long offsetR) return ipL.Add(offsetR);
        if (left is long offsetL && right is StashIpAddress ipR) return ipR.Add(offsetL);
        // Duration + Duration
        if (left is StashDuration durL && right is StashDuration durR) return durL.Add(durR);
        // ByteSize + ByteSize
        if (left is StashByteSize bsL && right is StashByteSize bsR) return bsL.Add(bsR);
        // Type mismatch errors
        if (left is StashDuration or StashByteSize || right is StashDuration or StashByteSize)
            throw new RuntimeError("Cannot mix duration or byte size with other types in addition.", span);
        throw new RuntimeError("Operands must be numbers or strings.", span);
    }

    public static object? Subtract(object? left, object? right, SourceSpan? span)
    {
        if (left is long la && right is long lb) return la - lb;
        if (RuntimeValues.IsNumeric(left) && RuntimeValues.IsNumeric(right))
            return RuntimeValues.ToDouble(left) - RuntimeValues.ToDouble(right);
        if (left is StashIpAddress ipSubL && right is StashIpAddress ipSubR) return ipSubL.Subtract(ipSubR);
        if (left is StashIpAddress ipSubA && right is long offsetSub) return ipSubA.Add(-offsetSub);
        if (left is StashDuration durSubL && right is StashDuration durSubR) return durSubL.Subtract(durSubR);
        if (left is StashByteSize bsSubL && right is StashByteSize bsSubR) return bsSubL.Subtract(bsSubR);
        if (left is StashDuration or StashByteSize || right is StashDuration or StashByteSize)
            throw new RuntimeError("Cannot mix duration or byte size with other types in subtraction.", span);
        throw new RuntimeError("Operands must be two numbers or two IP addresses.", span);
    }

    public static object? Multiply(object? left, object? right, SourceSpan? span)
    {
        // String repeat: "abc" * 3 or 3 * "abc"
        if (left is string ls && right is long ri)
        {
            if (ri < 0) throw new RuntimeError("String repeat count must be non-negative.", span);
            return ri == 0 ? "" : string.Concat(Enumerable.Repeat(ls, (int)ri));
        }
        if (left is long li2 && right is string rs)
        {
            if (li2 < 0) throw new RuntimeError("String repeat count must be non-negative.", span);
            return li2 == 0 ? "" : string.Concat(Enumerable.Repeat(rs, (int)li2));
        }
        // Numeric
        if (left is long la && right is long lb) return la * lb;
        if (RuntimeValues.IsNumeric(left) && RuntimeValues.IsNumeric(right))
            return RuntimeValues.ToDouble(left) * RuntimeValues.ToDouble(right);
        // Duration * number
        if (left is StashDuration durMulL && RuntimeValues.IsNumeric(right))
            return durMulL.Multiply(RuntimeValues.ToDouble(right));
        if (RuntimeValues.IsNumeric(left) && right is StashDuration durMulR)
            return durMulR.Multiply(RuntimeValues.ToDouble(left));
        // ByteSize * number
        if (left is StashByteSize bsMulL && RuntimeValues.IsNumeric(right))
            return bsMulL.Multiply(RuntimeValues.ToDouble(right));
        if (RuntimeValues.IsNumeric(left) && right is StashByteSize bsMulR)
            return bsMulR.Multiply(RuntimeValues.ToDouble(left));
        // Errors
        if (left is StashDuration or StashByteSize || right is StashDuration or StashByteSize)
            throw new RuntimeError("Duration and byte size can only be multiplied by a number.", span);
        throw new RuntimeError("Operands must be numbers.", span);
    }

    public static object? Divide(object? left, object? right, SourceSpan? span)
    {
        // Duration / duration or Duration / number
        if (left is StashDuration durDivL)
        {
            if (right is StashDuration durDivR) return durDivL.DivideBy(durDivR);
            if (RuntimeValues.IsNumeric(right)) return durDivL.Divide(RuntimeValues.ToDouble(right));
            throw new RuntimeError("Duration can only be divided by a number or another duration.", span);
        }
        // ByteSize / bytesize or ByteSize / number
        if (left is StashByteSize bsDivL)
        {
            if (right is StashByteSize bsDivR) return bsDivL.DivideBy(bsDivR);
            if (RuntimeValues.IsNumeric(right)) return bsDivL.Divide(RuntimeValues.ToDouble(right));
            throw new RuntimeError("Byte size can only be divided by a number or another byte size.", span);
        }
        // Numeric division
        if (!RuntimeValues.IsNumeric(left) || !RuntimeValues.IsNumeric(right))
            throw new RuntimeError("Operands must be numbers.", span);
        if (left is long la && right is long lb)
        {
            if (lb == 0) throw new RuntimeError("Division by zero.", span);
            return la / lb;
        }
        double dr = RuntimeValues.ToDouble(right);
        if (dr == 0.0) throw new RuntimeError("Division by zero.", span);
        return RuntimeValues.ToDouble(left) / dr;
    }

    public static object? Modulo(object? left, object? right, SourceSpan? span)
    {
        if (!RuntimeValues.IsNumeric(left) || !RuntimeValues.IsNumeric(right))
            throw new RuntimeError("Operands must be numbers.", span);
        if (left is long la && right is long lb)
        {
            if (lb == 0) throw new RuntimeError("Division by zero.", span);
            return la % lb;
        }
        double dr = RuntimeValues.ToDouble(right);
        if (dr == 0.0) throw new RuntimeError("Division by zero.", span);
        return RuntimeValues.ToDouble(left) % dr;
    }

    public static object? Power(object? left, object? right, SourceSpan? span)
    {
        if (left is long ll && right is long rl)
            return (long)Math.Pow(ll, rl);
        double dl = left is long l1 ? l1 : left is double d1 ? d1
            : throw new RuntimeError("Operands must be numbers.", span);
        double dr = right is long l2 ? l2 : right is double d2 ? d2
            : throw new RuntimeError("Operands must be numbers.", span);
        return Math.Pow(dl, dr);
    }

    public static bool Contains(object? left, object? right, SourceSpan? span)
    {
        return right switch
        {
            List<object?> list => list.Any(item => IsEqual(left, item)),
            string str when left is string sub => str.Contains(sub),
            string => throw new RuntimeError(
                "Left operand of 'in' must be a string when checking string containment.", span),
            StashDictionary dict => left is not null && dict.Has(left),
            StashRange range when left is long l => range.Contains(l),
            StashRange range when left is double d && d == Math.Floor(d) => range.Contains((long)d),
            StashRange => throw new RuntimeError(
                "Left operand of 'in' must be an integer when checking range membership.", span),
            StashIpAddress ipNet when left is StashIpAddress ipAddr => ipNet.Contains(ipAddr),
            StashIpAddress => throw new RuntimeError(
                "Left operand of 'in' must be an IP address when checking CIDR containment.", span),
            StashSemVer svRange when left is StashSemVer svVal => svRange.Matches(svVal),
            StashSemVer => throw new RuntimeError(
                "Left operand of 'in' must be a semver when checking version range.", span),
            _ => throw new RuntimeError(
                "Right operand of 'in' must be an array, string, dictionary, range, or semver.", span)
        };
    }

    public static object? Negate(object? value, SourceSpan? span)
    {
        if (value is long l) return -l;
        if (value is double d) return -d;
        throw new RuntimeError("Operand must be a number.", span);
    }

    // --- Bitwise ---

    public static object? BitAnd(object? left, object? right, SourceSpan? span)
    {
        if (left is long la && right is long lb) return la & lb;
        if (left is StashIpAddress ipL && right is StashIpAddress ipR) return ipL.BitwiseAnd(ipR);
        throw new RuntimeError("Operands must be two integers or two IP addresses.", span);
    }

    public static object? BitOr(object? left, object? right, SourceSpan? span)
    {
        if (left is long la && right is long lb) return la | lb;
        if (left is StashIpAddress ipL && right is StashIpAddress ipR) return ipL.BitwiseOr(ipR);
        throw new RuntimeError("Operands must be two integers or two IP addresses.", span);
    }

    public static object? BitXor(object? left, object? right, SourceSpan? span)
    {
        if (left is long la && right is long lb) return la ^ lb;
        throw new RuntimeError("Operands must be integers.", span);
    }

    public static object? BitNot(object? value, SourceSpan? span)
    {
        if (value is long l) return ~l;
        throw new RuntimeError("Operand must be an integer.", span);
    }

    public static object? ShiftLeft(object? left, object? right, SourceSpan? span)
    {
        if (left is long la && right is long lb)
        {
            if (lb < 0 || lb > 63) throw new RuntimeError("Shift count must be in the range 0..63.", span);
            return la << (int)lb;
        }
        throw new RuntimeError("Operands must be integers.", span);
    }

    public static object? ShiftRight(object? left, object? right, SourceSpan? span)
    {
        if (left is long la && right is long lb)
        {
            if (lb < 0 || lb > 63) throw new RuntimeError("Shift count must be in the range 0..63.", span);
            return la >> (int)lb;
        }
        throw new RuntimeError("Operands must be integers.", span);
    }

    // --- Comparison ---
    // Returns a comparison result for ordered comparisons.
    // Supports: long, double (with promotion), IpAddress, Duration, ByteSize, SemVer

    public static bool LessThan(object? left, object? right, SourceSpan? span)
    {
        if (left is long la && right is long lb) return la < lb;
        if (RuntimeValues.IsNumeric(left) && RuntimeValues.IsNumeric(right))
            return RuntimeValues.ToDouble(left) < RuntimeValues.ToDouble(right);
        if (left is StashIpAddress ipL && right is StashIpAddress ipR) return ipL.CompareTo(ipR) < 0;
        if (left is StashDuration durL && right is StashDuration durR) return durL.CompareTo(durR) < 0;
        if (left is StashByteSize bsL && right is StashByteSize bsR) return bsL.CompareTo(bsR) < 0;
        if (left is StashSemVer svL && right is StashSemVer svR) return svL.CompareTo(svR) < 0;
        if (left is StashSemVer || right is StashSemVer)
            throw new RuntimeError("Semver can only be compared to another semver.", span);
        if (left is StashDuration or StashByteSize || right is StashDuration or StashByteSize)
            throw new RuntimeError("Cannot compare duration or byte size with incompatible types.", span);
        throw new RuntimeError("Operands must be two numbers or two IP addresses.", span);
    }

    public static bool LessEqual(object? left, object? right, SourceSpan? span)
    {
        if (left is long la && right is long lb) return la <= lb;
        if (RuntimeValues.IsNumeric(left) && RuntimeValues.IsNumeric(right))
            return RuntimeValues.ToDouble(left) <= RuntimeValues.ToDouble(right);
        if (left is StashIpAddress ipL && right is StashIpAddress ipR) return ipL.CompareTo(ipR) <= 0;
        if (left is StashDuration durL && right is StashDuration durR) return durL.CompareTo(durR) <= 0;
        if (left is StashByteSize bsL && right is StashByteSize bsR) return bsL.CompareTo(bsR) <= 0;
        if (left is StashSemVer svL && right is StashSemVer svR) return svL.CompareTo(svR) <= 0;
        if (left is StashSemVer || right is StashSemVer)
            throw new RuntimeError("Semver can only be compared to another semver.", span);
        if (left is StashDuration or StashByteSize || right is StashDuration or StashByteSize)
            throw new RuntimeError("Cannot compare duration or byte size with incompatible types.", span);
        throw new RuntimeError("Operands must be two numbers or two IP addresses.", span);
    }

    public static bool GreaterThan(object? left, object? right, SourceSpan? span)
    {
        if (left is long la && right is long lb) return la > lb;
        if (RuntimeValues.IsNumeric(left) && RuntimeValues.IsNumeric(right))
            return RuntimeValues.ToDouble(left) > RuntimeValues.ToDouble(right);
        if (left is StashIpAddress ipL && right is StashIpAddress ipR) return ipL.CompareTo(ipR) > 0;
        if (left is StashDuration durL && right is StashDuration durR) return durL.CompareTo(durR) > 0;
        if (left is StashByteSize bsL && right is StashByteSize bsR) return bsL.CompareTo(bsR) > 0;
        if (left is StashSemVer svL && right is StashSemVer svR) return svL.CompareTo(svR) > 0;
        if (left is StashSemVer || right is StashSemVer)
            throw new RuntimeError("Semver can only be compared to another semver.", span);
        if (left is StashDuration or StashByteSize || right is StashDuration or StashByteSize)
            throw new RuntimeError("Cannot compare duration or byte size with incompatible types.", span);
        throw new RuntimeError("Operands must be two numbers or two IP addresses.", span);
    }

    public static bool GreaterEqual(object? left, object? right, SourceSpan? span)
    {
        if (left is long la && right is long lb) return la >= lb;
        if (RuntimeValues.IsNumeric(left) && RuntimeValues.IsNumeric(right))
            return RuntimeValues.ToDouble(left) >= RuntimeValues.ToDouble(right);
        if (left is StashIpAddress ipL && right is StashIpAddress ipR) return ipL.CompareTo(ipR) >= 0;
        if (left is StashDuration durL && right is StashDuration durR) return durL.CompareTo(durR) >= 0;
        if (left is StashByteSize bsL && right is StashByteSize bsR) return bsL.CompareTo(bsR) >= 0;
        if (left is StashSemVer svL && right is StashSemVer svR) return svL.CompareTo(svR) >= 0;
        if (left is StashSemVer || right is StashSemVer)
            throw new RuntimeError("Semver can only be compared to another semver.", span);
        if (left is StashDuration or StashByteSize || right is StashDuration or StashByteSize)
            throw new RuntimeError("Cannot compare duration or byte size with incompatible types.", span);
        throw new RuntimeError("Operands must be two numbers or two IP addresses.", span);
    }

    // --- String Interpolation ---

    public static string Interpolate(object?[] stack, int sp, int count)
    {
        var sb = new StringBuilder();
        int start = sp - count;
        for (int i = start; i < sp; i++)
            sb.Append(RuntimeValues.Stringify(stack[i]));
        return sb.ToString();
    }
}
