using Stash.Bytecode;
using Stash.Debugging;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using CallFrame = Stash.Debugging.CallFrame;

namespace Stash.Tests.Debugging;

public class DebuggingInfrastructureTests
{
    // ── Breakpoint Tests ──────────────────────────────────────────────

    [Fact]
    public void Breakpoint_HasUniqueId()
    {
        var bp1 = new Breakpoint("test.stash", 10);
        var bp2 = new Breakpoint("test.stash", 20);

        Assert.NotEqual(bp1.Id, bp2.Id);
    }

    [Fact]
    public void Breakpoint_TracksHitCount()
    {
        var bp = new Breakpoint("test.stash", 10);

        Assert.Equal(0, bp.HitCount);
        Assert.Equal(1, bp.IncrementHitCount());
        Assert.Equal(2, bp.IncrementHitCount());
        Assert.Equal(2, bp.HitCount);
    }

    [Fact]
    public void Breakpoint_ResetHitCount()
    {
        var bp = new Breakpoint("test.stash", 10);
        bp.IncrementHitCount();
        bp.IncrementHitCount();

        bp.ResetHitCount();

        Assert.Equal(0, bp.HitCount);
    }

    [Fact]
    public void Breakpoint_IsLogpoint_WhenLogMessageSet()
    {
        var bp = new Breakpoint("test.stash", 10);
        Assert.False(bp.IsLogpoint);

        bp.LogMessage = "x = {x}";
        Assert.True(bp.IsLogpoint);
    }

    [Fact]
    public void Breakpoint_DefaultsToVerified()
    {
        var bp = new Breakpoint("test.stash", 10);
        Assert.True(bp.Verified);
    }

    [Fact]
    public void Breakpoint_StoresCondition()
    {
        var bp = new Breakpoint("test.stash", 10) { Condition = "x > 5" };

        Assert.Equal("x > 5", bp.Condition);
    }

    // ── CallFrame Tests ───────────────────────────────────────────────

    [Fact]
    public void CallFrame_HasUniqueId()
    {
        var frame1 = new CallFrame
        {
            CallSite = new SourceSpan("test.stash", 1, 1, 1, 1),
            LocalScope = new TestDebugScope()
        };
        var frame2 = new CallFrame
        {
            CallSite = new SourceSpan("test.stash", 2, 1, 2, 1),
            LocalScope = new TestDebugScope()
        };

        Assert.NotEqual(frame1.Id, frame2.Id);
    }

    [Fact]
    public void CallFrame_DefaultFunctionName_IsScript()
    {
        var frame = new CallFrame
        {
            CallSite = new SourceSpan("test.stash", 1, 1, 1, 1),
            LocalScope = new TestDebugScope()
        };

        Assert.Equal("<script>", frame.FunctionName);
    }

    [Fact]
    public void CallFrame_FunctionSpan_NullByDefault()
    {
        var frame = new CallFrame
        {
            CallSite = new SourceSpan("test.stash", 1, 1, 1, 1),
            LocalScope = new TestDebugScope()
        };

        Assert.Null(frame.FunctionSpan);
    }

    [Fact]
    public void CallFrame_FunctionSpan_CanBeSet()
    {
        var defSpan = new SourceSpan("utils.stash", 10, 1, 15, 2);
        var frame = new CallFrame
        {
            FunctionName = "deploy",
            CallSite = new SourceSpan("main.stash", 5, 1, 5, 15),
            LocalScope = new TestDebugScope(),
            FunctionSpan = defSpan
        };

        Assert.Equal(defSpan, frame.FunctionSpan);
        Assert.Equal("deploy", frame.FunctionName);
    }

    // ── DebugScope Tests ──────────────────────────────────────────────

    [Fact]
    public void DebugScope_ReportsCorrectVariableCount()
    {
        var inner = new TestDebugScope();
        inner.Define("x", 1L);
        inner.Define("y", 2L);
        var scope = new DebugScope(ScopeKind.Local, "Local", inner);

        Assert.Equal(ScopeKind.Local, scope.Kind);
        Assert.Equal("Local", scope.Name);
        Assert.Equal(2, scope.VariableCount);
    }

    [Fact]
    public void DebugScope_EmptyEnvironment_HasZeroVariables()
    {
        var inner = new TestDebugScope();
        var scope = new DebugScope(ScopeKind.Global, "Global", inner);

        Assert.Equal(0, scope.VariableCount);
    }

    // ── PauseReason Tests ─────────────────────────────────────────────

    [Theory]
    [InlineData(PauseReason.Breakpoint)]
    [InlineData(PauseReason.Step)]
    [InlineData(PauseReason.Pause)]
    [InlineData(PauseReason.Exception)]
    [InlineData(PauseReason.Entry)]
    [InlineData(PauseReason.FunctionBreakpoint)]
    public void PauseReason_AllValuesExist(PauseReason reason)
    {
        Assert.True(Enum.IsDefined(reason));
    }

    // ── IDebugger Hook Tests ──────────────────────────────────────────

    [Fact]
    public void VM_CallsDebuggerOnBeforeExecute()
    {
        var debugger = new TestDebugger();
        var lexer = new Lexer("let x = 42;", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        vm.Debugger = debugger;
        vm.Execute(chunk);

        Assert.True(debugger.OnBeforeExecuteCalled);
        Assert.NotNull(debugger.LastSpan);
        Assert.NotNull(debugger.LastEnv);
    }

    [Fact]
    public void VM_CallsDebuggerOnFunctionEnterExit()
    {
        var debugger = new TestDebugger();
        var lexer = new Lexer("fn greet() { let x = 1; } greet();", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        vm.Debugger = debugger;
        vm.Execute(chunk);

        Assert.Contains("greet", debugger.FunctionsEntered);
        Assert.Contains("greet", debugger.FunctionsExited);
    }

    [Fact]
    public void VM_CallsDebuggerOnError()
    {
        var debugger = new TestDebugger();
        var lexer = new Lexer("let x = 1 / 0;", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        vm.Debugger = debugger;

        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
        Assert.True(debugger.OnErrorCalled);
    }

    [Fact]
    public void VM_ExposesGlobals()
    {
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        Assert.NotNull(vm.Globals);
        Assert.True(vm.Globals.ContainsKey("typeof"));
    }

    // ── Test Helpers ──────────────────────────────────────────────────

    private class TestDebugScope : IDebugScope
    {
        private readonly Dictionary<string, object?> _vars = new();
        public IDebugScope? EnclosingScope => null;
        public IEnumerable<KeyValuePair<string, object?>> GetAllBindings() => _vars;
        public void Define(string name, object? value) => _vars[name] = value;
    }

    private class TestDebugger : IDebugger
    {
        public bool OnBeforeExecuteCalled { get; private set; }
        public bool OnErrorCalled { get; private set; }
        public SourceSpan? LastSpan { get; private set; }
        public IDebugScope? LastEnv { get; private set; }
        public List<string> FunctionsEntered { get; } = new();
        public List<string> FunctionsExited { get; } = new();
        public List<string> SourcesLoaded { get; } = new();

        public Action<SourceSpan, IDebugScope>? OnBeforeExecuteCallback { get; set; }
        public Action<string, SourceSpan, IDebugScope>? OnFunctionEnterCallback { get; set; }

        public void OnBeforeExecute(SourceSpan span, IDebugScope env, int threadId)
        {
            OnBeforeExecuteCalled = true;
            LastSpan = span;
            LastEnv = env;
            OnBeforeExecuteCallback?.Invoke(span, env);
        }

        public void OnFunctionEnter(string name, SourceSpan callSite, IDebugScope env, int threadId)
        {
            FunctionsEntered.Add(name);
            OnFunctionEnterCallback?.Invoke(name, callSite, env);
        }

        public void OnFunctionExit(string name, int threadId)
        {
            FunctionsExited.Add(name);
        }

        public bool ShouldBreakOnFunctionEntry(string functionName) => true;

        public void OnError(RuntimeError error, IReadOnlyList<CallFrame> callStack, int threadId)
        {
            OnErrorCalled = true;
        }

        public void OnSourceLoaded(string filePath)
        {
            SourcesLoaded.Add(filePath);
        }
    }
}
