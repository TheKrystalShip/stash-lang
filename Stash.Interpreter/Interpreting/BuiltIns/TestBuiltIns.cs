namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            var span = interp.CurrentSpan ?? new SourceSpan("<unknown>", 1, 1, 1, 1);

            // Build the fully qualified test name from describe context
            string fullName = BuildFullName(interp, interp.CurrentDescribe, name);

            // Check test filter
            if (interp.TestFilter is not null)
            {
                bool matches = interp.TestFilter.Any(f => fullName.StartsWith(f));
                if (!matches)
                {
                    return null; // Silent — filtered-out tests emit nothing
                }
            }

            // Discovery mode — record but don't execute
            if (interp.DiscoveryMode)
            {
                interp.TestHarness?.OnTestDiscovered(fullName, span);
                return null;
            }

            harness?.OnTestStart(fullName, span);
            var sw = Stopwatch.StartNew();

            try
            {
                // Run beforeEach hooks from all describe levels (outermost to innermost)
                foreach (var level in interp.BeforeEachHooks)
                {
                    foreach (var hook in level)
                    {
                        hook.Call(interp, new List<object?>());
                    }
                }

                body.Call(interp, new List<object?>());
                sw.Stop();

                // Run afterEach hooks from all describe levels (innermost to outermost)
                for (int i = interp.AfterEachHooks.Count - 1; i >= 0; i--)
                {
                    foreach (var hook in interp.AfterEachHooks[i])
                    {
                        hook.Call(interp, new List<object?>());
                    }
                }

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

        // skip(name, fn) — register a skipped test; body is never executed
        globals.Define("skip", new BuiltInFunction("skip", 2, (interp, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("skip() requires a string name as first argument.", interp.CurrentSpan);
            }
            if (args[1] is not IStashCallable)
            {
                throw new RuntimeError("skip() requires a function as second argument.", interp.CurrentSpan);
            }

            string fullName = BuildFullName(interp, interp.CurrentDescribe, name);

            // Check test filter
            if (interp.TestFilter is not null)
            {
                bool matches = interp.TestFilter.Any(f => fullName.StartsWith(f));
                if (!matches)
                {
                    return null;
                }
            }

            // Discovery mode — record but don't execute
            if (interp.DiscoveryMode)
            {
                var span = interp.CurrentSpan ?? new SourceSpan("<unknown>", 1, 1, 1, 1);
                interp.TestHarness?.OnTestDiscovered(fullName, span);
                return null;
            }

            interp.TestHarness?.OnTestSkip(fullName, "skipped");
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
                : $"{Path.GetFileName(interp.CurrentFile ?? "unknown")} > {name}";

            // Check test filter
            if (interp.TestFilter is not null)
            {
                bool anyMatch = interp.TestFilter.Any(f => f.StartsWith(fullName) || fullName.StartsWith(f));
                if (!anyMatch)
                {
                    return null; // Skip entire describe block
                }
            }

            string? previousDescribe = interp.CurrentDescribe;
            interp.CurrentDescribe = fullName;

            harness?.OnSuiteStart(fullName);

            int passedBefore = harness?.PassedCount ?? 0;
            int failedBefore = harness?.FailedCount ?? 0;
            int skippedBefore = harness?.SkippedCount ?? 0;

            // Push hook layers for this describe scope
            interp.BeforeEachHooks.Add(new List<IStashCallable>());
            interp.AfterEachHooks.Add(new List<IStashCallable>());
            interp.AfterAllHooks.Add(new List<IStashCallable>());

            try
            {
                body.Call(interp, new List<object?>());
            }
            finally
            {
                // Run afterAll hooks for this scope
                foreach (var hook in interp.AfterAllHooks[^1])
                {
                    hook.Call(interp, new List<object?>());
                }

                // Pop hook layers
                interp.BeforeEachHooks.RemoveAt(interp.BeforeEachHooks.Count - 1);
                interp.AfterEachHooks.RemoveAt(interp.AfterEachHooks.Count - 1);
                interp.AfterAllHooks.RemoveAt(interp.AfterAllHooks.Count - 1);

                int passed = (harness?.PassedCount ?? 0) - passedBefore;
                int failed = (harness?.FailedCount ?? 0) - failedBefore;
                int skipped = (harness?.SkippedCount ?? 0) - skippedBefore;

                harness?.OnSuiteEnd(fullName, passed, failed, skipped);
                interp.CurrentDescribe = previousDescribe;
            }

            return null;
        }));

        // beforeAll(fn) — execute fn() immediately inside a describe block (runs before any tests below it)
        globals.Define("beforeAll", new BuiltInFunction("beforeAll", 1, (interp, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("beforeAll() requires a function argument.", interp.CurrentSpan);
            }
            if (interp.BeforeEachHooks.Count == 0)
            {
                throw new RuntimeError("beforeAll() must be used inside a describe() block.", interp.CurrentSpan);
            }
            callable.Call(interp, new List<object?>());
            return null;
        }));

        // afterAll(fn) — register fn() to run when the current describe block ends
        globals.Define("afterAll", new BuiltInFunction("afterAll", 1, (interp, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("afterAll() requires a function argument.", interp.CurrentSpan);
            }
            if (interp.AfterAllHooks.Count == 0)
            {
                throw new RuntimeError("afterAll() must be used inside a describe() block.", interp.CurrentSpan);
            }
            interp.AfterAllHooks[^1].Add(callable);
            return null;
        }));

        // beforeEach(fn) — register fn() to run before each test in the current describe scope
        globals.Define("beforeEach", new BuiltInFunction("beforeEach", 1, (interp, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("beforeEach() requires a function argument.", interp.CurrentSpan);
            }
            if (interp.BeforeEachHooks.Count == 0)
            {
                throw new RuntimeError("beforeEach() must be used inside a describe() block.", interp.CurrentSpan);
            }
            interp.BeforeEachHooks[^1].Add(callable);
            return null;
        }));

        // afterEach(fn) — register fn() to run after each test in the current describe scope
        globals.Define("afterEach", new BuiltInFunction("afterEach", 1, (interp, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("afterEach() requires a function argument.", interp.CurrentSpan);
            }
            if (interp.AfterEachHooks.Count == 0)
            {
                throw new RuntimeError("afterEach() must be used inside a describe() block.", interp.CurrentSpan);
            }
            interp.AfterEachHooks[^1].Add(callable);
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

    private static string BuildFullName(Interpreter interp, string? currentDescribe, string testName)
    {
        if (currentDescribe is not null)
        {
            return $"{currentDescribe} > {testName}";  // currentDescribe already has filename prefix
        }

        string fileName = Path.GetFileName(interp.CurrentFile ?? "unknown");
        return $"{fileName} > {testName}";
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
