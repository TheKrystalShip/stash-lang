namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the 'assert' namespace.
/// </summary>
[StashNamespace]
public static partial class AssertBuiltIns
{
    /// <summary>Asserts that actual equals expected using strict equality (no type coercion). Throws AssertionError if not.</summary>
    /// <param name="actual">The actual value</param>
    /// <param name="expected">The expected value</param>
    /// <returns>null</returns>
    [StashFn]
    private static void Equal(IInterpreterContext ctx, StashValue actual, StashValue expected)
    {
        object? actualObj = actual.ToObject();
        object? expectedObj = expected.ToObject();
        if (!RuntimeValues.IsEqual(actualObj, expectedObj))
        {
            string msg = $"assert.equal failed: expected {RuntimeValues.Stringify(expectedObj)} but got {RuntimeValues.Stringify(actualObj)}";
            throw new AssertionError(msg, expectedObj, actualObj, ctx.CurrentSpan);
        }
    }

    /// <summary>Asserts that actual does not equal expected. Throws AssertionError if they are equal.</summary>
    /// <param name="actual">The actual value</param>
    /// <param name="expected">The value to compare against</param>
    /// <returns>null</returns>
    [StashFn]
    private static void NotEqual(IInterpreterContext ctx, StashValue actual, StashValue expected)
    {
        object? actualObj = actual.ToObject();
        object? expectedObj = expected.ToObject();
        if (RuntimeValues.IsEqual(actualObj, expectedObj))
        {
            string msg = $"assert.notEqual failed: expected values to differ but both are {RuntimeValues.Stringify(actualObj)}";
            throw new AssertionError(msg, expectedObj, actualObj, ctx.CurrentSpan);
        }
    }

    /// <summary>Asserts that the value is truthy. Throws AssertionError if falsy.</summary>
    /// <param name="value">The value to check</param>
    /// <returns>null</returns>
    [StashFn]
    private static void True(IInterpreterContext ctx, StashValue value)
    {
        object? val = value.ToObject();
        if (!RuntimeValues.IsTruthy(val))
        {
            string msg = $"assert.true failed: expected truthy value but got {RuntimeValues.Stringify(val)}";
            throw new AssertionError(msg, true, val, ctx.CurrentSpan);
        }
    }

    /// <summary>Asserts that the value is falsy. Throws AssertionError if truthy.</summary>
    /// <param name="value">The value to check</param>
    /// <returns>null</returns>
    [StashFn]
    private static void False(IInterpreterContext ctx, StashValue value)
    {
        object? val = value.ToObject();
        if (RuntimeValues.IsTruthy(val))
        {
            string msg = $"assert.false failed: expected falsy value but got {RuntimeValues.Stringify(val)}";
            throw new AssertionError(msg, false, val, ctx.CurrentSpan);
        }
    }

    /// <summary>Asserts that the value is null. Throws AssertionError if not null.</summary>
    /// <param name="value">The value to check</param>
    /// <returns>null</returns>
    [StashFn(Name = "null")]
    private static void IsNull(IInterpreterContext ctx, StashValue value)
    {
        if (!value.IsNull)
        {
            object? val = value.ToObject();
            string msg = $"assert.null failed: expected null but got {RuntimeValues.Stringify(val)}";
            throw new AssertionError(msg, null, val, ctx.CurrentSpan);
        }
    }

    /// <summary>Asserts that the value is not null. Throws AssertionError if null.</summary>
    /// <param name="value">The value to check</param>
    /// <returns>null</returns>
    [StashFn]
    private static void NotNull(IInterpreterContext ctx, StashValue value)
    {
        if (value.IsNull)
        {
            string msg = "assert.notNull failed: expected non-null value but got null";
            throw new AssertionError(msg, "non-null", null, ctx.CurrentSpan);
        }
    }

    /// <summary>Asserts that a is greater than b. Throws AssertionError if not.</summary>
    /// <param name="a">The left-hand value</param>
    /// <param name="b">The right-hand value</param>
    /// <returns>null</returns>
    [StashFn]
    private static void Greater(IInterpreterContext ctx, StashValue a, StashValue b)
    {
        double aNum = ToNumeric(a, "a", "assert.greater");
        double bNum = ToNumeric(b, "b", "assert.greater");
        if (!(aNum > bNum))
        {
            object? aObj = a.ToObject();
            object? bObj = b.ToObject();
            string msg = $"assert.greater failed: expected {RuntimeValues.Stringify(aObj)} > {RuntimeValues.Stringify(bObj)}";
            throw new AssertionError(msg, $"> {RuntimeValues.Stringify(bObj)}", aObj, ctx.CurrentSpan);
        }
    }

    /// <summary>Asserts that a is less than b. Throws AssertionError if not.</summary>
    /// <param name="a">The left-hand value</param>
    /// <param name="b">The right-hand value</param>
    /// <returns>null</returns>
    [StashFn]
    private static void Less(IInterpreterContext ctx, StashValue a, StashValue b)
    {
        double aNum = ToNumeric(a, "a", "assert.less");
        double bNum = ToNumeric(b, "b", "assert.less");
        if (!(aNum < bNum))
        {
            object? aObj = a.ToObject();
            object? bObj = b.ToObject();
            string msg = $"assert.less failed: expected {RuntimeValues.Stringify(aObj)} < {RuntimeValues.Stringify(bObj)}";
            throw new AssertionError(msg, $"< {RuntimeValues.Stringify(bObj)}", aObj, ctx.CurrentSpan);
        }
    }

    /// <summary>Asserts that fn throws an error when called. Returns the error message if it throws.</summary>
    /// <param name="fn">The function to invoke</param>
    /// <returns>The error message thrown by fn</returns>
    [StashFn(ReturnType = "string")]
    private static string Throws(IInterpreterContext ctx, IStashCallable fn)
    {
        try
        {
            ctx.InvokeCallbackDirect(fn, ReadOnlySpan<StashValue>.Empty);
        }
        catch (RuntimeError ex)
        {
            return ex.Message;
        }
        string msg = "assert.throws failed: expected function to throw but it did not";
        throw new AssertionError(msg, "error", "no error", ctx.CurrentSpan);
    }

    /// <summary>Immediately fails the test with an optional message.</summary>
    /// <param name="message">Optional failure message</param>
    /// <returns>never</returns>
    // Raw = true: 'message' is optional and may be any type; typed form can't express "optional any" cleanly.
    [StashFn(Raw = true, ReturnType = "never")]
    private static StashValue Fail(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        string msg = args[0].ToObject() is string s ? s : "assert.fail called";
        throw new AssertionError(msg, null, null, ctx.CurrentSpan);
    }

    /// <summary>Asserts structural equality between actual and expected by recursively comparing their contents. Works for primitives, arrays, dictionaries, and struct instances.</summary>
    /// <param name="actual">The actual value</param>
    /// <param name="expected">The expected value</param>
    /// <returns>null</returns>
    [StashFn]
    private static void DeepEqual(IInterpreterContext ctx, StashValue actual, StashValue expected)
    {
        var (equal, failPath, expectedStr, actualStr) = DeepEquals(actual, expected, "");

        if (!equal)
        {
            string pathInfo = failPath.Length > 0 ? $"\n  at: {failPath}" : "";
            string msg = $"assert.deepEqual failed{pathInfo}\n  Expected: {expectedStr}\n  Actual:   {actualStr}";
            throw new AssertionError(msg, expected.ToObject(), actual.ToObject(), ctx.CurrentSpan);
        }
    }

    /// <summary>Asserts that actual is within delta of expected. Throws AssertionError if the absolute difference exceeds delta.</summary>
    /// <param name="actual">The actual value</param>
    /// <param name="expected">The expected value</param>
    /// <param name="delta">The maximum allowed difference (must be non-negative)</param>
    /// <returns>null</returns>
    [StashFn]
    private static void CloseTo(IInterpreterContext ctx, StashValue actual, StashValue expected, StashValue delta)
    {
        double actualNum = ToNumeric(actual, "actual", "assert.closeTo");
        double expectedNum = ToNumeric(expected, "expected", "assert.closeTo");
        double deltaNum = ToNumeric(delta, "delta", "assert.closeTo");

        if (deltaNum < 0)
            throw new RuntimeError("assert.closeTo: delta must be non-negative.");

        double diff = Math.Abs(actualNum - expectedNum);
        if (diff > deltaNum)
        {
            string msg = $"assert.closeTo failed: expected {actualNum} to be within {deltaNum} of {expectedNum} (difference: {diff})";
            throw new AssertionError(msg, expectedNum, actualNum, ctx.CurrentSpan);
        }
    }

    private static double ToNumeric(StashValue v, string paramName, string funcName)
    {
        if (v.IsFloat) return v.AsFloat;
        if (v.IsInt) return (double)v.AsInt;
        throw new RuntimeError($"Argument '{paramName}' to '{funcName}' must be a number.", errorType: StashErrorTypes.TypeError);
    }

    private static (bool Equal, string FailPath, string? ExpStr, string? ActStr)
        DeepEquals(StashValue actual, StashValue expected, string path)
    {
        // Reference identity short-circuit
        if (actual.IsObj && expected.IsObj && actual.AsObj != null && ReferenceEquals(actual.AsObj, expected.AsObj))
            return (true, "", null, null);

        // Both null
        if (actual.IsNull && expected.IsNull)
            return (true, "", null, null);

        // Primitive comparisons (same tag)
        if (actual.IsInt && expected.IsInt)
        {
            bool eq = actual.AsInt == expected.AsInt;
            return (eq, path, eq ? null : expected.AsInt.ToString(), eq ? null : actual.AsInt.ToString());
        }
        if (actual.IsFloat && expected.IsFloat)
        {
            bool eq = actual.AsFloat == expected.AsFloat;
            return (eq, path,
                eq ? null : expected.AsFloat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                eq ? null : actual.AsFloat.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (actual.IsBool && expected.IsBool)
        {
            bool eq = actual.AsBool == expected.AsBool;
            return (eq, path,
                eq ? null : (expected.AsBool ? "true" : "false"),
                eq ? null : (actual.AsBool ? "true" : "false"));
        }
        if (actual.IsByte && expected.IsByte)
        {
            bool eq = actual.AsByte == expected.AsByte;
            return (eq, path, eq ? null : expected.AsByte.ToString(), eq ? null : actual.AsByte.ToString());
        }

        // Object types
        if (actual.IsObj && expected.IsObj)
        {
            // String
            if (actual.AsObj is string sA && expected.AsObj is string sE)
            {
                bool eq = sA == sE;
                return (eq, path, eq ? null : $"\"{sE}\"", eq ? null : $"\"{sA}\"");
            }

            // Array
            if (actual.AsObj is List<StashValue> arrA && expected.AsObj is List<StashValue> arrE)
            {
                if (arrA.Count != arrE.Count)
                    return (false, path, $"array[{arrE.Count}]", $"array[{arrA.Count}]");
                for (int i = 0; i < arrA.Count; i++)
                {
                    var result = DeepEquals(arrA[i], arrE[i], $"{path}[{i}]");
                    if (!result.Equal) return result;
                }
                return (true, "", null, null);
            }

            // Dictionary
            if (actual.AsObj is StashDictionary dictA && expected.AsObj is StashDictionary dictE)
            {
                foreach (var kvp in dictE.RawEntries())
                {
                    string keyStr = RuntimeValues.Stringify(kvp.Key);
                    string keyPath = $"{path}[\"{keyStr}\"]";
                    if (!dictA.Has(kvp.Key))
                        return (false, keyPath, RuntimeValues.Stringify(kvp.Value.ToObject()), "missing");
                    var result = DeepEquals(dictA.Get(kvp.Key), kvp.Value, keyPath);
                    if (!result.Equal) return result;
                }
                foreach (var kvp in dictA.RawEntries())
                {
                    if (!dictE.Has(kvp.Key))
                    {
                        string keyStr = RuntimeValues.Stringify(kvp.Key);
                        return (false, $"{path}[\"{keyStr}\"]", "missing", RuntimeValues.Stringify(kvp.Value.ToObject()));
                    }
                }
                return (true, "", null, null);
            }

            // Struct instance
            if (actual.AsObj is StashInstance instA && expected.AsObj is StashInstance instE)
            {
                if (instA.TypeName != instE.TypeName)
                    return (false, path, $"<{instE.TypeName}>", $"<{instA.TypeName}>");
                var expFields = instE.GetFields();
                var actFields = instA.GetFields();
                foreach (var kvp in expFields)
                {
                    string fieldPath = $"{path}.{kvp.Key}";
                    if (!actFields.TryGetValue(kvp.Key, out StashValue actVal))
                        return (false, fieldPath, RuntimeValues.Stringify(kvp.Value.ToObject()), "missing");
                    var result = DeepEquals(actVal, kvp.Value, fieldPath);
                    if (!result.Equal) return result;
                }
                return (true, "", null, null);
            }
        }

        // Fallback: type mismatch or unhandled type
        return (false, path, RuntimeValues.Stringify(expected.ToObject()), RuntimeValues.Stringify(actual.ToObject()));
    }

}
