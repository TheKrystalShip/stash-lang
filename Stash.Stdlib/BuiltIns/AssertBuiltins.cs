namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using Stash.Common;
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
        ns.Function("equal", [Param("actual"), Param("expected")], (ctx, args) =>
        {
            object? actual = args[0];
            object? expected = args[1];
            if (!RuntimeValues.IsEqual(actual, expected))
            {
                string msg = $"assert.equal failed: expected {RuntimeValues.Stringify(expected)} but got {RuntimeValues.Stringify(actual)}";
                throw new AssertionError(msg, expected, actual, ctx.CurrentSpan);
            }
            return null;
        });

        // assert.notEqual(actual, expected)
        ns.Function("notEqual", [Param("actual"), Param("expected")], (ctx, args) =>
        {
            object? actual = args[0];
            object? expected = args[1];
            if (RuntimeValues.IsEqual(actual, expected))
            {
                string msg = $"assert.notEqual failed: expected values to differ but both are {RuntimeValues.Stringify(actual)}";
                throw new AssertionError(msg, expected, actual, ctx.CurrentSpan);
            }
            return null;
        });

        // assert.true(value)
        ns.Function("true", [Param("value")], (ctx, args) =>
        {
            if (!RuntimeValues.IsTruthy(args[0]))
            {
                string msg = $"assert.true failed: expected truthy value but got {RuntimeValues.Stringify(args[0])}";
                throw new AssertionError(msg, true, args[0], ctx.CurrentSpan);
            }
            return null;
        });

        // assert.false(value)
        ns.Function("false", [Param("value")], (ctx, args) =>
        {
            if (RuntimeValues.IsTruthy(args[0]))
            {
                string msg = $"assert.false failed: expected falsy value but got {RuntimeValues.Stringify(args[0])}";
                throw new AssertionError(msg, false, args[0], ctx.CurrentSpan);
            }
            return null;
        });

        // assert.null(value)
        ns.Function("null", [Param("value")], (ctx, args) =>
        {
            if (args[0] is not null)
            {
                string msg = $"assert.null failed: expected null but got {RuntimeValues.Stringify(args[0])}";
                throw new AssertionError(msg, null, args[0], ctx.CurrentSpan);
            }
            return null;
        });

        // assert.notNull(value)
        ns.Function("notNull", [Param("value")], (ctx, args) =>
        {
            if (args[0] is null)
            {
                string msg = "assert.notNull failed: expected non-null value but got null";
                throw new AssertionError(msg, "non-null", null, ctx.CurrentSpan);
            }
            return null;
        });

        // assert.greater(a, b) — a > b
        ns.Function("greater", [Param("a"), Param("b")], (ctx, args) =>
        {
            double a = ToDouble(args[0], "assert.greater", ctx.CurrentSpan);
            double b = ToDouble(args[1], "assert.greater", ctx.CurrentSpan);
            if (!(a > b))
            {
                string msg = $"assert.greater failed: expected {RuntimeValues.Stringify(args[0])} > {RuntimeValues.Stringify(args[1])}";
                throw new AssertionError(msg, $"> {RuntimeValues.Stringify(args[1])}", args[0], ctx.CurrentSpan);
            }
            return null;
        });

        // assert.less(a, b) — a < b
        ns.Function("less", [Param("a"), Param("b")], (ctx, args) =>
        {
            double a = ToDouble(args[0], "assert.less", ctx.CurrentSpan);
            double b = ToDouble(args[1], "assert.less", ctx.CurrentSpan);
            if (!(a < b))
            {
                string msg = $"assert.less failed: expected {RuntimeValues.Stringify(args[0])} < {RuntimeValues.Stringify(args[1])}";
                throw new AssertionError(msg, $"< {RuntimeValues.Stringify(args[1])}", args[0], ctx.CurrentSpan);
            }
            return null;
        });

        // assert.throws(fn) — fn() should throw; returns error message
        ns.Function("throws", [Param("fn", "function")], (ctx, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("assert.throws requires a function argument.", ctx.CurrentSpan);
            }
            try
            {
                callable.Call(ctx, new List<object?>());
            }
            catch (RuntimeError ex)
            {
                return ex.Message;
            }
            string msg = "assert.throws failed: expected function to throw but it did not";
            throw new AssertionError(msg, "error", "no error", ctx.CurrentSpan);
        });

        // assert.fail(message?) — unconditional failure
        ns.Function("fail", [Param("message", "string?")], (ctx, args) =>
        {
            string msg = args[0] is string s ? s : "assert.fail called";
            throw new AssertionError(msg, null, null, ctx.CurrentSpan);
        });

        return ns.Build();
    }

    /// <summary>
    /// Converts a Stash runtime value to a <see cref="double"/> for use in numeric
    /// comparison assertions (<c>assert.greater</c> and <c>assert.less</c>).
    /// </summary>
    /// <param name="value">The runtime value to convert. Must be a <see cref="long"/> or <see cref="double"/>.</param>
    /// <param name="funcName">The name of the calling assert function, used in the error message.</param>
    /// <param name="span">The source location to attach to the <see cref="RuntimeError"/> if conversion fails.</param>
    /// <returns>The numeric value as a <see cref="double"/>.</returns>
    /// <exception cref="RuntimeError">
    /// Thrown when <paramref name="value"/> is neither a <see cref="long"/> nor a <see cref="double"/>.
    /// </exception>
    private static double ToDouble(object? value, string funcName, SourceSpan? span)
    {
        return value switch
        {
            long l => l,
            double d => d,
            _ => throw new RuntimeError($"{funcName} requires numeric arguments.", span)
        };
    }
}
