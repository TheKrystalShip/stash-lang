namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Stash.Common;
using Stash.Runtime;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the 'test' namespace (test.it, test.describe, test.skip, etc.).
/// </summary>
public static class TestBuiltIns
{
    public static NamespaceDefinition Define()
    {
        // ── test namespace ───────────────────────────────────────────
        var ns = new NamespaceBuilder("test");

        // test.it(name, fn) — register and execute a test case
        ns.Function("it", [Param("name", "string"), Param("fn", "function")], (ctx, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("test.it() requires a string name as first argument.", ctx.CurrentSpan);
            }
            if (args[1] is not IStashCallable body)
            {
                throw new RuntimeError("test.it() requires a function as second argument.", ctx.CurrentSpan);
            }

            var harness = ctx.TestHarness as ITestHarness;
            var span = ctx.CurrentSpan ?? new SourceSpan("<unknown>", 1, 1, 1, 1);

            // Build the fully qualified test name from describe context
            string fullName = BuildFullName(ctx, ctx.CurrentDescribe, name);

            // Check test filter
            if (ctx.TestFilter is not null)
            {
                bool matches = ctx.TestFilter.Any(f => fullName.StartsWith(f));
                if (!matches)
                {
                    return null; // Silent — filtered-out tests emit nothing
                }
            }

            // Discovery mode — record but don't execute
            if (ctx.DiscoveryMode)
            {
                (ctx.TestHarness as ITestHarness)?.OnTestDiscovered(fullName, span);
                return null;
            }

            harness?.OnTestStart(fullName, span);
            var sw = Stopwatch.StartNew();

            try
            {
                // Run beforeEach hooks from all describe levels (outermost to innermost)
                foreach (var level in ctx.BeforeEachHooks)
                {
                    foreach (var hook in level)
                    {
                        hook.Call(ctx, new List<object?>());
                    }
                }

                body.Call(ctx, new List<object?>());
                sw.Stop();

                // Run afterEach hooks from all describe levels (innermost to outermost)
                for (int i = ctx.AfterEachHooks.Count - 1; i >= 0; i--)
                {
                    foreach (var hook in ctx.AfterEachHooks[i])
                    {
                        hook.Call(ctx, new List<object?>());
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
        });

        // test.skip(name, fn) — register a skipped test; body is never executed
        ns.Function("skip", [Param("name", "string"), Param("fn", "function")], (ctx, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("test.skip() requires a string name as first argument.", ctx.CurrentSpan);
            }
            if (args[1] is not IStashCallable)
            {
                throw new RuntimeError("test.skip() requires a function as second argument.", ctx.CurrentSpan);
            }

            string fullName = BuildFullName(ctx, ctx.CurrentDescribe, name);

            // Check test filter
            if (ctx.TestFilter is not null)
            {
                bool matches = ctx.TestFilter.Any(f => fullName.StartsWith(f));
                if (!matches)
                {
                    return null;
                }
            }

            // Discovery mode — record but don't execute
            if (ctx.DiscoveryMode)
            {
                var span = ctx.CurrentSpan ?? new SourceSpan("<unknown>", 1, 1, 1, 1);
                (ctx.TestHarness as ITestHarness)?.OnTestDiscovered(fullName, span);
                return null;
            }

            (ctx.TestHarness as ITestHarness)?.OnTestSkip(fullName, "skipped");
            return null;
        });

        // test.describe(name, fn) — group tests
        ns.Function("describe", [Param("name", "string"), Param("fn", "function")], (ctx, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("test.describe() requires a string name as first argument.", ctx.CurrentSpan);
            }
            if (args[1] is not IStashCallable body)
            {
                throw new RuntimeError("test.describe() requires a function as second argument.", ctx.CurrentSpan);
            }

            var harness = ctx.TestHarness as ITestHarness;

            // Build the fully qualified suite name from nested describes
            string fullName = ctx.CurrentDescribe is not null
                ? $"{ctx.CurrentDescribe} > {name}"
                : $"{Path.GetFileName(ctx.CurrentFile ?? "unknown")} > {name}";

            // Check test filter
            if (ctx.TestFilter is not null)
            {
                bool anyMatch = ctx.TestFilter.Any(f => f.StartsWith(fullName) || fullName.StartsWith(f));
                if (!anyMatch)
                {
                    return null; // Skip entire describe block
                }
            }

            string? previousDescribe = ctx.CurrentDescribe;
            ctx.CurrentDescribe = fullName;

            harness?.OnSuiteStart(fullName);

            int passedBefore = harness?.PassedCount ?? 0;
            int failedBefore = harness?.FailedCount ?? 0;
            int skippedBefore = harness?.SkippedCount ?? 0;

            // Push hook layers for this describe scope
            ctx.BeforeEachHooks.Add(new List<IStashCallable>());
            ctx.AfterEachHooks.Add(new List<IStashCallable>());
            ctx.AfterAllHooks.Add(new List<IStashCallable>());

            try
            {
                body.Call(ctx, new List<object?>());
            }
            finally
            {
                // Run afterAll hooks for this scope
                foreach (var hook in ctx.AfterAllHooks[^1])
                {
                    hook.Call(ctx, new List<object?>());
                }

                // Pop hook layers
                ctx.BeforeEachHooks.RemoveAt(ctx.BeforeEachHooks.Count - 1);
                ctx.AfterEachHooks.RemoveAt(ctx.AfterEachHooks.Count - 1);
                ctx.AfterAllHooks.RemoveAt(ctx.AfterAllHooks.Count - 1);

                int passed = (harness?.PassedCount ?? 0) - passedBefore;
                int failed = (harness?.FailedCount ?? 0) - failedBefore;
                int skipped = (harness?.SkippedCount ?? 0) - skippedBefore;

                harness?.OnSuiteEnd(fullName, passed, failed, skipped);
                ctx.CurrentDescribe = previousDescribe;
            }

            return null;
        });

        // test.beforeAll(fn) — execute fn() immediately inside a describe block (runs before any tests below it)
        ns.Function("beforeAll", [Param("fn", "function")], (ctx, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("test.beforeAll() requires a function argument.", ctx.CurrentSpan);
            }
            if (ctx.BeforeEachHooks.Count == 0)
            {
                throw new RuntimeError("test.beforeAll() must be used inside a test.describe() block.", ctx.CurrentSpan);
            }
            callable.Call(ctx, new List<object?>());
            return null;
        });

        // test.afterAll(fn) — register fn() to run when the current describe block ends
        ns.Function("afterAll", [Param("fn", "function")], (ctx, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("test.afterAll() requires a function argument.", ctx.CurrentSpan);
            }
            if (ctx.AfterAllHooks.Count == 0)
            {
                throw new RuntimeError("test.afterAll() must be used inside a test.describe() block.", ctx.CurrentSpan);
            }
            ctx.AfterAllHooks[^1].Add(callable);
            return null;
        });

        // test.beforeEach(fn) — register fn() to run before each test in the current describe scope
        ns.Function("beforeEach", [Param("fn", "function")], (ctx, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("test.beforeEach() requires a function argument.", ctx.CurrentSpan);
            }
            if (ctx.BeforeEachHooks.Count == 0)
            {
                throw new RuntimeError("test.beforeEach() must be used inside a test.describe() block.", ctx.CurrentSpan);
            }
            ctx.BeforeEachHooks[^1].Add(callable);
            return null;
        });

        // test.afterEach(fn) — register fn() to run after each test in the current describe scope
        ns.Function("afterEach", [Param("fn", "function")], (ctx, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("test.afterEach() requires a function argument.", ctx.CurrentSpan);
            }
            if (ctx.AfterEachHooks.Count == 0)
            {
                throw new RuntimeError("test.afterEach() must be used inside a test.describe() block.", ctx.CurrentSpan);
            }
            ctx.AfterEachHooks[^1].Add(callable);
            return null;
        });

        // test.captureOutput(fn) — execute fn() with output redirected to a string, returns captured output
        ns.Function("captureOutput", [Param("fn", "function")], (ctx, args) =>
        {
            if (args[0] is not IStashCallable callable)
            {
                throw new RuntimeError("test.captureOutput() requires a function argument.", ctx.CurrentSpan);
            }

            var previousOutput = ctx.Output;
            var sw = new StringWriter();
            ctx.Output = sw;
            try
            {
                callable.Call(ctx, new List<object?>());
            }
            finally
            {
                ctx.Output = previousOutput;
            }
            return sw.ToString();
        });

        return ns.Build();
    }

    private static string BuildFullName(IInterpreterContext ctx, string? currentDescribe, string testName)
    {
        if (currentDescribe is not null)
        {
            return $"{currentDescribe} > {testName}";  // currentDescribe already has filename prefix
        }

        string fileName = Path.GetFileName(ctx.CurrentFile ?? "unknown");
        return $"{fileName} > {testName}";
    }
}
