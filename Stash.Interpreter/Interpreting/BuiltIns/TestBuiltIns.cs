namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Stash.Common;
using Stash.Interpreting.Types;
using Stash.Testing;

/// <summary>
/// Registers the 'assert' namespace and global test functions (test, describe).
/// </summary>
public static class TestBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        // ── assert namespace ─────────────────────────────────────────
        var assert = new StashNamespace("assert");

        // assert.equal(actual, expected) — no type coercion
        assert.Define("equal", new BuiltInFunction("assert.equal", 2, (interp, args) =>
        {
            object? actual = args[0];
            object? expected = args[1];
            if (!RuntimeValues.IsEqual(actual, expected))
            {
                string msg = $"assert.equal failed: expected {RuntimeValues.Stringify(expected)} but got {RuntimeValues.Stringify(actual)}";
                throw new AssertionError(msg, expected, actual, interp.CurrentSpan);
            }
            return null;
        }));

        // assert.notEqual(actual, expected)
        assert.Define("notEqual", new BuiltInFunction("assert.notEqual", 2, (interp, args) =>
        {
            object? actual = args[0];
            object? expected = args[1];
            if (RuntimeValues.IsEqual(actual, expected))
            {
                string msg = $"assert.notEqual failed: expected values to differ but both are {RuntimeValues.Stringify(actual)}";
                throw new AssertionError(msg, expected, actual, interp.CurrentSpan);
            }
            return null;
        }));

        // assert.true(value)
        assert.Define("true", new BuiltInFunction("assert.true", 1, (interp, args) =>
        {
            if (!RuntimeValues.IsTruthy(args[0]))
            {
                string msg = $"assert.true failed: expected truthy value but got {RuntimeValues.Stringify(args[0])}";
                throw new AssertionError(msg, true, args[0], interp.CurrentSpan);
            }
            return null;
        }));

        // assert.false(value)
        assert.Define("false", new BuiltInFunction("assert.false", 1, (interp, args) =>
        {
            if (RuntimeValues.IsTruthy(args[0]))
            {
                string msg = $"assert.false failed: expected falsy value but got {RuntimeValues.Stringify(args[0])}";
                throw new AssertionError(msg, false, args[0], interp.CurrentSpan);
            }
            return null;
        }));

        // assert.null(value)
        assert.Define("null", new BuiltInFunction("assert.null", 1, (interp, args) =>
        {
            if (args[0] is not null)
            {
                string msg = $"assert.null failed: expected null but got {RuntimeValues.Stringify(args[0])}";
                throw new AssertionError(msg, null, args[0], interp.CurrentSpan);
            }
            return null;
        }));

        // assert.notNull(value)
        assert.Define("notNull", new BuiltInFunction("assert.notNull", 1, (interp, args) =>
        {
            if (args[0] is null)
            {
                string msg = "assert.notNull failed: expected non-null value but got null";
                throw new AssertionError(msg, "non-null", null, interp.CurrentSpan);
            }
            return null;
        }));

        // assert.greater(a, b) — a > b
        assert.Define("greater", new BuiltInFunction("assert.greater", 2, (interp, args) =>
        {
            double a = ToDouble(args[0], "assert.greater", interp.CurrentSpan);
            double b = ToDouble(args[1], "assert.greater", interp.CurrentSpan);
            if (!(a > b))
            {
                string msg = $"assert.greater failed: expected {RuntimeValues.Stringify(args[0])} > {RuntimeValues.Stringify(args[1])}";
                throw new AssertionError(msg, $"> {RuntimeValues.Stringify(args[1])}", args[0], interp.CurrentSpan);
            }
            return null;
        }));

        // assert.less(a, b) — a < b
        assert.Define("less", new BuiltInFunction("assert.less", 2, (interp, args) =>
        {
            double a = ToDouble(args[0], "assert.less", interp.CurrentSpan);
            double b = ToDouble(args[1], "assert.less", interp.CurrentSpan);
            if (!(a < b))
            {
                string msg = $"assert.less failed: expected {RuntimeValues.Stringify(args[0])} < {RuntimeValues.Stringify(args[1])}";
                throw new AssertionError(msg, $"< {RuntimeValues.Stringify(args[1])}", args[0], interp.CurrentSpan);
            }
            return null;
        }));

        // assert.throws(fn) — fn() should throw; returns error message
        assert.Define("throws", new BuiltInFunction("assert.throws", 1, (interp, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("assert.throws requires a function argument.", interp.CurrentSpan);
            }
            try
            {
                callable.Call(interp, new List<object?>());
            }
            catch (RuntimeError ex)
            {
                return ex.Message;
            }
            string msg = "assert.throws failed: expected function to throw but it did not";
            throw new AssertionError(msg, "error", "no error", interp.CurrentSpan);
        }));

        // assert.fail(message?) — unconditional failure
        assert.Define("fail", new BuiltInFunction("assert.fail", 1, (interp, args) =>
        {
            string msg = args[0] is string s ? s : "assert.fail called";
            throw new AssertionError(msg, null, null, interp.CurrentSpan);
        }));

        globals.Define("assert", assert);

        // ── Global test functions ────────────────────────────────────

        // test(name, fn) — register and execute a test case
        globals.Define("test", new BuiltInFunction("test", 2, (interp, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("test() requires a string name as first argument.", interp.CurrentSpan);
            }
            if (args[1] is not IStashCallable body)
            {
                throw new RuntimeError("test() requires a function as second argument.", interp.CurrentSpan);
            }

            var harness = interp.TestHarness;
            var span = interp.CurrentSpan ?? new SourceSpan("<unknown>", 0, 0, 0, 0);

            // Build the fully qualified test name from describe context
            string fullName = interp.CurrentDescribe is not null
                ? $"{interp.CurrentDescribe} > {name}"
                : name;

            harness?.OnTestStart(fullName, span);
            var sw = Stopwatch.StartNew();

            try
            {
                body.Call(interp, new List<object?>());
                sw.Stop();
                harness?.OnTestPass(fullName, sw.Elapsed);
            }
            catch (AssertionError ex)
            {
                sw.Stop();
                if (harness is not null)
                {
                    harness.OnTestFail(fullName, ex.Message, ex.Span ?? span, sw.Elapsed);
                }
                else
                {
                    // No harness — assertion failures crash the script (normal behavior)
                    throw;
                }
            }
            catch (RuntimeError ex)
            {
                sw.Stop();
                if (harness is not null)
                {
                    harness.OnTestFail(fullName, ex.Message, ex.Span ?? span, sw.Elapsed);
                }
                else
                {
                    throw;
                }
            }

            return null;
        }));

        // describe(name, fn) — group tests
        globals.Define("describe", new BuiltInFunction("describe", 2, (interp, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("describe() requires a string name as first argument.", interp.CurrentSpan);
            }
            if (args[1] is not IStashCallable body)
            {
                throw new RuntimeError("describe() requires a function as second argument.", interp.CurrentSpan);
            }

            var harness = interp.TestHarness;

            // Build the fully qualified suite name from nested describes
            string fullName = interp.CurrentDescribe is not null
                ? $"{interp.CurrentDescribe} > {name}"
                : name;

            string? previousDescribe = interp.CurrentDescribe;
            interp.CurrentDescribe = fullName;

            harness?.OnSuiteStart(fullName);

            int passedBefore = harness?.PassedCount ?? 0;
            int failedBefore = harness?.FailedCount ?? 0;
            int skippedBefore = harness?.SkippedCount ?? 0;

            try
            {
                body.Call(interp, new List<object?>());
            }
            finally
            {
                int passed = (harness?.PassedCount ?? 0) - passedBefore;
                int failed = (harness?.FailedCount ?? 0) - failedBefore;
                int skipped = (harness?.SkippedCount ?? 0) - skippedBefore;

                harness?.OnSuiteEnd(fullName, passed, failed, skipped);
                interp.CurrentDescribe = previousDescribe;
            }

            return null;
        }));

        // captureOutput(fn) — execute fn() with output redirected to a string, returns captured output
        globals.Define("captureOutput", new BuiltInFunction("captureOutput", 1, (interp, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("captureOutput() requires a function argument.", interp.CurrentSpan);
            }

            var previousOutput = interp.Output;
            var sw = new StringWriter();
            interp.Output = sw;
            try
            {
                callable.Call(interp, new List<object?>());
            }
            finally
            {
                interp.Output = previousOutput;
            }
            return sw.ToString();
        }));
    }

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
