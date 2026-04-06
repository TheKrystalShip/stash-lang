using System.Collections.Generic;
using System.Linq;
using Stash.Bytecode;
using Stash.Common;
using Stash.Debugging;
using Stash.Runtime;
using Xunit;
using DebugCallFrame = Stash.Debugging.CallFrame;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Regression tests for the BuildFrameScope fix:
/// the debug scope must not expose stale/uninitialized stack slots when a
/// breakpoint fires before all locals in a function have been initialized.
///
/// The fix caps the active local count to
///   Math.Min(chunk.LocalCount, Math.Max(0, _sp - frame.BaseSlot))
/// instead of blindly using chunk.LocalCount (the peak allocation count).
///
/// All assertions inspect OnBeforeExecute scopes captured inside function
/// bodies.  Lambda parameters are always on the stack from the first
/// statement, so they serve as a reliable signal that a given scope belongs
/// to the lambda body and not to the enclosing top-level execution.
/// </summary>
public class BuildFrameScopeTests : BytecodeTestBase
{
    // ── Infrastructure ──────────────────────────────────────────────────────

    private static (object? Result, CapturingDebugger Debugger) ExecuteWithDebugger(string source)
    {
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        var debugger = new CapturingDebugger();
        vm.Debugger = debugger;
        object? result = vm.Execute(chunk);
        return (result, debugger);
    }

    /// <summary>
    /// Minimal test debugger that records every OnBeforeExecute scope for
    /// post-execution assertions.
    /// </summary>
    private sealed class CapturingDebugger : IDebugger
    {
        /// <summary>Every (span, scope) pair captured at each statement boundary.</summary>
        public List<(SourceSpan Span, IDebugScope Scope)> BeforeExecute { get; } = new();

        public bool StopOnEntry => false;
        public bool IsPauseRequested => false;

        public void OnBeforeExecute(SourceSpan span, IDebugScope env, int threadId)
            => BeforeExecute.Add((span, env));

        public void OnFunctionEnter(string name, SourceSpan callSite, IDebugScope env, int threadId) { }
        public void OnFunctionExit(string name, int threadId) { }
        public void OnError(RuntimeError error, IReadOnlyList<DebugCallFrame> callStack, int threadId) { }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns only the scope snapshots that belong to the lambda body,
    /// identified by the presence of the supplied parameter name.
    /// Lambda parameters are always live from the function's first statement,
    /// so any scope that contains the param key is definitely an inner scope.
    /// </summary>
    private static List<Dictionary<string, object?>> LambdaBodyScopes(
        CapturingDebugger dbg, string paramName) =>
        dbg.BeforeExecute
           .Where(c => c.Scope.GetAllBindings().Any(kv => kv.Key == paramName))
           .Select(c => c.Scope.GetAllBindings().ToDictionary(kv => kv.Key, kv => kv.Value))
           .ToList();

    // =========================================================================
    // 1. Before a local is declared, only the parameter is visible
    // =========================================================================

    [Fact]
    public void BuildFrameScope_BeforeLocalDeclaration_ScopeContainsOnlyParam()
    {
        // compute has 1 parameter (a) and 1 local (x, declared AFTER the first stmt).
        // chunk.LocalCount == 2, but at the first statement only the arg slot is
        // live (_sp - frame.BaseSlot == 1).
        // Old code: activeLocalCount = 2 → reads the stale x slot → x appears as null.
        // Fix:      activeLocalCount = min(2,1) = 1 → scope has only { a }.
        string source = @"
let compute = null;
compute = (a) => {
    let x = 100;
    return x + a;
};
return compute(42);
";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(142L, result);

        // All lambda-body scopes: the first one is at 'let x = 100;' (before x is
        // pushed), the second at 'return x + a;' (after x is pushed).
        List<Dictionary<string, object?>> innerScopes = LambdaBodyScopes(dbg, "a");
        Assert.True(innerScopes.Count >= 2, "Expected at least two OnBeforeExecute calls inside the lambda");

        // First inner scope — captured before 'let x = 100;' executes.
        // x must not be visible.
        Dictionary<string, object?> preInitScope = innerScopes[0];
        Assert.Single(preInitScope);  // only {a}
        Assert.True(preInitScope.ContainsKey("a"), "Parameter 'a' should be visible from the start");
        Assert.False(preInitScope.ContainsKey("x"), "'x' must not appear before 'let x = 100;' executes");
    }

    // =========================================================================
    // 2. After a local is declared, it appears with the correct value
    // =========================================================================

    [Fact]
    public void BuildFrameScope_AfterLocalDeclaration_LocalAppearsInScope()
    {
        // The OnBeforeExecute call at 'return x + a;' fires after 'let x = 100'
        // has executed, so both 'a' and 'x' must be present with correct values.
        string source = @"
let compute = null;
compute = (a) => {
    let x = 100;
    return x + a;
};
return compute(42);
";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(142L, result);

        // Find the first scope inside the lambda that contains x.
        Dictionary<string, object?>? postInitScope = LambdaBodyScopes(dbg, "a")
            .FirstOrDefault(s => s.ContainsKey("x"));

        Assert.NotNull(postInitScope);
        Assert.Equal(100L, postInitScope["x"]);
        Assert.Equal(42L, postInitScope["a"]);
    }

    // =========================================================================
    // 3. Paramless function: no stale locals before the declaration
    // =========================================================================

    [Fact]
    public void BuildFrameScope_Paramless_NoStaleValueBeforeLocalInit()
    {
        // compute has no parameters and one local (x).
        // chunk.LocalCount == 1.  At the first statement _sp - frame.BaseSlot == 0,
        // so the old code would expose {x: null} (the uninitialized slot).
        // With the fix, x must never appear before its initializer runs.
        string source = @"
let compute = null;
compute = () => {
    let x = 99;
    return x;
};
return compute();
";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(99L, result);

        // 'x' should never appear with a null (stale) value in any scope anywhere.
        bool hasStaleX = dbg.BeforeExecute
            .Any(c => c.Scope.GetAllBindings().Any(kv => kv.Key == "x" && kv.Value is null));
        Assert.False(hasStaleX, "'x' appeared with a null (stale) value — uninitialized local was exposed");

        // Sanity check: 'x' with value 99 must have been seen (function ran).
        bool hasInitializedX = dbg.BeforeExecute
            .Any(c => c.Scope.GetAllBindings().Any(kv => kv.Key == "x" && Equals(kv.Value, 99L)));
        Assert.True(hasInitializedX, "'x' should be visible with value 99 after initialization");
    }

    // =========================================================================
    // 4. For-in loop: only the parameter visible at the first statement
    // =========================================================================

    [Fact]
    public void BuildFrameScope_ForInLoop_FirstStatementScopeHasOnlyParam()
    {
        // A for-in loop inside the function causes chunk.LocalCount to be 3
        // (a + <iter> + item).  After the loop 'extra' reuses the iterator slot
        // so the peak stays at 3.
        //
        // At the very first statement of the lambda body (the 'for' statement
        // itself, before the iterable is evaluated or any loop locals are pushed),
        // _sp - frame.BaseSlot == 1.
        //
        // Old behaviour: activeLocalCount = chunk.LocalCount = 3 → stale slots 1 and 2 exposed.
        // Fix:           activeLocalCount = min(3, 1) = 1 → only { a } shown.
        string source = @"
let compute = null;
compute = (a) => {
    for (let item in [1, 2, 3]) {
        item;
    }
    let extra = a + 10;
    return extra;
};
return compute(5);
";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(15L, result);

        // Lambda body scopes: identified by presence of 'a'.
        List<Dictionary<string, object?>> innerScopes = LambdaBodyScopes(dbg, "a");
        Assert.True(innerScopes.Count >= 1, "Expected at least one OnBeforeExecute call inside the lambda");

        // First inner scope — captured at the 'for' statement before any loop
        // locals have been pushed.  Only the parameter should be visible.
        Dictionary<string, object?> firstScope = innerScopes[0];
        Assert.Single(firstScope);
        Assert.True(firstScope.ContainsKey("a"), "Parameter 'a' should be visible at the first statement");
        Assert.Equal(5L, firstScope["a"]);
    }

    // =========================================================================
    // 5. Multiple locals: scope grows incrementally as each local is initialized
    // =========================================================================

    [Fact]
    public void BuildFrameScope_MultipleLocals_ScopeGrowsIncrementally()
    {
        // compute has 2 params (a, b) and 2 locals (sum, result).
        // chunk.LocalCount == 4.  Without the fix every scope inside the function
        // would show 4 bindings, with stale null values for as-yet-uninitialized
        // locals.  With the fix the binding count grows: 2 → 3 → 4 as each local
        // is pushed.
        string source = @"
let compute = null;
compute = (a, b) => {
    let sum = a + b;
    let result = sum * 2;
    return result;
};
return compute(3, 4);
";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(14L, result);

        // Lambda-body scopes: all OnBeforeExecute calls where 'a' is present.
        List<Dictionary<string, object?>> innerScopes = LambdaBodyScopes(dbg, "a");
        Assert.True(innerScopes.Count >= 3,
            "Expected at least 3 inner scopes: at 'let sum', 'let result', 'return result'");

        // Scope 0 — at 'let sum = a + b;': only a and b visible.
        Dictionary<string, object?> s0 = innerScopes[0];
        Assert.Equal(2, s0.Count);
        Assert.True(s0.ContainsKey("a") && s0.ContainsKey("b"), "Scope 0 should contain {a, b}");
        Assert.False(s0.ContainsKey("sum"), "'sum' must not be visible before its initializer");
        Assert.False(s0.ContainsKey("result"), "'result' must not be visible before its initializer");

        // Scope 1 — at 'let result = sum * 2;': a, b, and sum visible.
        Dictionary<string, object?> s1 = innerScopes[1];
        Assert.Equal(3, s1.Count);
        Assert.True(s1.ContainsKey("sum"), "'sum' should be visible after initialization");
        Assert.False(s1.ContainsKey("result"), "'result' must not be visible before its initializer");

        // Scope 2 — at 'return result;': all locals visible.
        Dictionary<string, object?> s2 = innerScopes[2];
        Assert.Equal(4, s2.Count);
        Assert.True(s2.ContainsKey("result"), "'result' should be visible after initialization");
        Assert.Equal(14L, s2["result"]);
    }
}
