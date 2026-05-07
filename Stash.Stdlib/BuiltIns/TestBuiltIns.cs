namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Stash.Common;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the 'test' namespace (test.it, test.describe, test.skip, etc.).
/// </summary>
[StashNamespace]
public static partial class TestBuiltIns
{
    /// <summary>Defines and executes a test case with the given name and body function.</summary>
    /// <param name="name">The test case name</param>
    /// <param name="fn">The test body function</param>
    /// <returns>null</returns>
    [StashFn(Raw = true)]
    private static StashValue It(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var name = SvArgs.String(args, 0, "test.it");
        var body = SvArgs.Callable(args, 1, "test.it");
        var span = ctx.CurrentSpan ?? new SourceSpan("<unknown>", 1, 1, 1, 1);

        // Build the fully qualified test name from describe context
        string fullName = BuildFullName(ctx, ctx.CurrentDescribe, name);

        // Check test filter
        if (ctx.TestFilter is not null)
        {
            bool matches = ctx.TestFilter.Any(f => fullName.StartsWith(f));
            if (!matches)
            {
                return StashValue.Null; // Silent — filtered-out tests emit nothing
            }
        }

        // Discovery mode — record but don't execute
        if (ctx.DiscoveryMode)
        {
            ctx.TestHarness?.OnTestDiscovered(fullName, span);
            return StashValue.Null;
        }

        // If exclusive mode is active (any test.only was called), skip this test
        if (ctx.HasExclusiveTests)
        {
            ctx.TestHarness?.OnTestSkip(fullName, "test.only active");
            return StashValue.Null;
        }

        return RunTest(ctx, fullName, body, span);
    }

    /// <summary>Defines and executes an exclusive test case. When one or more test.only() calls exist, all test.it() calls are silently skipped.</summary>
    /// <param name="name">The test case name</param>
    /// <param name="fn">The test body function</param>
    /// <returns>null</returns>
    [StashFn(Raw = true)]
    private static StashValue Only(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        // Mark exclusive mode — subsequent test.it calls will be skipped
        ctx.HasExclusiveTests = true;

        var name = SvArgs.String(args, 0, "test.only");
        var body = SvArgs.Callable(args, 1, "test.only");
        var span = ctx.CurrentSpan ?? new SourceSpan("<unknown>", 1, 1, 1, 1);

        string fullName = BuildFullName(ctx, ctx.CurrentDescribe, name);

        // Check test filter
        if (ctx.TestFilter is not null)
        {
            bool matches = ctx.TestFilter.Any(f => fullName.StartsWith(f));
            if (!matches)
            {
                return StashValue.Null;
            }
        }

        // Discovery mode — record but don't execute
        if (ctx.DiscoveryMode)
        {
            ctx.TestHarness?.OnTestDiscovered(fullName, span);
            return StashValue.Null;
        }

        return RunTest(ctx, fullName, body, span);
    }

    /// <summary>Defines a skipped test case that will not be executed.</summary>
    /// <param name="name">The test case name</param>
    /// <param name="fn">The test body function (not executed)</param>
    /// <returns>null</returns>
    [StashFn(Raw = true)]
    private static StashValue Skip(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var name = SvArgs.String(args, 0, "test.skip");
        SvArgs.Callable(args, 1, "test.skip");
        string fullName = BuildFullName(ctx, ctx.CurrentDescribe, name);

        // Check test filter
        if (ctx.TestFilter is not null)
        {
            bool matches = ctx.TestFilter.Any(f => fullName.StartsWith(f));
            if (!matches)
            {
                return StashValue.Null;
            }
        }

        // Discovery mode — record but don't execute
        if (ctx.DiscoveryMode)
        {
            var span = ctx.CurrentSpan ?? new SourceSpan("<unknown>", 1, 1, 1, 1);
            ctx.TestHarness?.OnTestDiscovered(fullName, span);
            return StashValue.Null;
        }

        ctx.TestHarness?.OnTestSkip(fullName, "skipped");
        return StashValue.Null;
    }

    /// <summary>Groups related test cases under a named description block.</summary>
    /// <param name="name">The description block name</param>
    /// <param name="fn">The function containing test cases</param>
    /// <returns>null</returns>
    [StashFn(Raw = true)]
    private static StashValue Describe(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var name = SvArgs.String(args, 0, "test.describe");
        var body = SvArgs.Callable(args, 1, "test.describe");
        var harness = ctx.TestHarness;

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
                return StashValue.Null; // Skip entire describe block
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
            ctx.InvokeCallbackDirect(body, ReadOnlySpan<StashValue>.Empty);
        }
        finally
        {
            // Run afterAll hooks for this scope
            foreach (var hook in ctx.AfterAllHooks[^1])
            {
                ctx.InvokeCallbackDirect(hook, ReadOnlySpan<StashValue>.Empty);
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

        return StashValue.Null;
    }

    /// <summary>Registers a setup function to run once before all tests in the current describe block.</summary>
    /// <param name="fn">The setup function</param>
    /// <returns>null</returns>
    [StashFn(Raw = true)]
    private static StashValue BeforeAll(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var callable = SvArgs.Callable(args, 0, "test.beforeAll");
        if (ctx.BeforeEachHooks.Count == 0)
        {
            throw new RuntimeError("test.beforeAll() must be used inside a test.describe() block.", ctx.CurrentSpan);
        }
        ctx.InvokeCallbackDirect(callable, ReadOnlySpan<StashValue>.Empty);
        return StashValue.Null;
    }

    /// <summary>Registers a teardown function to run once after all tests in the current describe block.</summary>
    /// <param name="fn">The teardown function</param>
    /// <returns>null</returns>
    [StashFn(Raw = true)]
    private static StashValue AfterAll(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var callable = SvArgs.Callable(args, 0, "test.afterAll");
        if (ctx.AfterAllHooks.Count == 0)
        {
            throw new RuntimeError("test.afterAll() must be used inside a test.describe() block.", ctx.CurrentSpan);
        }
        ctx.AfterAllHooks[^1].Add(callable);
        return StashValue.Null;
    }

    /// <summary>Registers a setup function to run before each test case in the current describe block.</summary>
    /// <param name="fn">The setup function</param>
    /// <returns>null</returns>
    [StashFn(Raw = true)]
    private static StashValue BeforeEach(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var callable = SvArgs.Callable(args, 0, "test.beforeEach");
        if (ctx.BeforeEachHooks.Count == 0)
        {
            throw new RuntimeError("test.beforeEach() must be used inside a test.describe() block.", ctx.CurrentSpan);
        }
        ctx.BeforeEachHooks[^1].Add(callable);
        return StashValue.Null;
    }

    /// <summary>Registers a teardown function to run after each test case in the current describe block.</summary>
    /// <param name="fn">The teardown function</param>
    /// <returns>null</returns>
    [StashFn(Raw = true)]
    private static StashValue AfterEach(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var callable = SvArgs.Callable(args, 0, "test.afterEach");
        if (ctx.AfterEachHooks.Count == 0)
        {
            throw new RuntimeError("test.afterEach() must be used inside a test.describe() block.", ctx.CurrentSpan);
        }
        ctx.AfterEachHooks[^1].Add(callable);
        return StashValue.Null;
    }

    /// <summary>Executes fn while capturing all printed output, then returns the captured text.</summary>
    /// <param name="fn">The function to execute</param>
    /// <returns>The captured output as a string</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue CaptureOutput(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var callable = SvArgs.Callable(args, 0, "test.captureOutput");
        var previousOutput = ctx.Output;
        var sw = new StringWriter();
        sw.NewLine = "\n";
        ctx.Output = sw;
        try
        {
            ctx.InvokeCallbackDirect(callable, ReadOnlySpan<StashValue>.Empty);
        }
        finally
        {
            ctx.Output = previousOutput;
        }
        return StashValue.FromObj(sw.ToString());
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

    private static StashValue RunTest(IInterpreterContext ctx, string fullName, IStashCallable body, SourceSpan span)
    {
        var harness = ctx.TestHarness;
        harness?.OnTestStart(fullName, span);
        var sw = Stopwatch.StartNew();

        try
        {
            // Run beforeEach hooks from all describe levels (outermost to innermost)
            foreach (var level in ctx.BeforeEachHooks)
            {
                foreach (var hook in level)
                {
                    ctx.InvokeCallbackDirect(hook, ReadOnlySpan<StashValue>.Empty);
                }
            }

            ctx.InvokeCallbackDirect(body, ReadOnlySpan<StashValue>.Empty);
            sw.Stop();

            // Run afterEach hooks from all describe levels (innermost to outermost)
            for (int i = ctx.AfterEachHooks.Count - 1; i >= 0; i--)
            {
                foreach (var hook in ctx.AfterEachHooks[i])
                {
                    ctx.InvokeCallbackDirect(hook, ReadOnlySpan<StashValue>.Empty);
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

        return StashValue.Null;
    }
}
