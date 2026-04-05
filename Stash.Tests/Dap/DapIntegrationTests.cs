namespace Stash.Tests.Dap;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Stash.Dap;
using SysThread = System.Threading.Thread;
using Stash.Debugging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;

/// <summary>
/// End-to-end integration tests for the DAP debug session.  Each test runs a real
/// Stash script on a background thread and coordinates with the interpreter via the
/// DebugSession API (breakpoints, step commands, variable inspection, etc.).
/// </summary>
public class DapIntegrationTests
{
    // ── Reflection helpers ────────────────────────────────────────────────────

    private static readonly FieldInfo ThreadsField =
        typeof(DebugSession).GetField("_threads",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo InterpreterThreadField =
        typeof(DebugSession).GetField("_interpreterThread",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static object? GetMainThreadState(DebugSession session)
    {
        object? dict = ThreadsField.GetValue(session);
        if (dict == null)
        {
            return null;
        }

        MethodInfo tryGet = dict.GetType().GetMethod("TryGetValue")!;
        object?[] parameters = new object?[] { 1, null };
        bool found = (bool)tryGet.Invoke(dict, parameters)!;
        return found ? parameters[1] : null;
    }

    private static bool GetIsPaused(DebugSession session)
    {
        object? ts = GetMainThreadState(session);
        if (ts == null)
        {
            return false;
        }

        FieldInfo field = ts.GetType().GetField("IsPaused")!;
        return (bool)field.GetValue(ts)!;
    }

    private static PauseReason GetPauseReason(DebugSession session)
    {
        object? ts = GetMainThreadState(session);
        if (ts == null)
        {
            return PauseReason.Step;
        }

        FieldInfo field = ts.GetType().GetField("PauseReason")!;
        return (PauseReason)field.GetValue(ts)!;
    }

    private static void WaitForPause(DebugSession session, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!GetIsPaused(session))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Interpreter did not pause within timeout.");
            }

            SysThread.Sleep(10);
        }
    }

    private static void WaitForTermination(DebugSession session, int timeoutMs = 5000)
    {
        var thread = (SysThread?)InterpreterThreadField.GetValue(session);
        if (thread != null && !thread.Join(timeoutMs))
        {
            throw new TimeoutException("Interpreter thread did not terminate within timeout.");
        }
    }

    private static string CreateTempScript(string code)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stash_test_{Guid.NewGuid():N}.stash");
        File.WriteAllText(path, code);
        return path;
    }

    // ── 1. Basic Breakpoint Hit ───────────────────────────────────────────────

    [Fact]
    public void Integration_BreakpointHit_PausesAtCorrectLine()
    {
        var path = CreateTempScript("let x = 10;\nlet y = 20;\nlet z = 30;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 2 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            Assert.NotEmpty(frames);
            Assert.Equal(2, frames[0].Line);
            Assert.Equal(PauseReason.Breakpoint, GetPauseReason(session));

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 2. Variable Inspection at Breakpoint ─────────────────────────────────

    [Fact]
    public void Integration_VariableInspection_ShowsCorrectValues()
    {
        var path = CreateTempScript("let x = 42;\nlet y = \"hello\";\nlet z = true;\nlet w = x;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 4 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            Assert.NotEmpty(frames);
            var frameId = (int)frames[0].Id;

            var scopes = session.GetScopes(frameId);
            Assert.NotEmpty(scopes);

            var vars = session.GetVariables((int)scopes[0].VariablesReference);
            Assert.Contains(vars, v => v.Name == "x" && v.Value == "42");
            Assert.Contains(vars, v => v.Name == "y" && v.Value == "\"hello\"");
            Assert.Contains(vars, v => v.Name == "z" && v.Value == "true");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 3. Conditional Breakpoint ─────────────────────────────────────────────

    [Fact]
    public void Integration_ConditionalBreakpoint_OnlyHitsWhenConditionTrue()
    {
        var path = CreateTempScript("let i = 0;\nwhile (i < 10) {\ni = i + 1;\n}\nlet done = true;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[]
            {
                new SourceBreakpoint { Line = 3, Condition = "i == 5" },
            });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            var frameId = (int)frames[0].Id;
            var scopes = session.GetScopes(frameId);
            // `i` is declared at global scope; the while body creates its own inner block
            // scope, so we search all scope layers to locate the variable.
            var allVars = scopes.SelectMany(s => session.GetVariables((int)s.VariablesReference)).ToList();

            var iVar = allVars.FirstOrDefault(v => v.Name == "i");
            Assert.NotNull(iVar);
            Assert.Equal("5", iVar!.Value);

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 4. Step Over ─────────────────────────────────────────────────────────

    [Fact]
    public void Integration_StepOver_AdvancesOneStatement()
    {
        var path = CreateTempScript("let a = 1;\nlet b = 2;\nlet c = 3;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 1 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);
            Assert.Equal(1, session.GetStackTrace()[0].Line);

            session.Next();
            WaitForPause(session);

            var frames = session.GetStackTrace();
            Assert.Equal(2, frames[0].Line);

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 5. Step Into Function ─────────────────────────────────────────────────

    [Fact]
    public void Integration_StepIn_EntersFunction()
    {
        var path = CreateTempScript("fn greet() {\nreturn \"hi\";\n}\nlet result = greet();\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 4 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);
            Assert.Equal(4, session.GetStackTrace()[0].Line);

            session.StepIn();
            WaitForPause(session);

            var frames = session.GetStackTrace();
            // After stepping in, the top frame should be the greet function body
            Assert.Equal("greet", frames[0].Name);

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 6. Step Out of Function ───────────────────────────────────────────────

    [Fact]
    public void Integration_StepOut_ExitsFunction()
    {
        var path = CreateTempScript(
            "fn compute() {\nlet x = 1;\nlet y = 2;\nreturn x + y;\n}\nlet result = compute();\nlet done = true;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 2 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);
            Assert.Equal(2, session.GetStackTrace()[0].Line);

            session.StepOut();
            WaitForPause(session);

            var frames = session.GetStackTrace();
            Assert.DoesNotContain(frames, f => f.Name == "compute");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 7. Multiple Breakpoints ───────────────────────────────────────────────

    [Fact]
    public void Integration_MultipleBreakpoints_HitsEachInOrder()
    {
        var path = CreateTempScript("let a = 1;\nlet b = 2;\nlet c = 3;\nlet d = 4;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[]
            {
                new SourceBreakpoint { Line = 2 },
                new SourceBreakpoint { Line = 4 },
            });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);
            Assert.Equal(2, session.GetStackTrace()[0].Line);

            session.Continue();
            WaitForPause(session);
            Assert.Equal(4, session.GetStackTrace()[0].Line);

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 8. Evaluate Expression ────────────────────────────────────────────────

    [Fact]
    public void Integration_Evaluate_ReturnsComputedResult()
    {
        var path = CreateTempScript("let x = 10;\nlet y = 20;\nlet z = x;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 3 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            var frameId = (int)frames[0].Id;

            var result = session.Evaluate("x + y", frameId);
            Assert.Equal("30", result);

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 9. Stop On Entry ─────────────────────────────────────────────────────

    [Fact]
    public void Integration_StopOnEntry_PausesAtFirstStatement()
    {
        var path = CreateTempScript("let first = 1;\nlet second = 2;\n");
        try
        {
            var session = new DebugSession();
            session.Launch(path, null, stopOnEntry: true, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            Assert.NotEmpty(frames);
            Assert.Equal(1, frames[0].Line);
            Assert.Equal(PauseReason.Entry, GetPauseReason(session));

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 10. Pause Request ────────────────────────────────────────────────────

    [Fact]
    public void Integration_PauseRequest_PausesDuringExecution()
    {
        var path = CreateTempScript("let i = 0;\nwhile (i < 1000000) {\ni = i + 1;\n}\n");
        try
        {
            var session = new DebugSession();
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            // Give the interpreter a moment to start running the loop, then request pause
            SysThread.Sleep(20);
            session.Pause();

            WaitForPause(session);
            Assert.True(GetIsPaused(session));
            Assert.Equal(PauseReason.Pause, GetPauseReason(session));

            session.Disconnect();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 11. Disconnect During Execution ──────────────────────────────────────

    [Fact]
    public void Integration_Disconnect_TerminatesCleanly()
    {
        var path = CreateTempScript("let i = 0;\nwhile (true) {\ni = i + 1;\n}\n");
        try
        {
            var session = new DebugSession();
            session.Launch(path, null, stopOnEntry: true, null);
            session.ConfigurationDone();

            WaitForPause(session);
            session.Disconnect();
            WaitForTermination(session);
            // If we reach here without TimeoutException, terminate was clean
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 12. Hit Count Breakpoint ─────────────────────────────────────────────

    [Fact]
    public void Integration_HitCountBreakpoint_PausesAtCorrectHitCount()
    {
        // Breakpoint on line 3 (i = i + 1). First hit: i was 0 going in, becomes 1.
        // On the 5th hit the condition fires — at that point i == 4 (set by iteration 4).
        var path = CreateTempScript("let i = 0;\nwhile (i < 10) {\ni = i + 1;\n}\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[]
            {
                new SourceBreakpoint { Line = 3, HitCondition = "== 5" },
            });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            var frameId = (int)frames[0].Id;
            var scopes = session.GetScopes(frameId);
            // `i` is declared at global scope; the while body creates its own inner block
            // scope, so we search all scope layers to locate the variable.
            var allVars = scopes.SelectMany(s => session.GetVariables((int)s.VariablesReference)).ToList();

            var iVar = allVars.FirstOrDefault(v => v.Name == "i");
            Assert.NotNull(iVar);
            Assert.Equal("4", iVar!.Value);

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 13. Logpoint (no pause) ───────────────────────────────────────────────

    [Fact]
    public void Integration_Logpoint_DoesNotPause()
    {
        var path = CreateTempScript("let x = 5;\nlet y = 10;\nlet z = 15;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[]
            {
                new SourceBreakpoint { Line = 2, LogMessage = "value is {x}" },
            });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            // Logpoint must not pause — the script should run to completion
            WaitForTermination(session);
            Assert.False(GetIsPaused(session));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 14. Exception Breakpoint ─────────────────────────────────────────────

    [Fact]
    public void Integration_ExceptionBreakpoint_PausesOnError()
    {
        // Accessing a field on null causes a RuntimeError which routes through OnError.
        // With _breakOnAllExceptions = true, the session pauses before propagating.
        var path = CreateTempScript("let x = 1;\nlet y = null;\ny.field;\n");
        try
        {
            var session = new DebugSession();
            session.SetExceptionBreakpoints(new[] { "all" });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);
            Assert.Equal(PauseReason.Exception, GetPauseReason(session));

            // Continuing lets the RuntimeError propagate; the thread terminates.
            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 15. Nested Function Call Stack ────────────────────────────────────────

    [Fact]
    public void Integration_NestedCalls_StackTraceShowsFullChain()
    {
        var path = CreateTempScript(
            "fn inner() {\nlet x = 1;\nreturn x;\n}\nfn outer() {\nreturn inner();\n}\nlet result = outer();\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 2 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            Assert.True(frames.Count >= 3);
            Assert.Contains(frames, f => f.Name == "inner");
            Assert.Contains(frames, f => f.Name == "outer");
            Assert.Contains(frames, f => f.Name == "<script>");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 16. Scopes Show Local vs Global ──────────────────────────────────────

    [Fact]
    public void Integration_Scopes_ShowLocalAndGlobal()
    {
        var path = CreateTempScript(
            "let g = \"global\";\nfn test() {\nlet loc = \"local\";\nreturn loc;\n}\nlet result = test();\n");
        try
        {
            var session = new DebugSession();
            // Line 4 = `return loc;` — loc is already defined at this point
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 4 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            var frameId = (int)frames[0].Id;
            var scopes = session.GetScopes(frameId);

            Assert.True(scopes.Count >= 2);
            Assert.Contains(scopes, s => s.Name == "Local");
            Assert.Contains(scopes, s => s.Name == "Global");

            var localScope = scopes.First(s => s.Name == "Local");
            var localVars = session.GetVariables((int)localScope.VariablesReference);
            Assert.Contains(localVars, v => v.Name == "loc" && v.Value == "\"local\"");

            var globalScope = scopes.First(s => s.Name == "Global");
            var globalVars = session.GetVariables((int)globalScope.VariablesReference);
            Assert.Contains(globalVars, v => v.Name == "g" && v.Value == "\"global\"");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 17. Array Variable Expansion ─────────────────────────────────────────

    [Fact]
    public void Integration_ArrayExpansion_ShowsElements()
    {
        var path = CreateTempScript("let arr = [10, 20, 30];\nlet x = arr;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 2 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            var frameId = (int)frames[0].Id;
            var scopes = session.GetScopes(frameId);
            var vars = session.GetVariables((int)scopes[0].VariablesReference);

            var arrVar = vars.FirstOrDefault(v => v.Name == "arr");
            Assert.NotNull(arrVar);
            Assert.True(arrVar!.VariablesReference > 0);

            var elements = session.GetVariables((int)arrVar.VariablesReference);
            Assert.Equal(3, elements.Count);
            Assert.Contains(elements, e => e.Name == "[0]" && e.Value == "10");
            Assert.Contains(elements, e => e.Name == "[1]" && e.Value == "20");
            Assert.Contains(elements, e => e.Name == "[2]" && e.Value == "30");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 18. Dictionary Variable Expansion ────────────────────────────────────

    [Fact]
    public void Integration_DictExpansion_ShowsEntries()
    {
        var path = CreateTempScript("let d = dict.new();\nd[\"name\"] = \"stash\";\nd[\"version\"] = 1;\nlet x = d;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 4 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            var frameId = (int)frames[0].Id;
            var scopes = session.GetScopes(frameId);
            var vars = session.GetVariables((int)scopes[0].VariablesReference);

            var dVar = vars.FirstOrDefault(v => v.Name == "d");
            Assert.NotNull(dVar);
            Assert.True(dVar!.VariablesReference > 0);

            var entries = session.GetVariables((int)dVar.VariablesReference);
            Assert.True(entries.Count >= 2);
            Assert.Contains(entries, e => e.Name == "name" && e.Value == "\"stash\"");
            Assert.Contains(entries, e => e.Name == "version" && e.Value == "1");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 19. Script with Arguments ─────────────────────────────────────────────

    [Fact]
    public void Integration_ScriptArgs_Accessible()
    {
        // Use the args.parse API to expose script arguments as Stash values.
        var script =
            "let a = args.parse({ positionals: [{ name: \"target\", type: \"string\", description: \"Target\" }] });\n" +
            "let first = a.target;\n" +
            "let x = first;\n";
        var path = CreateTempScript(script);
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 3 } });
            session.Launch(path, null, false, new[] { "hello" });
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            var frameId = (int)frames[0].Id;
            var scopes = session.GetScopes(frameId);
            var vars = session.GetVariables((int)scopes[0].VariablesReference);

            Assert.Contains(vars, v => v.Name == "first" && v.Value == "\"hello\"");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 20. Configuration Done Blocks Execution ───────────────────────────────

    [Fact]
    public void Integration_ConfigurationDone_BlocksUntilCalled()
    {
        var path = CreateTempScript("let x = 1;\nlet y = 2;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 1 } });
            session.Launch(path, null, false, null);

            // The interpreter thread is blocked waiting for ConfigurationDone.
            // The breakpoint on line 1 must NOT have fired yet.
            SysThread.Sleep(200);
            Assert.False(GetIsPaused(session));

            // Unblock execution — interpreter should now hit the breakpoint on line 1.
            session.ConfigurationDone();

            WaitForPause(session);
            Assert.Equal(1, session.GetStackTrace()[0].Line);

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 21. Function Breakpoint ───────────────────────────────────────────────

    [Fact]
    public void Integration_FunctionBreakpoint_PausesOnEntry()
    {
        var path = CreateTempScript(
            "fn greet(name) {\nreturn \"hi \" + name;\n}\nlet result = greet(\"world\");\n");
        try
        {
            var session = new DebugSession();
            session.SetFunctionBreakpoints(new[]
            {
                new FunctionBreakpoint { Name = "greet" },
            });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            Assert.Contains(frames, f => f.Name == "greet");
            Assert.Equal(PauseReason.FunctionBreakpoint, GetPauseReason(session));

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 22. Function Breakpoint with Condition ────────────────────────────────

    [Fact]
    public void Integration_FunctionBreakpoint_WithCondition()
    {
        // Verify the breakpoint fires on every invocation of the function.
        var path = CreateTempScript(
            "fn greet(name) {\nreturn \"hi \" + name;\n}\nlet result = greet(\"world\");\nlet result2 = greet(\"moon\");\n");
        try
        {
            var session = new DebugSession();
            session.SetFunctionBreakpoints(new[]
            {
                new FunctionBreakpoint { Name = "greet" },
            });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            // First call to greet("world")
            WaitForPause(session);
            var frames = session.GetStackTrace();
            Assert.Contains(frames, f => f.Name == "greet");
            Assert.Equal(PauseReason.FunctionBreakpoint, GetPauseReason(session));

            session.Continue();

            // Second call to greet("moon")
            WaitForPause(session);
            frames = session.GetStackTrace();
            Assert.Contains(frames, f => f.Name == "greet");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 23. Set Variable at Breakpoint ────────────────────────────────────────

    [Fact]
    public void Integration_SetVariable_ModifiesValueDuringExecution()
    {
        var path = CreateTempScript("let x = 10;\nlet y = x + 5;\nlet z = y;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 2 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            var frameId = (int)frames[0].Id;
            var scopes = session.GetScopes(frameId);

            // Modify x from 10 to 100
            var result = session.SetVariable((int)scopes[0].VariablesReference, "x", "100");
            Assert.Equal("100", result.Value);
            Assert.Equal("int", result.Type);

            // Verify the change persisted
            var vars = session.GetVariables((int)scopes[0].VariablesReference);
            Assert.Contains(vars, v => v.Name == "x" && v.Value == "100");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 24. Set Variable — Array Element ──────────────────────────────────────

    [Fact]
    public void Integration_SetVariable_ModifiesArrayElement()
    {
        var path = CreateTempScript("let arr = [1, 2, 3];\nlet x = arr;\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 2 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            var frameId = (int)frames[0].Id;
            var scopes = session.GetScopes(frameId);
            var vars = session.GetVariables((int)scopes[0].VariablesReference);

            var arrVar = vars.First(v => v.Name == "arr");
            Assert.True(arrVar.VariablesReference > 0);

            // Modify arr[1] from 2 to 99
            var result = session.SetVariable((int)arrVar.VariablesReference, "[1]", "99");
            Assert.Equal("99", result.Value);

            // Verify the change
            var elements = session.GetVariables((int)arrVar.VariablesReference);
            Assert.Contains(elements, e => e.Name == "[1]" && e.Value == "99");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 25. Multiple Function Breakpoints ─────────────────────────────────────

    [Fact]
    public void Integration_MultipleFunctionBreakpoints_HitsEach()
    {
        var path = CreateTempScript(
            "fn alpha() {\nreturn 1;\n}\nfn beta() {\nreturn 2;\n}\nlet a = alpha();\nlet b = beta();\n");
        try
        {
            var session = new DebugSession();
            session.SetFunctionBreakpoints(new[]
            {
                new FunctionBreakpoint { Name = "alpha" },
                new FunctionBreakpoint { Name = "beta" },
            });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();

            // First hit: alpha
            WaitForPause(session);
            var frames = session.GetStackTrace();
            Assert.Contains(frames, f => f.Name == "alpha");

            session.Continue();

            // Second hit: beta
            WaitForPause(session);
            frames = session.GetStackTrace();
            Assert.Contains(frames, f => f.Name == "beta");

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 26. Loaded Sources ────────────────────────────────────────────────────

    [Fact]
    public void LoadedSources_MainScript_AppearsInSources()
    {
        var path = CreateTempScript(@"
let x = 1;
let y = 2;
");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(path, new[] { new SourceBreakpoint { Line = 3 } });
            session.Launch(path, null, false, null);
            session.ConfigurationDone();
            WaitForPause(session);

            var sources = session.GetLoadedSources();
            Assert.Single(sources);
            Assert.Equal(path, sources[0].Path);
            Assert.Equal(Path.GetFileName(path), sources[0].Name);

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadedSources_WithImport_BothSourcesAppear()
    {
        var modulePath = Path.Combine(Path.GetTempPath(), $"stash_module_{Guid.NewGuid():N}.stash");
        File.WriteAllText(modulePath, @"
fn helper() {
    return 42;
}
");

        var mainPath = Path.Combine(Path.GetTempPath(), $"stash_main_{Guid.NewGuid():N}.stash");
        File.WriteAllText(mainPath,
            $"import {{ helper }} from \"{modulePath}\";\n" +
            "let result = helper();\n");
        try
        {
            var session = new DebugSession();
            session.SetBreakpoints(mainPath, new[] { new SourceBreakpoint { Line = 2 } });
            session.Launch(mainPath, null, false, null);
            session.ConfigurationDone();
            WaitForPause(session);

            var sources = session.GetLoadedSources();
            Assert.Equal(2, sources.Count);
            Assert.Contains(sources, s => s.Path != null && s.Path.Contains(Path.GetFileName(mainPath)));
            Assert.Contains(sources, s => s.Path != null && s.Path.Contains(Path.GetFileName(modulePath)));

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(mainPath);
            File.Delete(modulePath);
        }
    }

    // ── Windows Path Normalization ────────────────────────────────────────────

    [Fact]
    public void Integration_BreakpointHit_DifferentDriveLetterCasing()
    {
        var path = CreateTempScript("let x = 10;\nlet y = 20;\nlet z = 30;\n");
        try
        {
            // Simulate VS Code sending different drive letter casing
            // for breakpoints vs program path
            string bpPath = path;
            string launchPath = path;

            if (path.Length >= 2 && path[1] == ':')
            {
                // Use lowercase drive letter for breakpoints, uppercase for launch
                bpPath = char.ToLowerInvariant(path[0]) + path[1..];
                launchPath = char.ToUpperInvariant(path[0]) + path[1..];
            }

            var session = new DebugSession();
            session.SetBreakpoints(bpPath, new[] { new SourceBreakpoint { Line = 2 } });
            session.Launch(launchPath, null, false, null);
            session.ConfigurationDone();

            WaitForPause(session);

            var frames = session.GetStackTrace();
            Assert.NotEmpty(frames);
            Assert.Equal(2, frames[0].Line);
            Assert.Equal(PauseReason.Breakpoint, GetPauseReason(session));

            session.Continue();
            WaitForTermination(session);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
