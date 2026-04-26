namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the 'assert' namespace.
/// </summary>
public static class AssertBuiltIns
{
    public static NamespaceDefinition Define()
    {
        // ── assert namespace ───────────────────────────────────────────
        var ns = new NamespaceBuilder("assert");

        // assert.equal(actual, expected) — no type coercion
        ns.Function("equal", [Param("actual"), Param("expected")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            object? actual = args[0].ToObject();
            object? expected = args[1].ToObject();
            if (!RuntimeValues.IsEqual(actual, expected))
            {
                string msg = $"assert.equal failed: expected {RuntimeValues.Stringify(expected)} but got {RuntimeValues.Stringify(actual)}";
                throw new AssertionError(msg, expected, actual, ctx.CurrentSpan);
            }
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Asserts that actual equals expected using strict equality (no type coercion). Throws AssertionError if not.\n@param actual The actual value\n@param expected The expected value\n@return null");

        // assert.notEqual(actual, expected)
        ns.Function("notEqual", [Param("actual"), Param("expected")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            object? actual = args[0].ToObject();
            object? expected = args[1].ToObject();
            if (RuntimeValues.IsEqual(actual, expected))
            {
                string msg = $"assert.notEqual failed: expected values to differ but both are {RuntimeValues.Stringify(actual)}";
                throw new AssertionError(msg, expected, actual, ctx.CurrentSpan);
            }
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Asserts that actual does not equal expected. Throws AssertionError if they are equal.\n@param actual The actual value\n@param expected The value to compare against\n@return null");

        // assert.true(value)
        ns.Function("true", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            object? val = args[0].ToObject();
            if (!RuntimeValues.IsTruthy(val))
            {
                string msg = $"assert.true failed: expected truthy value but got {RuntimeValues.Stringify(val)}";
                throw new AssertionError(msg, true, val, ctx.CurrentSpan);
            }
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Asserts that the value is truthy. Throws AssertionError if falsy.\n@param value The value to check\n@return null");

        // assert.false(value)
        ns.Function("false", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            object? val = args[0].ToObject();
            if (RuntimeValues.IsTruthy(val))
            {
                string msg = $"assert.false failed: expected falsy value but got {RuntimeValues.Stringify(val)}";
                throw new AssertionError(msg, false, val, ctx.CurrentSpan);
            }
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Asserts that the value is falsy. Throws AssertionError if truthy.\n@param value The value to check\n@return null");

        // assert.null(value)
        ns.Function("null", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (!args[0].IsNull)
            {
                object? val = args[0].ToObject();
                string msg = $"assert.null failed: expected null but got {RuntimeValues.Stringify(val)}";
                throw new AssertionError(msg, null, val, ctx.CurrentSpan);
            }
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Asserts that the value is null. Throws AssertionError if not null.\n@param value The value to check\n@return null");

        // assert.notNull(value)
        ns.Function("notNull", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args[0].IsNull)
            {
                string msg = "assert.notNull failed: expected non-null value but got null";
                throw new AssertionError(msg, "non-null", null, ctx.CurrentSpan);
            }
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Asserts that the value is not null. Throws AssertionError if null.\n@param value The value to check\n@return null");

        // assert.greater(a, b) — a > b
        ns.Function("greater", [Param("a"), Param("b")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double a = SvArgs.Numeric(args, 0, "assert.greater");
            double b = SvArgs.Numeric(args, 1, "assert.greater");
            if (!(a > b))
            {
                object? aObj = args[0].ToObject();
                object? bObj = args[1].ToObject();
                string msg = $"assert.greater failed: expected {RuntimeValues.Stringify(aObj)} > {RuntimeValues.Stringify(bObj)}";
                throw new AssertionError(msg, $"> {RuntimeValues.Stringify(bObj)}", aObj, ctx.CurrentSpan);
            }
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Asserts that a is greater than b. Throws AssertionError if not.\n@param a The left-hand value\n@param b The right-hand value\n@return null");

        // assert.less(a, b) — a < b
        ns.Function("less", [Param("a"), Param("b")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double a = SvArgs.Numeric(args, 0, "assert.less");
            double b = SvArgs.Numeric(args, 1, "assert.less");
            if (!(a < b))
            {
                object? aObj = args[0].ToObject();
                object? bObj = args[1].ToObject();
                string msg = $"assert.less failed: expected {RuntimeValues.Stringify(aObj)} < {RuntimeValues.Stringify(bObj)}";
                throw new AssertionError(msg, $"< {RuntimeValues.Stringify(bObj)}", aObj, ctx.CurrentSpan);
            }
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Asserts that a is less than b. Throws AssertionError if not.\n@param a The left-hand value\n@param b The right-hand value\n@return null");

        // assert.throws(fn) — fn() should throw; returns error message
        ns.Function("throws", [Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var callable = SvArgs.Callable(args, 0, "assert.throws");
            try
            {
                ctx.InvokeCallbackDirect(callable, ReadOnlySpan<StashValue>.Empty);
            }
            catch (RuntimeError ex)
            {
                return StashValue.FromObj(ex.Message);
            }
            string msg = "assert.throws failed: expected function to throw but it did not";
            throw new AssertionError(msg, "error", "no error", ctx.CurrentSpan);
        },
            returnType: "string",
            documentation: "Asserts that fn throws an error when called. Returns the error message if it throws.\n@param fn The function to invoke\n@return The error message thrown by fn");

        // assert.fail(message?) — unconditional failure
        ns.Function("fail", [Param("message", "string?")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string msg = args[0].ToObject() is string s ? s : "assert.fail called";
            throw new AssertionError(msg, null, null, ctx.CurrentSpan);
        },
            returnType: "never",
            documentation: "Immediately fails the test with an optional message.\n@param message Optional failure message\n@return never");

        // assert.deepEqual(actual, expected) — recursive structural equality
        ns.Function("deepEqual", [Param("actual"), Param("expected")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            StashValue actual = args[0];
            StashValue expected = args[1];

            var (equal, failPath, expectedStr, actualStr) = DeepEquals(actual, expected, "");

            if (!equal)
            {
                string pathInfo = failPath.Length > 0 ? $"\n  at: {failPath}" : "";
                string msg = $"assert.deepEqual failed{pathInfo}\n  Expected: {expectedStr}\n  Actual:   {actualStr}";
                throw new AssertionError(msg, expected.ToObject(), actual.ToObject(), ctx.CurrentSpan);
            }

            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Asserts structural equality between actual and expected by recursively comparing their contents. Works for primitives, arrays, dictionaries, and struct instances.\n@param actual The actual value\n@param expected The expected value\n@return null");

        // assert.closeTo(actual, expected, delta) — numeric proximity
        ns.Function("closeTo", [Param("actual", "float"), Param("expected", "float"), Param("delta", "float")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double actual = SvArgs.Numeric(args, 0, "assert.closeTo");
            double expected = SvArgs.Numeric(args, 1, "assert.closeTo");
            double delta = SvArgs.Numeric(args, 2, "assert.closeTo");

            if (delta < 0)
                throw new RuntimeError("assert.closeTo: delta must be non-negative.");

            double diff = Math.Abs(actual - expected);
            if (diff > delta)
            {
                string msg = $"assert.closeTo failed: expected {actual} to be within {delta} of {expected} (difference: {diff})";
                throw new AssertionError(msg, expected, actual, ctx.CurrentSpan);
            }

            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Asserts that actual is within delta of expected. Throws AssertionError if the absolute difference exceeds delta.\n@param actual The actual value\n@param expected The expected value\n@param delta The maximum allowed difference (must be non-negative)\n@return null");

        return ns.Build();
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
