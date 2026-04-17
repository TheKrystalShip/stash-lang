using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;
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
    public static bool IsFalsy(StashValue value)
    {
        StashValueTag tag = value.Tag;
        if (tag == StashValueTag.Bool) return !value.AsBool;
        if (tag == StashValueTag.Int) return value.AsInt == 0;
        if (tag == StashValueTag.Null) return true;
        return IsFalsySlow(value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsFalsySlow(StashValue value) => value.Tag switch
    {
        StashValueTag.Byte => value.AsByte == 0,
        StashValueTag.Float => value.AsFloat == 0.0,
        StashValueTag.Obj => value.AsObj switch
        {
            null => true,
            string s => s.Length == 0,
            IVMTruthiness t => t.VMIsFalsy,
            _ => false,
        },
        _ => true,
    };

    // --- Equality ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEqual(StashValue left, StashValue right)
    {
        if (left.Tag != right.Tag) return false;
        if (left.Tag == StashValueTag.Int) return left.AsInt == right.AsInt;
        if (left.Tag == StashValueTag.Bool) return left.AsBool == right.AsBool;
        return IsEqualSlow(left, right);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsEqualSlow(StashValue left, StashValue right) => left.Tag switch
    {
        StashValueTag.Null => true,
        StashValueTag.Byte => left.AsInt == right.AsInt,
        StashValueTag.Float => left.AsFloat == right.AsFloat,
        StashValueTag.Obj => left.AsObj is IVMEquatable eq
            ? eq.VMEquals(right)
            : RuntimeValues.IsEqual(left.AsObj, right.AsObj),
        _ => false,
    };

    // --- Stringify ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Stringify(StashValue value) => value.Tag switch
    {
        StashValueTag.Null => "null",
        StashValueTag.Bool => value.AsBool ? "true" : "false",
        StashValueTag.Int => value.AsInt.ToString(),
        StashValueTag.Byte => value.AsByte.ToString(),
        StashValueTag.Float => value.AsFloat.ToString(System.Globalization.CultureInfo.InvariantCulture),
        StashValueTag.Obj => value.AsObj is string s ? s : StringifyObj(value.AsObj),
        _ => "null",
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string StringifyObj(object? obj) => obj switch
    {
        IVMStringifiable sf => sf.VMToString(),
        { } o => RuntimeValues.Stringify(o),
        _ => "null",
    };

    // --- Arithmetic ---

    public static StashValue Add(StashValue left, StashValue right, SourceSpan? span)
    {
        // Byte → Int promotion for arithmetic
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
        // Fast paths first
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt(left.AsInt + right.AsInt);
        }

        if (left.IsNumeric && right.IsNumeric)
        {
            return StashValue.FromFloat(ToDouble(left) + ToDouble(right));
        }
        object? lObj = left.IsObj ? left.AsObj : null;
        object? rObj = right.IsObj ? right.AsObj : null;

        // Protocol dispatch — try left operand first, then right (reverse dispatch)
        if (lObj is IVMArithmetic leftArith)
        {
            if (leftArith.VMTryArithmetic(ArithmeticOp.Add, right, true, out StashValue result, span))
                return result;
        }
        if (rObj is IVMArithmetic rightArith)
        {
            if (rightArith.VMTryArithmetic(ArithmeticOp.Add, left, false, out StashValue result, span))
                return result;
        }

        // String concatenation — if either side is a string (strings can't implement protocols)
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

        throw new RuntimeError("Operands must be numbers or strings.", span);
    }

    public static StashValue Subtract(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt(left.AsInt - right.AsInt);
        }

        if (left.IsNumeric && right.IsNumeric)
        {
            return StashValue.FromFloat(ToDouble(left) - ToDouble(right));
        }

        // Protocol dispatch
        object? lObj = left.IsObj ? left.AsObj : null;
        object? rObj = right.IsObj ? right.AsObj : null;

        if (lObj is IVMArithmetic leftArith)
        {
            if (leftArith.VMTryArithmetic(ArithmeticOp.Subtract, right, true, out StashValue result, span))
                return result;
        }
        if (rObj is IVMArithmetic rightArith)
        {
            if (rightArith.VMTryArithmetic(ArithmeticOp.Subtract, left, false, out StashValue result, span))
                return result;
        }

        throw new RuntimeError("Operands must be two numbers or two IP addresses.", span);
    }

    public static StashValue Multiply(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
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
        // Protocol dispatch
        if (lObj is IVMArithmetic leftArith)
        {
            if (leftArith.VMTryArithmetic(ArithmeticOp.Multiply, right, true, out StashValue result, span))
                return result;
        }
        if (rObj is IVMArithmetic rightArith)
        {
            if (rightArith.VMTryArithmetic(ArithmeticOp.Multiply, left, false, out StashValue result, span))
                return result;
        }

        throw new RuntimeError("Operands must be numbers.", span);
    }

    public static StashValue Divide(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
        // Protocol dispatch first (Duration/ByteSize division has special semantics)
        object? lObj = left.IsObj ? left.AsObj : null;
        object? rObj = right.IsObj ? right.AsObj : null;

        if (lObj is IVMArithmetic leftArith)
        {
            if (leftArith.VMTryArithmetic(ArithmeticOp.Divide, right, true, out StashValue result, span))
                return result;
        }
        if (rObj is IVMArithmetic rightArith)
        {
            if (rightArith.VMTryArithmetic(ArithmeticOp.Divide, left, false, out StashValue result, span))
                return result;
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
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);

        if (left.IsInt && right.IsInt)
        {
            if (right.AsInt == 0)
            {
                throw new RuntimeError("Division by zero.", span);
            }

            return StashValue.FromInt(left.AsInt % right.AsInt);
        }
        if (left.IsNumeric && right.IsNumeric)
        {
            double dr = ToDouble(right);
            if (dr == 0.0)
            {
                throw new RuntimeError("Division by zero.", span);
            }

            return StashValue.FromFloat(ToDouble(left) % dr);
        }

        return ArithmeticProtocolFallback(left, right, ArithmeticOp.Modulo, "Operands must be numbers.", span);
    }

    public static StashValue Power(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt((long)Math.Pow(left.AsInt, right.AsInt));
        }
        if (left.IsNumeric && right.IsNumeric)
        {
            return StashValue.FromFloat(Math.Pow(ToDouble(left), ToDouble(right)));
        }

        return ArithmeticProtocolFallback(left, right, ArithmeticOp.Power, "Operands must be numbers.", span);
    }

    public static bool Contains(StashValue left, StashValue right, SourceSpan? span)
    {
        object? rObj = right.IsObj ? right.AsObj : null;
        return rObj switch
        {
            List<StashValue> svList => svList.Any(sv => sv.Equals(left)),
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
        if (value.IsByte) return StashValue.FromInt(-value.AsByte);
        if (value.IsInt)
        {
            return StashValue.FromInt(-value.AsInt);
        }

        if (value.IsFloat)
        {
            return StashValue.FromFloat(-value.AsFloat);
        }

        if (value.IsObj && value.AsObj is IVMArithmetic arith)
        {
            if (arith.VMTryArithmetic(ArithmeticOp.Negate, StashValue.Null, true, out StashValue result, span))
                return result;
        }

        throw new RuntimeError("Operand must be a number.", span);
    }

    // --- Bitwise ---

    public static StashValue BitAnd(StashValue left, StashValue right, SourceSpan? span)
    {
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
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
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
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
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
        if (left.IsInt && right.IsInt)
        {
            return StashValue.FromInt(left.AsInt ^ right.AsInt);
        }

        throw new RuntimeError("Operands must be integers.", span);
    }

    public static StashValue BitNot(StashValue value, SourceSpan? span)
    {
        if (value.IsByte) value = StashValue.FromInt(value.AsByte);
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
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
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
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
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
        if (left.IsByte) left = StashValue.FromInt(left.AsByte);
        if (right.IsByte) right = StashValue.FromInt(right.AsByte);
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

        if (lObj is IVMComparable leftCmp)
        {
            if (leftCmp.VMTryCompare(right, out int result, span))
                return result;
        }
        if (rObj is IVMComparable rightCmp)
        {
            if (rightCmp.VMTryCompare(left, out int result, span))
                return -result; // Reverse the comparison result
        }

        throw new RuntimeError("Operands must be two numbers or two comparable values.", span);
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StashValue ArithmeticProtocolFallback(
        StashValue left, StashValue right, ArithmeticOp op, string errorMessage, SourceSpan? span)
    {
        object? lObj = left.IsObj ? left.AsObj : null;
        object? rObj = right.IsObj ? right.AsObj : null;

        if (lObj is IVMArithmetic leftArith)
        {
            if (leftArith.VMTryArithmetic(op, right, true, out StashValue result, span))
                return result;
        }
        if (rObj is IVMArithmetic rightArith)
        {
            if (rightArith.VMTryArithmetic(op, left, false, out StashValue result, span))
                return result;
        }

        throw new RuntimeError(errorMessage, span);
    }

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
