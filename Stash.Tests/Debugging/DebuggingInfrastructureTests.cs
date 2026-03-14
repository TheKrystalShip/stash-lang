using Stash.Debugging;
using Stash.Interpreting;
using Stash.Interpreting.Types;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Environment = Stash.Interpreting.Environment;

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
            LocalScope = new Environment()
        };
        var frame2 = new CallFrame
        {
            CallSite = new SourceSpan("test.stash", 2, 1, 2, 1),
            LocalScope = new Environment()
        };

        Assert.NotEqual(frame1.Id, frame2.Id);
    }

    [Fact]
    public void CallFrame_DefaultFunctionName_IsScript()
    {
        var frame = new CallFrame
        {
            CallSite = new SourceSpan("test.stash", 1, 1, 1, 1),
            LocalScope = new Environment()
        };

        Assert.Equal("<script>", frame.FunctionName);
    }

    [Fact]
    public void CallFrame_FunctionSpan_NullByDefault()
    {
        var frame = new CallFrame
        {
            CallSite = new SourceSpan("test.stash", 1, 1, 1, 1),
            LocalScope = new Environment()
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
            LocalScope = new Environment(),
            FunctionSpan = defSpan
        };

        Assert.Equal(defSpan, frame.FunctionSpan);
        Assert.Equal("deploy", frame.FunctionName);
    }

    // ── DebugScope Tests ──────────────────────────────────────────────

    [Fact]
    public void DebugScope_ReportsCorrectVariableCount()
    {
        var env = new Environment();
        env.Define("x", 1L);
        env.Define("y", 2L);

        var scope = new DebugScope(ScopeKind.Local, "Local", env);

        Assert.Equal(ScopeKind.Local, scope.Kind);
        Assert.Equal("Local", scope.Name);
        Assert.Equal(2, scope.VariableCount);
    }

    [Fact]
    public void DebugScope_EmptyEnvironment_HasZeroVariables()
    {
        var env = new Environment();
        var scope = new DebugScope(ScopeKind.Global, "Global", env);

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
    public void Interpreter_CallsDebuggerOnBeforeExecute()
    {
        var debugger = new TestDebugger();
        var interpreter = new Interpreter { Debugger = debugger };

        var source = "let x = 42;";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        interpreter.Interpret(stmts);

        Assert.True(debugger.OnBeforeExecuteCalled);
        Assert.NotNull(debugger.LastSpan);
        Assert.NotNull(debugger.LastEnv);
    }

    [Fact]
    public void Interpreter_CallsDebuggerOnFunctionEnterExit()
    {
        var debugger = new TestDebugger();
        var interpreter = new Interpreter { Debugger = debugger };

        var source = "fn greet() { let x = 1; } greet();";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        interpreter.Interpret(stmts);

        Assert.Contains("greet", debugger.FunctionsEntered);
        Assert.Contains("greet", debugger.FunctionsExited);
    }

    [Fact]
    public void Interpreter_CallsDebuggerOnError()
    {
        var debugger = new TestDebugger();
        var interpreter = new Interpreter { Debugger = debugger };

        var source = "break;";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        Assert.Throws<RuntimeError>(() => interpreter.Interpret(stmts));
        Assert.True(debugger.OnErrorCalled);
    }

    [Fact]
    public void Interpreter_TracksLoadedSources()
    {
        var interpreter = new Interpreter();
        interpreter.CurrentFile = "/tmp/test.stash";

        var source = "let x = 1;";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        interpreter.Interpret(stmts);

        Assert.Contains("/tmp/test.stash", interpreter.LoadedSources);
    }

    [Fact]
    public void Interpreter_NotifiesDebuggerOnSourceLoaded()
    {
        var debugger = new TestDebugger();
        var interpreter = new Interpreter { Debugger = debugger };
        interpreter.CurrentFile = "/tmp/test.stash";

        var source = "let x = 1;";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        interpreter.Interpret(stmts);

        Assert.Contains("/tmp/test.stash", debugger.SourcesLoaded);
    }

    [Fact]
    public void Interpreter_ExposesGlobals()
    {
        var interpreter = new Interpreter();

        Assert.NotNull(interpreter.Globals);
        // Globals should have built-in functions defined
        Assert.True(interpreter.Globals.Contains("typeof"));
    }

    [Fact]
    public void Interpreter_CurrentSpan_NullWhenNotExecuting()
    {
        var interpreter = new Interpreter();

        Assert.Null(interpreter.CurrentSpan);
    }

    // ── EvaluateString Tests ──────────────────────────────────────────

    [Fact]
    public void EvaluateString_SimpleExpression()
    {
        var interpreter = new Interpreter();
        var env = new Environment();
        env.Define("x", 10L);

        var (value, error) = interpreter.EvaluateString("x + 5", env);

        Assert.Null(error);
        Assert.Equal(15L, value);
    }

    [Fact]
    public void EvaluateString_InvalidExpression_ReturnsError()
    {
        var interpreter = new Interpreter();
        var env = new Environment();

        var (value, error) = interpreter.EvaluateString("+ + +", env);

        Assert.NotNull(error);
    }

    [Fact]
    public void EvaluateString_UndefinedVariable_ReturnsError()
    {
        var interpreter = new Interpreter();
        var env = new Environment();

        var (value, error) = interpreter.EvaluateString("undefined_var", env);

        Assert.NotNull(error);
    }

    [Fact]
    public void EvaluateString_StringConcatenation()
    {
        var interpreter = new Interpreter();
        var env = new Environment();
        env.Define("name", "world");

        var (value, error) = interpreter.EvaluateString("\"hello \" + name", env);

        Assert.Null(error);
        Assert.Equal("hello world", value);
    }

    [Fact]
    public void EvaluateString_BooleanExpression()
    {
        var interpreter = new Interpreter();
        var env = new Environment();
        env.Define("x", 10L);

        var (value, error) = interpreter.EvaluateString("x > 5", env);

        Assert.Null(error);
        Assert.Equal(true, value);
    }

    [Fact]
    public void EvaluateString_NullCoalescing()
    {
        var interpreter = new Interpreter();
        var env = new Environment();
        env.Define("x", null);

        var (value, error) = interpreter.EvaluateString("x ?? 42", env);

        Assert.Null(error);
        Assert.Equal(42L, value);
    }

    // ── CallStack with FunctionSpan Tests ─────────────────────────────

    [Fact]
    public void CallStack_FunctionHasDefinitionSpan()
    {
        CallFrame? capturedFrame = null;
        var debugger = new TestDebugger
        {
            OnFunctionEnterCallback = (name, span, env) =>
            {
                // Will be called, but we need to check CallStack during execution
            }
        };

        var interpreter = new Interpreter { Debugger = debugger };

        // Set up a capturing debugger that grabs the call stack during function call
        debugger.OnBeforeExecuteCallback = (span, env) =>
        {
            if (interpreter.CallStack.Count > 0)
            {
                capturedFrame = interpreter.CallStack[^1];
            }
        };

        var source = "fn greet() { let x = 1; } greet();";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        interpreter.Interpret(stmts);

        Assert.NotNull(capturedFrame);
        Assert.Equal("greet", capturedFrame!.FunctionName);
        Assert.NotNull(capturedFrame.FunctionSpan);
    }

    // ── FormatValue Tests ─────────────────────────────────────────────

    [Theory]
    [InlineData(null, "null")]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    [InlineData(42L, "42")]
    public void FormatValue_PrimitiveTypes(object? input, string expected)
    {
        Assert.Equal(expected, CliDebugger.FormatValue(input));
    }

    [Fact]
    public void FormatValue_String_WrapsInQuotes()
    {
        Assert.Equal("\"hello\"", CliDebugger.FormatValue("hello"));
    }

    [Fact]
    public void FormatValue_Double()
    {
        Assert.Equal("3.14", CliDebugger.FormatValue(3.14));
    }

    [Fact]
    public void FormatValue_Array()
    {
        var list = new List<object?> { 1L, "two", null };
        string result = CliDebugger.FormatValue(list);
        Assert.Equal("[1, \"two\", null]", result);
    }

    // ── Test Helper ───────────────────────────────────────────────────

    private class TestDebugger : IDebugger
    {
        public bool OnBeforeExecuteCalled { get; private set; }
        public bool OnErrorCalled { get; private set; }
        public SourceSpan? LastSpan { get; private set; }
        public Environment? LastEnv { get; private set; }
        public List<string> FunctionsEntered { get; } = new();
        public List<string> FunctionsExited { get; } = new();
        public List<string> SourcesLoaded { get; } = new();

        public Action<SourceSpan, Environment>? OnBeforeExecuteCallback { get; set; }
        public Action<string, SourceSpan, Environment>? OnFunctionEnterCallback { get; set; }

        public void OnBeforeExecute(SourceSpan span, Environment env)
        {
            OnBeforeExecuteCalled = true;
            LastSpan = span;
            LastEnv = env;
            OnBeforeExecuteCallback?.Invoke(span, env);
        }

        public void OnFunctionEnter(string name, SourceSpan callSite, Environment env)
        {
            FunctionsEntered.Add(name);
            OnFunctionEnterCallback?.Invoke(name, callSite, env);
        }

        public void OnFunctionExit(string name)
        {
            FunctionsExited.Add(name);
        }

        public void OnError(RuntimeError error, IReadOnlyList<CallFrame> callStack)
        {
            OnErrorCalled = true;
        }

        public void OnSourceLoaded(string filePath)
        {
            SourcesLoaded.Add(filePath);
        }
    }
}
