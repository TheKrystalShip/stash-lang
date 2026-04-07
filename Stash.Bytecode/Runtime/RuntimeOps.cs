using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFalsy(StashValue value) => value.Tag switch
    {
        StashValueTag.Null => true,
        StashValueTag.Bool => !value.AsBool,
        StashValueTag.Int => value.AsInt == 0,
        StashValueTag.Float => value.AsFloat == 0.0,
        StashValueTag.Obj => value.AsObj switch
        {
            null => true,
            string s => s.Length == 0,
            StashError => true,
            _ => false,
        },
        _ => true,
    };

    // --- Equality ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEqual(StashValue left, StashValue right)
    {
        if (left.Tag != right.Tag) return false;
        return left.Tag switch
        {
            StashValueTag.Null => true,
            StashValueTag.Bool => left.AsBool == right.AsBool,
            StashValueTag.Int => left.AsInt == right.AsInt,
            StashValueTag.Float => left.AsFloat == right.AsFloat,
            StashValueTag.Obj => RuntimeValues.IsEqual(left.AsObj, right.AsObj),
            _ => false,
        };
    }

    // --- Stringify ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Stringify(StashValue value) => value.Tag switch
    {
        StashValueTag.Null => "null",
        StashValueTag.Bool => value.AsBool ? "true" : "false",
        StashValueTag.Int => value.AsInt.ToString(),
        StashValueTag.Float => value.AsFloat.ToString(System.Globalization.CultureInfo.InvariantCulture),
        StashValueTag.Obj => value.AsObj is string s ? s : RuntimeValues.Stringify(value.AsObj),
        _ => "null",
    };

    // --- Arithmetic ---

    public static StashValue Add(StashValue left, StashValue right, SourceSpan? span)
    {
        // Fast paths first
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt(left.AsInt + right.AsInt);
        }

        if (left.IsNumeric && right.IsNumeric)
        {
            return StashValue.FromFloat(ToDouble(left) + ToDouble(right));
        }
        // String concatenation — if either side is a string
        object? lObj = left.IsObj ? left.AsObj : null;
        object? rObj = right.IsObj ? right.AsObj : null;
        if (lObj is string ls && rObj is string rs)
        {
            return StashValue.FromObj(string.Concat(ls, rs));
        }
        if (lObj is string ls2)
        {
            return StashValue.FromObj(string.Concat(ls2, Stringify(right)));
        }
        if (rObj is string rs2)
        {
            return StashValue.FromObj(string.Concat(Stringify(left), rs2));
        }
        // IP address + offset
        if (lObj is StashIpAddress ipL && right.IsInt)
        {
            return StashValue.FromObj(ipL.Add(right.AsInt));
        }

        if (left.IsInt && rObj is StashIpAddress ipR)
        {
            return StashValue.FromObj(ipR.Add(left.AsInt));
        }
        // Duration + Duration
        if (lObj is StashDuration durL && rObj is StashDuration durR)
        {
            return StashValue.FromObj(durL.Add(durR));
        }
        // ByteSize + ByteSize
        if (lObj is StashByteSize bsL && rObj is StashByteSize bsR)
        {
            return StashValue.FromObj(bsL.Add(bsR));
        }
        // Type mismatch errors
        if (lObj is StashDuration or StashByteSize || rObj is StashDuration or StashByteSize)
        {
            throw new RuntimeError("Cannot mix duration or byte size with other types in addition.", span);
        }

        throw new RuntimeError("Operands must be numbers or strings.", span);
    }

    public static StashValue Subtract(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt(left.AsInt - right.AsInt);
        }

        if (left.IsNumeric && right.IsNumeric)
        {
            return StashValue.FromFloat(ToDouble(left) - ToDouble(right));
        }

        object? lObj = left.IsObj ? left.AsObj : null;
        object? rObj = right.IsObj ? right.AsObj : null;
        if (lObj is StashIpAddress ipSubL && rObj is StashIpAddress ipSubR)
        {
            return StashValue.FromObj(ipSubL.Subtract(ipSubR));
        }

        if (lObj is StashIpAddress ipSubA && right.IsInt)
        {
            return StashValue.FromObj(ipSubA.Add(-right.AsInt));
        }

        if (lObj is StashDuration durSubL && rObj is StashDuration durSubR)
        {
            return StashValue.FromObj(durSubL.Subtract(durSubR));
        }

        if (lObj is StashByteSize bsSubL && rObj is StashByteSize bsSubR)
        {
            return StashValue.FromObj(bsSubL.Subtract(bsSubR));
        }

        if (lObj is StashDuration or StashByteSize || rObj is StashDuration or StashByteSize)
        {
            throw new RuntimeError("Cannot mix duration or byte size with other types in subtraction.", span);
        }

        throw new RuntimeError("Operands must be two numbers or two IP addresses.", span);
    }

    public static StashValue Multiply(StashValue left, StashValue right, SourceSpan? span)
    {
        object? lObj = left.IsObj ? left.AsObj : null;
        object? rObj = right.IsObj ? right.AsObj : null;
        // String repeat: "abc" * 3 or 3 * "abc"
        if (lObj is string ls && right.IsInt)
        {
            if (right.AsInt < 0)
            {
                throw new RuntimeError("String repeat count must be non-negative.", span);
            }

            return StashValue.FromObj(RepeatString(ls, (int)right.AsInt));
        }
        if (left.IsInt && rObj is string rs)
        {
            if (left.AsInt < 0)
            {
                throw new RuntimeError("String repeat count must be non-negative.", span);
            }

            return StashValue.FromObj(RepeatString(rs, (int)left.AsInt));
        }
        // Numeric
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt(left.AsInt * right.AsInt);
        }

        if (left.IsNumeric && right.IsNumeric)
        {
            return StashValue.FromFloat(ToDouble(left) * ToDouble(right));
        }
        // Duration * number
        if (lObj is StashDuration durMulL && right.IsNumeric)
        {
            return StashValue.FromObj(durMulL.Multiply(ToDouble(right)));
        }

        if (left.IsNumeric && rObj is StashDuration durMulR)
        {
            return StashValue.FromObj(durMulR.Multiply(ToDouble(left)));
        }
        // ByteSize * number
        if (lObj is StashByteSize bsMulL && right.IsNumeric)
        {
            return StashValue.FromObj(bsMulL.Multiply(ToDouble(right)));
        }

        if (left.IsNumeric && rObj is StashByteSize bsMulR)
        {
            return StashValue.FromObj(bsMulR.Multiply(ToDouble(left)));
        }
        // Errors
        if (lObj is StashDuration or StashByteSize || rObj is StashDuration or StashByteSize)
        {
            throw new RuntimeError("Duration and byte size can only be multiplied by a number.", span);
        }

        throw new RuntimeError("Operands must be numbers.", span);
    }

    public static StashValue Divide(StashValue left, StashValue right, SourceSpan? span)
    {
        object? lObj = left.IsObj ? left.AsObj : null;
        object? rObj = right.IsObj ? right.AsObj : null;
        // Duration / duration or Duration / number
        if (lObj is StashDuration durDivL)
        {
            if (rObj is StashDuration durDivR)
            {
                return StashValue.FromFloat(durDivL.DivideBy(durDivR));
            }

            if (right.IsNumeric)
            {
                return StashValue.FromObj(durDivL.Divide(ToDouble(right)));
            }

            throw new RuntimeError("Duration can only be divided by a number or another duration.", span);
        }
        // ByteSize / bytesize or ByteSize / number
        if (lObj is StashByteSize bsDivL)
        {
            if (rObj is StashByteSize bsDivR)
            {
                return StashValue.FromFloat(bsDivL.DivideBy(bsDivR));
            }

            if (right.IsNumeric)
            {
                return StashValue.FromObj(bsDivL.Divide(ToDouble(right)));
            }

            throw new RuntimeError("Byte size can only be divided by a number or another byte size.", span);
        }
        // Numeric division
        if (!left.IsNumeric || !right.IsNumeric)
        {
            throw new RuntimeError("Operands must be numbers.", span);
        }

        if (left.IsInt && right.IsInt)
        {
            if (right.AsInt == 0)
            {
                throw new RuntimeError("Division by zero.", span);
            }

            return StashValue.FromInt(left.AsInt / right.AsInt);
        }
        double dr = ToDouble(right);
        if (dr == 0.0)
        {
            throw new RuntimeError("Division by zero.", span);
        }

        return StashValue.FromFloat(ToDouble(left) / dr);
    }

    public static StashValue Modulo(StashValue left, StashValue right, SourceSpan? span)
    {
        if (!left.IsNumeric || !right.IsNumeric)
        {
            throw new RuntimeError("Operands must be numbers.", span);
        }

        if (left.IsInt && right.IsInt)
        {
            if (right.AsInt == 0)
            {
                throw new RuntimeError("Division by zero.", span);
            }

            return StashValue.FromInt(left.AsInt % right.AsInt);
        }
        double dr = ToDouble(right);
        if (dr == 0.0)
        {
            throw new RuntimeError("Division by zero.", span);
        }

        return StashValue.FromFloat(ToDouble(left) % dr);
    }

    public static StashValue Power(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt((long)Math.Pow(left.AsInt, right.AsInt));
        }

        if (!left.IsNumeric)
        {
            throw new RuntimeError("Operands must be numbers.", span);
        }

        if (!right.IsNumeric)
        {
            throw new RuntimeError("Operands must be numbers.", span);
        }

        return StashValue.FromFloat(Math.Pow(ToDouble(left), ToDouble(right)));
    }

    public static bool Contains(StashValue left, StashValue right, SourceSpan? span)
    {
        object? rObj = right.IsObj ? right.AsObj : null;
        return rObj switch
        {
            List<object?> list => list.Any(item => RuntimeValues.IsEqual(left.ToObject(), item)),
            string str when left.AsObj is string sub => str.Contains(sub),
            string => throw new RuntimeError(
                "Left operand of 'in' must be a string when checking string containment.", span),
            StashDictionary dict => !left.IsNull && dict.Has(left.ToObject()!),
            StashRange range when left.IsInt => range.Contains(left.AsInt),
            StashRange range when left.IsFloat && left.AsFloat == Math.Floor(left.AsFloat) => range.Contains((long)left.AsFloat),
            StashRange => throw new RuntimeError(
                "Left operand of 'in' must be an integer when checking range membership.", span),
            StashIpAddress ipNet when left.AsObj is StashIpAddress ipAddr => ipNet.Contains(ipAddr),
            StashIpAddress => throw new RuntimeError(
                "Left operand of 'in' must be an IP address when checking CIDR containment.", span),
            StashSemVer svRange when left.AsObj is StashSemVer svVal => svRange.Matches(svVal),
            StashSemVer => throw new RuntimeError(
                "Left operand of 'in' must be a semver when checking version range.", span),
            _ => throw new RuntimeError(
                "Right operand of 'in' must be an array, string, dictionary, range, or semver.", span)
        };
    }

    public static StashValue Negate(StashValue value, SourceSpan? span)
    {
        if (value.IsInt)
        {
            return StashValue.FromInt(-value.AsInt);
        }

        if (value.IsFloat)
        {
            return StashValue.FromFloat(-value.AsFloat);
        }

        if (value.IsObj && value.AsObj is StashDuration dur)
        {
            return StashValue.FromObject(dur.Negate());
        }

        if (value.IsObj && value.AsObj is StashByteSize bs)
        {
            return StashValue.FromObject(new StashByteSize(-bs.TotalBytes));
        }

        throw new RuntimeError("Operand must be a number.", span);
    }

    // --- Bitwise ---

    public static StashValue BitAnd(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt(left.AsInt & right.AsInt);
        }

        if (left.AsObj is StashIpAddress ipL && right.AsObj is StashIpAddress ipR)
        {
            return StashValue.FromObj(ipL.BitwiseAnd(ipR));
        }

        throw new RuntimeError("Operands must be two integers or two IP addresses.", span);
    }

    public static StashValue BitOr(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt(left.AsInt | right.AsInt);
        }

        if (left.AsObj is StashIpAddress ipL && right.AsObj is StashIpAddress ipR)
        {
            return StashValue.FromObj(ipL.BitwiseOr(ipR));
        }

        throw new RuntimeError("Operands must be two integers or two IP addresses.", span);
    }

    public static StashValue BitXor(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt(left.AsInt ^ right.AsInt);
        }

        throw new RuntimeError("Operands must be integers.", span);
    }

    public static StashValue BitNot(StashValue value, SourceSpan? span)
    {
        if (value.IsInt)
        {
            return StashValue.FromInt(~value.AsInt);
        }

        if (value.IsObj && value.AsObj is StashIpAddress ip)
        {
            return StashValue.FromObject(ip.BitwiseNot());
        }

        throw new RuntimeError("Operand must be an integer.", span);
    }

    public static StashValue ShiftLeft(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsInt && right.IsInt)
        {
            if (right.AsInt < 0 || right.AsInt > 63)
            {
                throw new RuntimeError("Shift count must be in the range 0..63.", span);
            }

            return StashValue.FromInt(left.AsInt << (int)right.AsInt);
        }
        throw new RuntimeError("Operands must be integers.", span);
    }

    public static StashValue ShiftRight(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsInt && right.IsInt)
        {
            if (right.AsInt < 0 || right.AsInt > 63)
            {
                throw new RuntimeError("Shift count must be in the range 0..63.", span);
            }

            return StashValue.FromInt(left.AsInt >> (int)right.AsInt);
        }
        throw new RuntimeError("Operands must be integers.", span);
    }

    // --- Comparison ---
    // Supports: long, double (with promotion), IpAddress, Duration, ByteSize, SemVer

    public static bool LessThan(StashValue left, StashValue right, SourceSpan? span)
        => Compare(left, right, span) < 0;

    public static bool LessEqual(StashValue left, StashValue right, SourceSpan? span)
        => Compare(left, right, span) <= 0;

    public static bool GreaterThan(StashValue left, StashValue right, SourceSpan? span)
        => Compare(left, right, span) > 0;

    public static bool GreaterEqual(StashValue left, StashValue right, SourceSpan? span)
        => Compare(left, right, span) >= 0;

    private static int Compare(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsInt && right.IsInt)
        {
            return left.AsInt.CompareTo(right.AsInt);
        }

        if (left.IsNumeric && right.IsNumeric)
        {
            return ToDouble(left).CompareTo(ToDouble(right));
        }

        object? lObj = left.IsObj ? left.AsObj : null;
        object? rObj = right.IsObj ? right.AsObj : null;

        if (lObj is StashIpAddress ipL && rObj is StashIpAddress ipR)
        {
            return ipL.CompareTo(ipR);
        }

        if (lObj is StashDuration durL && rObj is StashDuration durR)
        {
            return durL.CompareTo(durR);
        }

        if (lObj is StashByteSize bsL && rObj is StashByteSize bsR)
        {
            return bsL.CompareTo(bsR);
        }

        if (lObj is StashSemVer svL && rObj is StashSemVer svR)
        {
            return svL.CompareTo(svR);
        }

        if (lObj is StashSemVer || rObj is StashSemVer)
        {
            throw new RuntimeError("Semver can only be compared to another semver.", span);
        }

        if (lObj is StashDuration or StashByteSize || rObj is StashDuration or StashByteSize)
        {
            throw new RuntimeError("Cannot compare duration or byte size with incompatible types.", span);
        }

        throw new RuntimeError("Operands must be two numbers or two IP addresses.", span);
    }

    // --- String Interpolation ---

    public static string Interpolate(StashValue[] stack, int sp, int count)
    {
        if (count == 1)
        {
            return RuntimeValues.Stringify(stack[sp - 1].ToObject());
        }

        Span<char> stackBuf = stackalloc char[256];
        var vsb = new ValueStringBuilder(stackBuf);

        int start = sp - count;
        for (int i = start; i < sp; i++)
        {
            vsb.Append(RuntimeValues.Stringify(stack[i].ToObject()));
        }

        string result = vsb.ToString();
        vsb.Dispose();
        return result;
    }

    // --- String Repeat ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string RepeatString(string s, int count)
    {
        if (count == 0 || s.Length == 0) return count == 0 ? "" : s;
        if (count == 1) return s;

        return string.Create(checked(s.Length * count), (s, count), static (span, state) =>
        {
            ReadOnlySpan<char> src = state.s;
            for (int i = 0; i < state.count; i++)
            {
                src.CopyTo(span[(i * src.Length)..]);
            }
        });
    }

    // --- Helpers ---

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double ToDouble(StashValue v)
    {
        return v.Tag switch
        {
            StashValueTag.Int => (double)v.AsInt,
            StashValueTag.Float => v.AsFloat,
            _ => throw new InvalidOperationException("Not a number"),
        };
    }
}
