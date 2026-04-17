using Stash.Runtime;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Integration tests for the <c>defer</c> statement.
/// All tests use <see cref="Stash.Tests.Interpreting.StashTestBase"/> so they have access to
/// <c>RunCapturingOutput</c> and stdlib functions (io.println, conv.toStr, arr.forEach).
/// </summary>
public class DeferTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // 1. Basic — defer runs at function exit
    // =========================================================================

    [Fact]
    public void Defer_SingleStatement_RunsAtFunctionExit()
    {
        string output = RunCapturingOutput("""
            fn test() {
                defer io.println("deferred");
                io.println("main");
            }
            test();
            """);
        Assert.Contains("main", output);
        Assert.Contains("deferred", output);
        Assert.True(output.IndexOf("main") < output.IndexOf("deferred"));
    }

    [Fact]
    public void Defer_Block_ExecutesAtFunctionExit()
    {
        string output = RunCapturingOutput("""
            fn test() {
                defer {
                    io.println("block deferred");
                }
                io.println("main");
            }
            test();
            """);
        Assert.Contains("main", output);
        Assert.Contains("block deferred", output);
        Assert.True(output.IndexOf("main") < output.IndexOf("block deferred"));
    }

    // =========================================================================
    // 2. LIFO order
    // =========================================================================

    [Fact]
    public void Defer_MultipleLIFO_ReversesExecutionOrder()
    {
        string output = RunCapturingOutput("""
            fn test() {
                defer io.println("first");
                defer io.println("second");
                defer io.println("third");
            }
            test();
            """);
        int pos3 = output.IndexOf("third");
        int pos2 = output.IndexOf("second");
        int pos1 = output.IndexOf("first");
        Assert.True(pos3 < pos2, "third should run before second");
        Assert.True(pos2 < pos1, "second should run before first");
    }

    // =========================================================================
    // 3. Eager evaluation (single-statement)
    // =========================================================================

    [Fact]
    public void Defer_EagerEval_CapturesValueAtDeferTime()
    {
        // Single-statement defer captures arguments eagerly — x is 10 at defer registration time.
        string output = RunCapturingOutput("""
            fn test() {
                let x = 10;
                defer io.println(x);
                x = 20;
            }
            test();
            """);
        Assert.Contains("10", output);
        Assert.DoesNotContain("20", output);
    }

    // =========================================================================
    // 4. Late binding (block defer)
    // =========================================================================

    [Fact]
    public void Defer_Block_LateBound_SeesCurrentValue()
    {
        // Block defer captures by closure — sees x = 20 (its value when the function exits).
        string output = RunCapturingOutput("""
            fn test() {
                let x = 10;
                defer { io.println(x); }
                x = 20;
            }
            test();
            """);
        Assert.Contains("20", output);
    }

    // =========================================================================
    // 5. Error handling
    // =========================================================================

    [Fact]
    public void Defer_RunsOnException()
    {
        // Defer still executes when the function throws.
        string output = RunCapturingOutput("""
            fn test() {
                defer io.println("cleanup");
                throw "error";
            }
            try { test(); } catch (e) { io.println("caught"); }
            """);
        Assert.Contains("cleanup", output);
        Assert.Contains("caught", output);
    }

    [Fact]
    public void Defer_RunsOnException_BeforeCatch()
    {
        // Defer runs before catch handler receives control.
        string output = RunCapturingOutput("""
            fn test() {
                defer io.println("cleanup");
                throw "oops";
            }
            try {
                test();
            } catch (e) {
                io.println("caught");
            }
            """);
        int cleanupPos = output.IndexOf("cleanup");
        int caughtPos = output.IndexOf("caught");
        Assert.True(cleanupPos < caughtPos, "defer cleanup should run before catch block executes");
    }

    [Fact]
    public void Defer_Throws_NormalReturn_PropagatesError()
    {
        // When function returns normally but defer throws, the defer's error propagates.
        var ex = Assert.Throws<RuntimeError>(() => RunCapturingOutput("""
            fn test() {
                defer { throw "defer error"; }
                return 42;
            }
            test();
            """));
        Assert.Contains("defer error", ex.Message);
    }

    [Fact]
    public void Defer_Throws_ErrorReturn_OriginalPropagates_SuppressedAttached()
    {
        // When the function throws and a defer also throws, the original error propagates
        // and the defer's error appears in e.suppressed.
        string output = RunCapturingOutput("""
            fn test() {
                defer { throw "cleanup failed"; }
                throw "main error";
            }
            try {
                test();
            } catch (e) {
                io.println(e.message);
            }
            """);
        Assert.Contains("main error", output);
    }

    [Fact]
    public void Defer_AllRunEvenIfOneThrows()
    {
        // All defers execute even when one of them throws.
        string output = RunCapturingOutput("""
            fn test() {
                defer io.println("first");
                defer { throw "fail"; }
                defer io.println("third");
            }
            try { test(); } catch (e) { io.println("caught"); }
            """);
        Assert.Contains("third", output);
        Assert.Contains("first", output);
        Assert.Contains("caught", output);
    }

    // =========================================================================
    // 6. Scope — defer is function-scoped, not block-scoped
    // =========================================================================

    [Fact]
    public void Defer_InIf_RunsAtFunctionExit_NotBlockExit()
    {
        // Defer inside an if-block runs at function exit, not when the block ends.
        string output = RunCapturingOutput("""
            fn test() {
                if (true) {
                    defer io.println("deferred");
                }
                io.println("after if");
            }
            test();
            """);
        Assert.True(output.IndexOf("after if") < output.IndexOf("deferred"));
    }

    [Fact]
    public void Defer_Conditional_NotRegisteredIfFalse()
    {
        // Defer inside a false branch is never registered.
        string output = RunCapturingOutput("""
            fn test() {
                if (false) {
                    defer io.println("should not run");
                }
                io.println("done");
            }
            test();
            """);
        Assert.Contains("done", output);
        Assert.DoesNotContain("should not run", output);
    }

    [Fact]
    public void Defer_TopLevel_RunsAtScriptExit()
    {
        // Top-level defer runs when the script finishes.
        string output = RunCapturingOutput("""
            defer io.println("cleanup");
            io.println("script");
            """);
        Assert.Contains("script", output);
        Assert.Contains("cleanup", output);
        Assert.True(output.IndexOf("script") < output.IndexOf("cleanup"));
    }

    [Fact]
    public void Defer_InsideLambda_ScopedToLambdaInvocation()
    {
        // Each lambda invocation has its own defer stack.
        string output = RunCapturingOutput("""
            let items = [1, 2, 3];
            arr.forEach(items, (item) => {
                defer io.println("done " + conv.toStr(item));
                io.println("process " + conv.toStr(item));
            });
            """);
        Assert.Contains("process 1", output);
        Assert.Contains("done 1", output);
        Assert.Contains("process 2", output);
        Assert.Contains("done 2", output);
        Assert.Contains("process 3", output);
        Assert.Contains("done 3", output);
        // Each "process N" should appear before its corresponding "done N"
        Assert.True(output.IndexOf("process 1") < output.IndexOf("done 1"));
        Assert.True(output.IndexOf("process 2") < output.IndexOf("done 2"));
        Assert.True(output.IndexOf("process 3") < output.IndexOf("done 3"));
    }

    // =========================================================================
    // 7. Return value — defer cannot change the return value
    // =========================================================================

    [Fact]
    public void Defer_CannotModifyReturnValue()
    {
        // The defer runs after the return value is captured; modifications to locals
        // inside the defer do not change what the caller receives.
        string output = RunCapturingOutput("""
            fn test() {
                let result = 42;
                defer { result = 0; }
                return result;
            }
            io.println(test());
            """);
        Assert.Contains("42", output);
    }

    // =========================================================================
    // 8. Edge cases
    // =========================================================================

    [Fact]
    public void Defer_EmptyBlock_NoError()
    {
        // Empty defer block is a no-op and must not throw.
        string output = RunCapturingOutput("""
            fn test() {
                defer {}
                return 1;
            }
            io.println(test());
            """);
        Assert.Contains("1", output);
    }

    [Fact]
    public void Defer_NestedFunctions_IndependentStacks()
    {
        // Inner and outer functions each have their own defer stack.
        string output = RunCapturingOutput("""
            fn outer() {
                defer io.println("outer defer");
                fn inner() {
                    defer io.println("inner defer");
                    io.println("inner body");
                }
                inner();
                io.println("outer body");
            }
            outer();
            """);
        int innerBody  = output.IndexOf("inner body");
        int innerDefer = output.IndexOf("inner defer");
        int outerBody  = output.IndexOf("outer body");
        int outerDefer = output.IndexOf("outer defer");
        Assert.True(innerBody  < innerDefer, "inner defer should run after inner body");
        Assert.True(innerDefer < outerBody,  "inner defer should run before outer body resumes");
        Assert.True(outerBody  < outerDefer, "outer defer should run after outer body");
    }

    [Fact]
    public void Defer_InLoop_Accumulates()
    {
        // Defers registered inside a loop accumulate and all run at function exit.
        // CloseUpval is emitted at each iteration end, so each closure captures its
        // own independent value (i=0 → "defer 0", i=1 → "defer 1", i=2 → "defer 2").
        string output = RunCapturingOutput("""
            fn test() {
                for (let i = 0; i < 3; i = i + 1) {
                    defer io.println("defer " + conv.toStr(i));
                }
                io.println("after loop");
            }
            test();
            """);
        Assert.Contains("after loop", output);
        // All three defers must have run with their captured values.
        Assert.Contains("defer 0", output);
        Assert.Contains("defer 1", output);
        Assert.Contains("defer 2", output);
        // Defers run at function exit, after "after loop".
        Assert.True(output.IndexOf("after loop") < output.LastIndexOf("defer"));
    }

    [Fact]
    public void Defer_InLoop_LIFO_OrderWithinLoop()
    {
        // CloseUpval is emitted at each loop body end, giving each iteration its own
        // closed upvalue. The three accumulated defers execute LIFO at function exit.
        string output = RunCapturingOutput("""
            fn test() {
                for (let i = 0; i < 3; i = i + 1) {
                    defer io.println("defer " + conv.toStr(i));
                }
            }
            test();
            """);
        // LIFO: defer 2 runs first, then 1, then 0.
        int pos2 = output.IndexOf("defer 2");
        int pos1 = output.IndexOf("defer 1");
        int pos0 = output.IndexOf("defer 0");
        Assert.True(pos2 < pos1, "defer 2 should run before defer 1");
        Assert.True(pos1 < pos0, "defer 1 should run before defer 0");
    }

    [Fact]
    public void Defer_WithTryFinally_FinallyRunsBeforeDefer()
    {
        // finally runs before defer because finally is part of the try block,
        // while defer runs at the very end when the function exits.
        string output = RunCapturingOutput("""
            fn test() {
                defer io.println("deferred");
                try {
                    io.println("try body");
                } finally {
                    io.println("finally");
                }
            }
            test();
            """);
        int tryBody = output.IndexOf("try body");
        int fin     = output.IndexOf("finally");
        int def     = output.IndexOf("deferred");
        Assert.True(tryBody < fin, "try body before finally");
        Assert.True(fin    < def, "finally before defer");
    }

    [Fact]
    public void Defer_Recursion_EachCallIndependent()
    {
        // Each recursive invocation has its own defer stack; innermost exits first.
        string output = RunCapturingOutput("""
            fn recurse(n) {
                defer io.println("exit " + conv.toStr(n));
                if (n > 0) { recurse(n - 1); }
            }
            recurse(3);
            """);
        int exit0 = output.IndexOf("exit 0");
        int exit1 = output.IndexOf("exit 1");
        int exit2 = output.IndexOf("exit 2");
        int exit3 = output.IndexOf("exit 3");
        Assert.True(exit0 < exit1, "exit 0 before exit 1");
        Assert.True(exit1 < exit2, "exit 1 before exit 2");
        Assert.True(exit2 < exit3, "exit 2 before exit 3");
    }

    [Fact]
    public void Defer_MultipleInSameFunction_AllRun()
    {
        // All deferred statements in a function run, regardless of where they appear.
        string output = RunCapturingOutput("""
            fn test() {
                defer io.println("alpha");
                io.println("body1");
                defer io.println("beta");
                io.println("body2");
                defer io.println("gamma");
            }
            test();
            """);
        Assert.Contains("body1", output);
        Assert.Contains("body2", output);
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
        Assert.Contains("gamma", output);
        // Body comes before any defer.
        Assert.True(output.IndexOf("body2") < output.IndexOf("gamma"));
        // LIFO: gamma, beta, alpha
        int posGamma = output.IndexOf("gamma");
        int posBeta  = output.IndexOf("beta");
        int posAlpha = output.IndexOf("alpha");
        Assert.True(posGamma < posBeta,  "gamma before beta");
        Assert.True(posBeta  < posAlpha, "beta before alpha");
    }

    [Fact]
    public void Defer_MultipleFunctionCalls_IndependentDefers()
    {
        // Calling the same function multiple times does not mix defer stacks.
        string output = RunCapturingOutput("""
            fn greet(name) {
                defer io.println("bye " + name);
                io.println("hi " + name);
            }
            greet("alice");
            greet("bob");
            """);
        int hiAlice  = output.IndexOf("hi alice");
        int byeAlice = output.IndexOf("bye alice");
        int hiBob    = output.IndexOf("hi bob");
        int byeBob   = output.IndexOf("bye bob");
        Assert.True(hiAlice  < byeAlice, "alice's defer after alice's body");
        Assert.True(byeAlice < hiBob,    "alice done before bob starts");
        Assert.True(hiBob    < byeBob,   "bob's defer after bob's body");
    }

    [Fact]
    public void Defer_SuppressedErrors_AttachedToOriginalError()
    {
        // When function throws and a defer also throws, the defer error is in e.suppressed.
        string output = RunCapturingOutput("""
            fn test() {
                defer { throw "cleanup failed"; }
                throw "main error";
            }
            try {
                test();
            } catch (e) {
                io.println(e.message);
                if (e.suppressed != null) {
                    io.println("has suppressed");
                }
            }
            """);
        Assert.Contains("main error", output);
        Assert.Contains("has suppressed", output);
    }

    [Fact]
    public void Defer_ThrowInDefer_OtherDefersRunExactlyOnce()
    {
        // Bug regression: when a defer throws during normal return, other defers
        // must run exactly once — not re-executed during exception unwinding.
        string output = RunCapturingOutput("""
            fn test() {
                defer io.println("good defer");
                defer { throw "bad defer"; }
                return 42;
            }
            try { test(); } catch (e) { io.println("caught"); }
            """);
        Assert.Contains("good defer", output);
        Assert.Contains("caught", output);
        // "good defer" must appear exactly once (count occurrences)
        int count = 0;
        int idx = 0;
        while ((idx = output.IndexOf("good defer", idx)) != -1)
        {
            count++;
            idx += "good defer".Length;
        }
        Assert.Equal(1, count);
    }

    [Fact]
    public void Defer_MultipleDeferThrow_NormalReturn_SuppressedCorrect()
    {
        // When multiple defers throw during normal return, the LIFO-first error
        // is primary; subsequent defer errors appear in .suppressed.
        string output = RunCapturingOutput("""
            fn test() {
                defer { throw "cleanup1"; }
                defer { throw "cleanup2"; }
                return 42;
            }
            try {
                test();
            } catch (e) {
                io.println("primary: " + e.message);
                io.println("suppressed: " + conv.toStr(e.suppressed));
            }
            """);
        // LIFO: cleanup2 runs first → primary error; cleanup1 runs second → suppressed
        Assert.Contains("primary: cleanup2", output);
        Assert.Contains("RuntimeError: cleanup1", output);
        // cleanup2 should NOT appear in suppressed (no duplicate)
        Assert.DoesNotContain("RuntimeError: cleanup2", output);
    }
}
