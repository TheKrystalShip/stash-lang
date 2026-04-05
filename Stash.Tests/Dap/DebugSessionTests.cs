namespace Stash.Tests.Dap;

using System;
using System.Collections.Generic;
using System.Reflection;
using Stash.Dap;
using Stash.Common;
using Stash.Debugging;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using Variable = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Variable;

public class DebugSessionTests
{
    // ── Reflection helpers ────────────────────────────────────────────────────

    private static bool InvokeEvaluateHitCondition(string condition, int hitCount)
    {
        var method = typeof(DebugSession).GetMethod("EvaluateHitCondition",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object[] { condition, hitCount })!;
    }

    private static Variable InvokeFormatVariable(DebugSession session, string name, object? value)
    {
        var method = typeof(DebugSession).GetMethod("FormatVariable",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Variable)method.Invoke(session, new object?[] { name, value })!;
    }

    private static string InvokeInterpolateLogMessage(DebugSession session, string template, IDebugScope env, IDebugExecutor? executor = null)
    {
        var method = typeof(DebugSession).GetMethod("InterpolateLogMessage",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (string)method.Invoke(session, new object?[] { template, env, executor })!;
    }

    private static void SetExecutor(DebugSession session, IDebugExecutor executor)
    {
        var field = typeof(DebugSession).GetField("_executor",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(session, executor);
    }

    // ── 1. SetBreakpoints Tests ───────────────────────────────────────────────

    [Fact]
    public void SetBreakpoints_ReturnsVerifiedBreakpoints()
    {
        var session = new DebugSession();
        var bps = session.SetBreakpoints("/tmp/test.stash", new[]
        {
            new SourceBreakpoint { Line = 5 },
        });
        Assert.Single(bps);
        Assert.True(bps[0].Verified);
        Assert.Equal(5, bps[0].Line);
    }

    [Fact]
    public void SetBreakpoints_AssignsUniqueIds()
    {
        var session = new DebugSession();
        var bps = session.SetBreakpoints("/tmp/test.stash", new[]
        {
            new SourceBreakpoint { Line = 1 },
            new SourceBreakpoint { Line = 2 },
        });
        Assert.Equal(2, bps.Count);
        Assert.NotEqual(bps[0].Id, bps[1].Id);
    }

    [Fact]
    public void SetBreakpoints_ReplacesExistingBreakpoints()
    {
        var session = new DebugSession();
        session.SetBreakpoints("/tmp/test.stash", new[]
        {
            new SourceBreakpoint { Line = 1 },
            new SourceBreakpoint { Line = 2 },
            new SourceBreakpoint { Line = 3 },
        });
        var bps = session.SetBreakpoints("/tmp/test.stash", new[]
        {
            new SourceBreakpoint { Line = 10 },
        });
        Assert.Single(bps);
        Assert.Equal(10, bps[0].Line);
    }

    [Fact]
    public void SetBreakpoints_WithCondition_StoresCondition()
    {
        var session = new DebugSession();
        var bps = session.SetBreakpoints("/tmp/test.stash", new[]
        {
            new SourceBreakpoint { Line = 7, Condition = "x > 3" },
        });
        Assert.Single(bps);
        Assert.True(bps[0].Verified);
        Assert.Equal(7, bps[0].Line);
    }

    [Fact]
    public void SetBreakpoints_WithHitCondition_StoresHitCondition()
    {
        var session = new DebugSession();
        var bps = session.SetBreakpoints("/tmp/test.stash", new[]
        {
            new SourceBreakpoint { Line = 3, HitCondition = ">= 5" },
        });
        Assert.Single(bps);
        Assert.Equal(3, bps[0].Line);
        Assert.True(bps[0].Verified);
    }

    [Fact]
    public void SetBreakpoints_WithLogMessage_StoresLogMessage()
    {
        var session = new DebugSession();
        var bps = session.SetBreakpoints("/tmp/test.stash", new[]
        {
            new SourceBreakpoint { Line = 8, LogMessage = "Reached line 8: x={x}" },
        });
        Assert.Single(bps);
        Assert.Equal(8, bps[0].Line);
        Assert.True(bps[0].Verified);
    }

    [Fact]
    public void SetBreakpoints_MultipleLinesInSameFile()
    {
        var session = new DebugSession();
        var bps = session.SetBreakpoints("/tmp/test.stash", new[]
        {
            new SourceBreakpoint { Line = 1 },
            new SourceBreakpoint { Line = 5 },
            new SourceBreakpoint { Line = 10 },
        });
        Assert.Equal(3, bps.Count);
        Assert.All(bps, b => Assert.True(b.Verified));
        Assert.Equal(1, bps[0].Line);
        Assert.Equal(5, bps[1].Line);
        Assert.Equal(10, bps[2].Line);
    }

    [Fact]
    public void SetBreakpoints_EmptyList_ClearsBreakpoints()
    {
        var session = new DebugSession();
        session.SetBreakpoints("/tmp/test.stash", new[]
        {
            new SourceBreakpoint { Line = 1 },
        });
        var bps = session.SetBreakpoints("/tmp/test.stash", Array.Empty<SourceBreakpoint>());
        Assert.Empty(bps);
    }

    [Fact]
    public void SetBreakpoints_DifferentFiles_Isolated()
    {
        var session = new DebugSession();
        var bps1 = session.SetBreakpoints("/tmp/file1.stash", new[]
        {
            new SourceBreakpoint { Line = 1 },
            new SourceBreakpoint { Line = 2 },
        });
        var bps2 = session.SetBreakpoints("/tmp/file2.stash", new[]
        {
            new SourceBreakpoint { Line = 10 },
        });
        Assert.Equal(2, bps1.Count);
        Assert.Single(bps2);
        Assert.Equal(10, bps2[0].Line);
    }

    // ── 2. SetExceptionBreakpoints Tests ─────────────────────────────────────

    [Fact]
    public void SetExceptionBreakpoints_AllFilter_SetsBreakOnAllExceptions()
    {
        var session = new DebugSession();
        session.SetExceptionBreakpoints(new[] { "all" });
        Assert.True(session.ShouldBreakOnException(new RuntimeError("test error")));
    }

    [Fact]
    public void SetExceptionBreakpoints_UncaughtFilter_SetsBreakOnExceptions()
    {
        var session = new DebugSession();
        session.SetExceptionBreakpoints(new[] { "uncaught" });
        Assert.True(session.ShouldBreakOnException(new RuntimeError("test error")));
    }

    [Fact]
    public void SetExceptionBreakpoints_EmptyFilters_DisablesExceptionBreaks()
    {
        var session = new DebugSession();
        session.SetExceptionBreakpoints(new[] { "all" });
        session.SetExceptionBreakpoints(Array.Empty<string>());
        Assert.False(session.ShouldBreakOnException(new RuntimeError("test error")));
    }

    [Fact]
    public void SetExceptionBreakpoints_UnknownFilter_Ignored()
    {
        var session = new DebugSession();
        session.SetExceptionBreakpoints(new[] { "unknown_filter", "foobar" });
        Assert.False(session.ShouldBreakOnException(new RuntimeError("test error")));
    }

    [Fact]
    public void ShouldBreakOnException_WhenAllEnabled_ReturnsTrue()
    {
        var session = new DebugSession();
        session.SetExceptionBreakpoints(new[] { "all" });
        Assert.True(session.ShouldBreakOnException(new RuntimeError("any error")));
    }

    [Fact]
    public void ShouldBreakOnException_WhenDisabled_ReturnsFalse()
    {
        var session = new DebugSession();
        Assert.False(session.ShouldBreakOnException(new RuntimeError("any error")));
    }

    // ── 3. Stepping Tests ─────────────────────────────────────────────────────

    [Fact]
    public void StepOut_AtTopLevel_ContinuesExecution()
    {
        // With no interpreter (_interpreter == null), callStack depth is 0.
        // StepOut delegates to Continue() which calls Resume() — should not throw.
        var session = new DebugSession();
        session.StepOut();
    }

    [Fact]
    public void Pause_SetsPauseRequested()
    {
        var session = new DebugSession();
        session.Pause();
        Assert.True(session.IsPauseRequested);
    }

    [Fact]
    public void IsPauseRequested_DefaultFalse()
    {
        var session = new DebugSession();
        Assert.False(session.IsPauseRequested);
    }

    [Fact]
    public void StopOnEntry_DefaultFalse()
    {
        var session = new DebugSession();
        Assert.False(session.StopOnEntry);
    }

    // ── 4. Launch Validation Tests ────────────────────────────────────────────

    [Fact]
    public void Launch_NullScriptPath_ThrowsArgumentException()
    {
        var session = new DebugSession();
        Assert.Throws<ArgumentException>(() => session.Launch(null!, null, false, null));
    }

    [Fact]
    public void Launch_EmptyScriptPath_ThrowsArgumentException()
    {
        var session = new DebugSession();
        Assert.Throws<ArgumentException>(() => session.Launch("", null, false, null));
    }

    [Fact]
    public void Launch_WhitespaceScriptPath_ThrowsArgumentException()
    {
        var session = new DebugSession();
        Assert.Throws<ArgumentException>(() => session.Launch("   ", null, false, null));
    }

    // ── 5. GetStackTrace Tests ────────────────────────────────────────────────

    [Fact]
    public void GetStackTrace_BeforeLaunch_ReturnsEmpty()
    {
        var session = new DebugSession();
        var frames = session.GetStackTrace();
        Assert.Empty(frames);
    }

    // ── 6. GetScopes Tests ────────────────────────────────────────────────────

    [Fact]
    public void GetScopes_BeforeLaunch_ReturnsEmpty()
    {
        var session = new DebugSession();
        var scopes = session.GetScopes(0);
        Assert.Empty(scopes);
    }

    // ── 7. GetVariables Tests ─────────────────────────────────────────────────

    [Fact]
    public void GetVariables_InvalidReference_ReturnsEmpty()
    {
        var session = new DebugSession();
        var vars = session.GetVariables(9999);
        Assert.Empty(vars);
    }

    // ── 8. Evaluate Tests ─────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_BeforeLaunch_ReturnsNoInterpreter()
    {
        var session = new DebugSession();
        var result = session.Evaluate("1 + 1", null);
        Assert.Equal("No interpreter", result);
    }

    // ── 9. Disconnect Tests ───────────────────────────────────────────────────

    [Fact]
    public void Disconnect_SetsTerminatedState()
    {
        var session = new DebugSession();
        session.Disconnect();
        var field = typeof(DebugSession).GetField("_terminated",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.True((bool)field.GetValue(session)!);
    }

    // ── 10. EvaluateHitCondition Tests ────────────────────────────────────────

    [Theory]
    [InlineData("== 5", 5, true)]
    [InlineData("== 5", 3, false)]
    [InlineData(">= 3", 3, true)]
    [InlineData(">= 3", 2, false)]
    [InlineData("> 3", 4, true)]
    [InlineData("> 3", 3, false)]
    [InlineData("<= 3", 3, true)]
    [InlineData("<= 3", 4, false)]
    [InlineData("< 3", 2, true)]
    [InlineData("< 3", 3, false)]
    [InlineData("% 2 == 0", 4, true)]
    [InlineData("% 2 == 0", 3, false)]
    [InlineData("5", 5, true)]
    [InlineData("5", 3, false)]
    [InlineData("garbage", 1, true)]
    public void EvaluateHitCondition_ReturnsExpected(string condition, int hitCount, bool expected)
    {
        Assert.Equal(expected, InvokeEvaluateHitCondition(condition, hitCount));
    }

    // ── 11. FormatVariable Tests ──────────────────────────────────────────────

    [Fact]
    public void FormatVariable_Null_ReturnsNullTypeAndValue()
    {
        var session = new DebugSession();
        var v = InvokeFormatVariable(session, "x", null);
        Assert.Equal("x", v.Name);
        Assert.Equal("null", v.Type);
        Assert.Equal("null", v.Value);
        Assert.Equal(0, v.VariablesReference);
    }

    [Fact]
    public void FormatVariable_Long_ReturnsIntType()
    {
        var session = new DebugSession();
        var v = InvokeFormatVariable(session, "n", 42L);
        Assert.Equal("int", v.Type);
        Assert.Equal("42", v.Value);
        Assert.Equal(0, v.VariablesReference);
    }

    [Fact]
    public void FormatVariable_Double_ReturnsFloatType()
    {
        var session = new DebugSession();
        var v = InvokeFormatVariable(session, "f", 3.14);
        Assert.Equal("float", v.Type);
        Assert.Equal("3.14", v.Value);
        Assert.Equal(0, v.VariablesReference);
    }

    [Fact]
    public void FormatVariable_BoolTrue_ReturnsTrueValue()
    {
        var session = new DebugSession();
        var v = InvokeFormatVariable(session, "b", true);
        Assert.Equal("bool", v.Type);
        Assert.Equal("true", v.Value);
        Assert.Equal(0, v.VariablesReference);
    }

    [Fact]
    public void FormatVariable_BoolFalse_ReturnsFalseValue()
    {
        var session = new DebugSession();
        var v = InvokeFormatVariable(session, "b", false);
        Assert.Equal("bool", v.Type);
        Assert.Equal("false", v.Value);
        Assert.Equal(0, v.VariablesReference);
    }

    [Fact]
    public void FormatVariable_String_ReturnsQuotedValue()
    {
        var session = new DebugSession();
        var v = InvokeFormatVariable(session, "s", "hello");
        Assert.Equal("string", v.Type);
        Assert.Equal("\"hello\"", v.Value);
        Assert.Equal(0, v.VariablesReference);
    }

    [Fact]
    public void FormatVariable_List_ReturnsArrayTypeWithReference()
    {
        var session = new DebugSession();
        var list = new List<object?> { 1L, 2L, 3L };
        var v = InvokeFormatVariable(session, "arr", list);
        Assert.Equal("array", v.Type);
        Assert.Equal("array[3]", v.Value);
        Assert.True(v.VariablesReference > 0);
    }

    [Fact]
    public void FormatVariable_EmptyList_ReturnsArrayZeroCount()
    {
        var session = new DebugSession();
        var v = InvokeFormatVariable(session, "arr", new List<object?>());
        Assert.Equal("array", v.Type);
        Assert.Equal("array[0]", v.Value);
        Assert.True(v.VariablesReference > 0);
    }

    [Fact]
    public void FormatVariable_StashDictionary_ReturnsDictTypeWithReference()
    {
        var session = new DebugSession();
        var dict = new StashDictionary();
        dict.Set("key1", "value1");
        dict.Set("key2", "value2");
        var v = InvokeFormatVariable(session, "d", dict);
        Assert.Equal("dict", v.Type);
        Assert.Equal("dict[2]", v.Value);
        Assert.True(v.VariablesReference > 0);
    }

    [Fact]
    public void FormatVariable_StashInstance_ReturnsTypeNameWithReference()
    {
        var session = new DebugSession();
        var instance = new StashInstance("Point", new Dictionary<string, object?>
        {
            { "x", 10L },
            { "y", 20L },
        });
        var v = InvokeFormatVariable(session, "p", instance);
        Assert.Equal("Point", v.Type);
        Assert.Equal("Point {...}", v.Value);
        Assert.True(v.VariablesReference > 0);
    }

    [Fact]
    public void FormatVariable_StashEnumValue_ReturnsEnumTypeAndDotNotation()
    {
        var session = new DebugSession();
        var enumVal = new StashEnumValue("Color", "Red");
        var v = InvokeFormatVariable(session, "c", enumVal);
        Assert.Equal("enum", v.Type);
        Assert.Equal("Color.Red", v.Value);
        Assert.Equal(0, v.VariablesReference);
    }

    // ── 12. InterpolateLogMessage Tests ──────────────────────────────────────

    [Fact]
    public void InterpolateLogMessage_PlainText_ReturnsAsIs()
    {
        // Without an interpreter, the method returns the template unchanged.
        var session = new DebugSession();
        var env = new TestDebugScope();
        var result = InvokeInterpolateLogMessage(session, "Hello World", env);
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void InterpolateLogMessage_WithExpression_Evaluates()
    {
        var session = new DebugSession();
        var interpreter = new TestExecutor();
        var env = new TestDebugScope();
        var result = InvokeInterpolateLogMessage(session, "Result: {1 + 1}", env, interpreter);
        Assert.Equal("Result: 2", result);
    }

    [Fact]
    public void InterpolateLogMessage_MissingCloseBrace_TreatsAsText()
    {
        var session = new DebugSession();
        var interpreter = new TestExecutor();
        var env = new TestDebugScope();
        // {unclosed has no matching } — characters are emitted as-is
        var result = InvokeInterpolateLogMessage(session, "value={unclosed", env, interpreter);
        Assert.Equal("value={unclosed", result);
    }

    // ── 13. OnBeforeExecute Tests ─────────────────────────────────────────────

    [Fact]
    public void OnBeforeExecute_WhenTerminated_ThrowsOperationCanceled()
    {
        var session = new DebugSession();
        session.Disconnect();
        Assert.Throws<OperationCanceledException>(() =>
            session.OnBeforeExecute(new SourceSpan("test.stash", 1, 1, 1, 10), new TestDebugScope(), 1));
    }

    [Fact]
    public void OnBeforeExecute_UpdatesPausedAtSpan()
    {
        var session = new DebugSession();
        var span = new SourceSpan("script.stash", 3, 1, 3, 20);
        // No step mode or pause requested — just records the span and returns.
        // The constructor pre-registers a placeholder ThreadState for the main thread.
        session.OnBeforeExecute(span, new TestDebugScope(), 1);

        // PausedAtSpan is now on the per-thread ThreadState stored in _threads[1]
        var threadsField = typeof(DebugSession).GetField("_threads",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        object? dict = threadsField.GetValue(session);
        MethodInfo tryGet = dict!.GetType().GetMethod("TryGetValue")!;
        object?[] args = new object?[] { 1, null };
        bool found = (bool)tryGet.Invoke(dict, args)!;
        Assert.True(found);
        object? threadState = args[1];
        FieldInfo pausedAtSpanField = threadState!.GetType().GetField("PausedAtSpan")!;
        var storedSpan = (SourceSpan?)pausedAtSpanField.GetValue(threadState);
        Assert.Equal(span, storedSpan);
    }

    // ── 14. SetVariable Tests ─────────────────────────────────────────────────

    [Fact]
    public void SetVariable_BeforeLaunch_ThrowsInvalidOperation()
    {
        var session = new DebugSession();
        Assert.Throws<InvalidOperationException>(() => session.SetVariable(1, "x", "42"));
    }

    [Fact]
    public void SetVariable_InvalidReference_ThrowsInvalidOperation()
    {
        var session = new DebugSession();
        SetExecutor(session, new TestExecutor());
        Assert.Throws<InvalidOperationException>(() => session.SetVariable(9999, "x", "42"));
    }

    // ── 15. SetFunctionBreakpoints Tests ──────────────────────────────────────

    [Fact]
    public void SetFunctionBreakpoints_ReturnsVerifiedBreakpoints()
    {
        var session = new DebugSession();
        var bps = session.SetFunctionBreakpoints(new[]
        {
            new FunctionBreakpoint { Name = "myFunc" },
        });
        Assert.Single(bps);
        Assert.True(bps[0].Verified);
    }

    [Fact]
    public void SetFunctionBreakpoints_AssignsUniqueIds()
    {
        var session = new DebugSession();
        var bps = session.SetFunctionBreakpoints(new[]
        {
            new FunctionBreakpoint { Name = "func1" },
            new FunctionBreakpoint { Name = "func2" },
        });
        Assert.Equal(2, bps.Count);
        Assert.NotEqual(bps[0].Id, bps[1].Id);
    }

    [Fact]
    public void SetFunctionBreakpoints_ReplacesExisting()
    {
        var session = new DebugSession();
        session.SetFunctionBreakpoints(new[]
        {
            new FunctionBreakpoint { Name = "func1" },
            new FunctionBreakpoint { Name = "func2" },
        });
        var bps = session.SetFunctionBreakpoints(new[]
        {
            new FunctionBreakpoint { Name = "func3" },
        });
        Assert.Single(bps);
    }

    [Fact]
    public void SetFunctionBreakpoints_EmptyList_ClearsAll()
    {
        var session = new DebugSession();
        session.SetFunctionBreakpoints(new[]
        {
            new FunctionBreakpoint { Name = "func1" },
        });
        var bps = session.SetFunctionBreakpoints(Array.Empty<FunctionBreakpoint>());
        Assert.Empty(bps);
    }

    [Fact]
    public void ShouldBreakOnFunctionEntry_ReturnsTrueWhenSet()
    {
        var session = new DebugSession();
        session.SetFunctionBreakpoints(new[]
        {
            new FunctionBreakpoint { Name = "myFunc" },
        });
        Assert.True(session.ShouldBreakOnFunctionEntry("myFunc"));
        Assert.False(session.ShouldBreakOnFunctionEntry("otherFunc"));
    }

    [Fact]
    public void ShouldBreakOnFunctionEntry_ReturnsFalseAfterClearing()
    {
        var session = new DebugSession();
        session.SetFunctionBreakpoints(new[]
        {
            new FunctionBreakpoint { Name = "myFunc" },
        });
        session.SetFunctionBreakpoints(Array.Empty<FunctionBreakpoint>());
        Assert.False(session.ShouldBreakOnFunctionEntry("myFunc"));
    }

    [Fact]
    public void ShouldBreakOnFunctionEntry_DefaultReturnsFalse()
    {
        var session = new DebugSession();
        Assert.False(session.ShouldBreakOnFunctionEntry("anyFunction"));
        Assert.False(session.ShouldBreakOnFunctionEntry("main"));
        Assert.False(session.ShouldBreakOnFunctionEntry(""));
    }

    // ── 16. LoadedSources Tests ──────────────────────────────────────────────

    [Fact]
    public void GetLoadedSources_BeforeLaunch_ReturnsEmpty()
    {
        var session = new DebugSession();
        var sources = session.GetLoadedSources();
        Assert.Empty(sources);
    }

    [Fact]
    public void OnSourceLoaded_TracksLoadedFile()
    {
        var session = new DebugSession();
        session.OnSourceLoaded("/path/to/script.stash");
        var sources = session.GetLoadedSources();
        Assert.Single(sources);
        Assert.Equal("/path/to/script.stash", sources[0].Path);
        Assert.Equal("script.stash", sources[0].Name);
    }

    [Fact]
    public void OnSourceLoaded_DeduplicatesSamePath()
    {
        var session = new DebugSession();
        session.OnSourceLoaded("/path/to/script.stash");
        session.OnSourceLoaded("/path/to/script.stash");
        var sources = session.GetLoadedSources();
        Assert.Single(sources);
    }

    [Fact]
    public void OnSourceLoaded_TracksMultipleFiles()
    {
        var session = new DebugSession();
        session.OnSourceLoaded("/path/to/main.stash");
        session.OnSourceLoaded("/path/to/lib/utils.stash");
        session.OnSourceLoaded("/path/to/lib/helpers.stash");
        var sources = session.GetLoadedSources();
        Assert.Equal(3, sources.Count);
    }

    [Fact]
    public void GetLoadedSources_ClearedAfterDisconnect()
    {
        var session = new DebugSession();
        session.OnSourceLoaded("/path/to/script.stash");
        Assert.Single(session.GetLoadedSources());
        session.Disconnect();
        Assert.Empty(session.GetLoadedSources());
    }

    private class TestExecutor : IDebugExecutor
    {
        public IReadOnlyList<Stash.Debugging.CallFrame> CallStack => [];
        public IDebugScope GlobalScope => new TestDebugScope();

        public (object? Value, string? Error) EvaluateExpression(string expression, IDebugScope scope)
        {
            try
            {
                var lexer = new Lexer(expression, "<eval>");
                var tokens = lexer.ScanTokens();
                var parser = new Parser(tokens);
                var expr = parser.Parse();
                var chunk = Compiler.CompileExpression(expr);
                var vm = new VirtualMachine();
                return (vm.Execute(chunk), null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }
    }

    private class TestDebugScope : IDebugScope
    {
        private readonly Dictionary<string, object?> _vars = new();
        public IDebugScope? EnclosingScope => null;
        public IEnumerable<KeyValuePair<string, object?>> GetAllBindings() => _vars;
        public bool Contains(string name) => _vars.ContainsKey(name);
        public bool TryAssign(string name, object? value)
        {
            if (!_vars.ContainsKey(name)) return false;
            _vars[name] = value;
            return true;
        }
    }
}
