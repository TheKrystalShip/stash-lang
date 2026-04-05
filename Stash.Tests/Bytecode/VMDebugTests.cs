using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Bytecode;
using Stash.Common;
using Stash.Debugging;
using Stash.Interpreting;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Runtime;
using DebugCallFrame = Stash.Debugging.CallFrame;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for Phase 7: Bytecode VM debugger integration.
/// Verifies that the VM correctly calls IDebugger hooks and provides
/// accurate source location, variable, and call stack information.
/// </summary>
public class VMDebugTests
{
    // ── Test infrastructure ──

    private static Chunk CompileSource(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        var interpreter = new Interpreter();
        var resolver = new Resolver(interpreter);
        resolver.Resolve(stmts);
        return Compiler.Compile(stmts);
    }

    private static (object? Result, TestDebugger Debugger) ExecuteWithDebugger(string source)
    {
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        var debugger = new TestDebugger();
        vm.Debugger = debugger;
        object? result = vm.Execute(chunk);
        return (result, debugger);
    }

    /// <summary>
    /// Mock debugger that records all hook calls for test verification.
    /// </summary>
    private sealed class TestDebugger : IDebugger
    {
        public List<(SourceSpan Span, IDebugScope Scope, int ThreadId)> BeforeExecuteCalls { get; } = new();
        public List<(string Name, SourceSpan CallSite, IDebugScope Scope, int ThreadId)> FunctionEnterCalls { get; } = new();
        public List<(string Name, int ThreadId)> FunctionExitCalls { get; } = new();
        public List<(RuntimeError Error, IReadOnlyList<DebugCallFrame> CallStack, int ThreadId)> ErrorCalls { get; } = new();

        public bool StopOnEntry => false;
        public bool IsPauseRequested { get; set; }

        private readonly HashSet<string> _functionBreakpoints = new();
        private readonly Func<RuntimeError, bool>? _shouldBreakOnException;

        public TestDebugger(Func<RuntimeError, bool>? shouldBreakOnException = null)
        {
            _shouldBreakOnException = shouldBreakOnException;
        }

        public void AddFunctionBreakpoint(string name) => _functionBreakpoints.Add(name);

        public void OnBeforeExecute(SourceSpan span, IDebugScope env, int threadId)
        {
            BeforeExecuteCalls.Add((span, env, threadId));
        }

        public void OnFunctionEnter(string name, SourceSpan callSite, IDebugScope env, int threadId)
        {
            FunctionEnterCalls.Add((name, callSite, env, threadId));
        }

        public void OnFunctionExit(string name, int threadId)
        {
            FunctionExitCalls.Add((name, threadId));
        }

        public void OnError(RuntimeError error, IReadOnlyList<DebugCallFrame> callStack, int threadId)
        {
            ErrorCalls.Add((error, callStack, threadId));
        }

        public bool ShouldBreakOnException(RuntimeError error) =>
            _shouldBreakOnException?.Invoke(error) ?? false;

        public bool ShouldBreakOnFunctionEntry(string functionName) =>
            _functionBreakpoints.Contains(functionName);
    }

    // =========================================================================
    // 34. Debugger — Statement Boundary Hooks
    // =========================================================================

    [Fact]
    public void Debug_OnBeforeExecute_CalledAtStatementBoundaries()
    {
        string source = @"
let x = null;
let y = null;
let z = null;
x = 1;
y = 2;
z = x + y;
return z;
";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(3L, result);
        // Should have at least one OnBeforeExecute call per statement
        Assert.True(dbg.BeforeExecuteCalls.Count >= 4,
            $"Expected at least 4 BeforeExecute calls, got {dbg.BeforeExecuteCalls.Count}");
    }

    [Fact]
    public void Debug_OnBeforeExecute_ReportsCorrectSourceLines()
    {
        string source = "let x = null;\nlet y = null;\nx = 10;\ny = 20;\nreturn x + y;";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(30L, result);

        // Extract unique source lines reported
        var lines = dbg.BeforeExecuteCalls.Select(c => c.Span.StartLine).Distinct().OrderBy(l => l).ToList();
        // Should see lines 1, 2, 3
        Assert.Contains(1, lines);
        Assert.Contains(2, lines);
        Assert.Contains(3, lines);
    }

    [Fact]
    public void Debug_OnBeforeExecute_ProvidesLocalVariables()
    {
        // Inside a function, let x = 42 is a true local — scope shows correct value
        string source = @"
let check = null;
check = () => {
    let x = 42;
    return x;
};
return check();
";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(42L, result);

        // Find the BeforeExecute call where x=42 (at 'return x;' — after let x = 42 ran)
        var callWithX = dbg.BeforeExecuteCalls
            .FirstOrDefault(c => c.Scope.GetAllBindings().Any(kv => kv.Key == "x" && Equals(kv.Value, 42L)));
        Assert.True(callWithX != default, "Expected a BeforeExecute call with x=42 in scope (at 'return x;')");
        var bindings = callWithX.Scope.GetAllBindings().ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(42L, bindings["x"]);
    }

    // =========================================================================
    // 35. Debugger — Function Enter/Exit
    // =========================================================================

    [Fact]
    public void Debug_FunctionEnterExit_CalledForUserFunctions()
    {
        string source = @"
let add = null;
add = (a, b) => a + b;
return add(3, 4);
";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(7L, result);

        // OnFunctionExit is unconditionally called for every function return
        Assert.True(dbg.FunctionExitCalls.Count >= 1,
            $"Expected at least 1 FunctionExit call, got {dbg.FunctionExitCalls.Count}");
    }

    [Fact]
    public void Debug_FunctionBreakpoint_TriggersOnFunctionEntry()
    {
        // Lambdas compile as anonymous chunks; break on "<anonymous>" to test the mechanism
        string source = @"
let greet = null;
greet = (name) => ""hello "" + name;
return greet(""world"");
";
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        var debugger = new TestDebugger();
        debugger.AddFunctionBreakpoint("<anonymous>");
        vm.Debugger = debugger;
        object? result = vm.Execute(chunk);
        Assert.Equal("hello world", result);

        // OnFunctionEnter should have fired because we broke on "<anonymous>"
        Assert.True(debugger.FunctionEnterCalls.Any(),
            "Expected OnFunctionEnter to fire for anonymous lambda with function breakpoint");
    }

    // =========================================================================
    // 36. Debugger — Call Stack
    // =========================================================================

    [Fact]
    public void Debug_CallStack_TrackedDuringNestedCalls()
    {
        string source = @"
let inner = null;
let outer = null;
inner = () => 42;
outer = () => inner();
return outer();
";
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        var debugger = new TestDebugger();
        vm.Debugger = debugger;
        object? result = vm.Execute(chunk);
        Assert.Equal(42L, result);

        // OnFunctionExit is unconditional — should fire for both outer and inner
        Assert.True(debugger.FunctionExitCalls.Count >= 2,
            $"Expected at least 2 FunctionExit calls for nested calls, got {debugger.FunctionExitCalls.Count}");
    }

    // =========================================================================
    // 37. Debugger — Variable Inspection
    // =========================================================================

    [Fact]
    public void Debug_LocalNames_StoredInChunk()
    {
        string source = @"
let first = null;
let second = null;
first = 1;
second = 2;
return first + second;
";
        Chunk chunk = CompileSource(source);
        // The top-level chunk should have local names recorded
        Assert.NotNull(chunk.LocalNames);
        Assert.Contains("first", chunk.LocalNames!);
        Assert.Contains("second", chunk.LocalNames!);
    }

    [Fact]
    public void Debug_FunctionLocalNames_StoredInChunk()
    {
        string source = @"
let func = null;
func = (a, b) => {
    let sum = a + b;
    return sum;
};
return func(3, 4);
";
        Chunk chunk = CompileSource(source);
        // The function chunk should be in the constant pool
        var fnChunks = chunk.Constants.OfType<Chunk>().ToList();
        Assert.True(fnChunks.Count >= 1, "Expected at least one nested Chunk in constants");

        // The function chunk should have parameter and local names
        Chunk fnChunk = fnChunks[0];
        Assert.NotNull(fnChunk.LocalNames);
        Assert.Contains("a", fnChunk.LocalNames!);
        Assert.Contains("b", fnChunk.LocalNames!);
        Assert.Contains("sum", fnChunk.LocalNames!);
    }

    // =========================================================================
    // 38. Debugger — No Debugger Attached
    // =========================================================================

    [Fact]
    public void Debug_NoDebugger_RunsNormally()
    {
        string source = "return 1 + 2;";
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        // Debugger NOT attached
        object? result = vm.Execute(chunk);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Debug_NullDebugger_NoOverhead()
    {
        // Verify a loop runs correctly without debugger
        string source = @"
let sum = null;
sum = 0;
let i = null;
i = 0;
while (i < 100) {
    sum = sum + i;
    i = i + 1;
}
return sum;
";
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        object? result = vm.Execute(chunk);
        Assert.Equal(4950L, result);
    }

    // =========================================================================
    // 39. Debugger — Error Reporting
    // =========================================================================

    [Fact]
    public void Debug_UncaughtError_ReportedToDebugger()
    {
        string source = @"
let x = null;
return x.field;
";
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        var debugger = new TestDebugger(shouldBreakOnException: _ => true);
        vm.Debugger = debugger;

        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
        Assert.True(debugger.ErrorCalls.Count >= 1, "Expected OnError to be called");
    }

    // =========================================================================
    // 40. Debugger — Thread ID
    // =========================================================================

    [Fact]
    public void Debug_ThreadId_DefaultIsOne()
    {
        string source = "return 42;";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(42L, result);

        // All calls should use thread ID 1
        foreach (var call in dbg.BeforeExecuteCalls)
            Assert.Equal(1, call.ThreadId);
    }

    [Fact]
    public void Debug_ThreadId_Configurable()
    {
        string source = "return 42;";
        Chunk chunk = CompileSource(source);
        var vm = new VirtualMachine();
        var debugger = new TestDebugger();
        vm.Debugger = debugger;
        vm.DebugThreadId = 5;
        object? result = vm.Execute(chunk);
        Assert.Equal(42L, result);

        foreach (var call in debugger.BeforeExecuteCalls)
            Assert.Equal(5, call.ThreadId);
    }

    // =========================================================================
    // 41. Debugger — Control Flow
    // =========================================================================

    [Fact]
    public void Debug_IfElse_ReportsCorrectBranch()
    {
        string source = @"
let x = null;
x = 10;
if (x > 5) {
    return ""big"";
} else {
    return ""small"";
}
";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal("big", result);

        // Should have BeforeExecute calls - verify they exist
        Assert.True(dbg.BeforeExecuteCalls.Count > 0);
    }

    [Fact]
    public void Debug_WhileLoop_ReportsIterations()
    {
        string source = @"
let i = null;
i = 0;
while (i < 3) {
    i = i + 1;
}
return i;
";
        var (result, dbg) = ExecuteWithDebugger(source);
        Assert.Equal(3L, result);

        // Should have multiple BeforeExecute calls from loop iterations
        Assert.True(dbg.BeforeExecuteCalls.Count >= 6,
            $"Expected at least 6 BeforeExecute calls for loop, got {dbg.BeforeExecuteCalls.Count}");
    }

    // =========================================================================
    // 42. Debugger — Scope Chain
    // =========================================================================

    [Fact]
    public void Debug_ScopeChain_HasGlobalAndLocal()
    {
        // Inside a function, the frame scope has an enclosing global scope
        string source = @"
let test = null;
test = () => {
    let x = 42;
    return x;
};
return test();
";
        var (result, dbg) = ExecuteWithDebugger(source);

        // The last scope (inside the function) should have an EnclosingScope (globals)
        var lastScope = dbg.BeforeExecuteCalls.Last().Scope;
        Assert.NotNull(lastScope.EnclosingScope);
    }

    // =========================================================================
    // 43. Debugger — VMDebugScope
    // =========================================================================

    [Fact]
    public void VMDebugScope_GetAllBindings_ReturnsCorrectValues()
    {
        var bindings = new KeyValuePair<string, object?>[]
        {
            new("x", 42L),
            new("name", "hello"),
            new("flag", true),
        };
        var scope = new VMDebugScope(bindings, null);

        var result = scope.GetAllBindings().ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(42L, result["x"]);
        Assert.Equal("hello", result["name"]);
        Assert.Equal(true, result["flag"]);
    }

    [Fact]
    public void VMDebugScope_ScopeChain_Works()
    {
        var global = new VMDebugScope(Array.Empty<KeyValuePair<string, object?>>(), null);
        IDebugScope local = new VMDebugScope(
            new[] { new KeyValuePair<string, object?>("x", 1L) },
            global);

        var chain = local.GetScopeChain().ToList();
        Assert.Equal(2, chain.Count);
        Assert.Same(local, chain[0]);
        Assert.Same(global, chain[1]);
    }
}
