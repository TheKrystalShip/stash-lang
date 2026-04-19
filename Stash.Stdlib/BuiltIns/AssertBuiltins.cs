namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
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

        return ns.Build();
    }

}
