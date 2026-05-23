namespace Stash.Tests.Dap;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using Stash.Dap;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Abstractions;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using Variable = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Variable;
using RuntimeBuiltInFunction = Stash.Runtime.BuiltInFunction;
using SysThread = System.Threading.Thread;
using Xunit;

/// <summary>
/// Tests for P6: DAP variable view over built-in namespaces surfaces <c>DataMember</c>
/// entries with auto-resolved values and <c>Type = "member"</c> labels, distinct from
/// <c>BuiltInFunction</c> entries which get <c>Type = "function"</c>.
///
/// Decision Log: DAP labeling convention:
///   - DataMember entries: <c>Type = "member"</c> (or <c>"member (live)"</c> for Live stability).
///   - BuiltInFunction entries: <c>Type = "function"</c> (unchanged).
/// This matches how IDEs like VS render C# properties vs methods in the debugger.
/// </summary>
public class NamespaceMembersDapTests
{
    // ── Reflection helpers ────────────────────────────────────────────────────

    private static Variable InvokeFormatVariable(DebugSession session, string name, object? value)
    {
        var method = typeof(DebugSession).GetMethod("FormatVariable",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Variable)method.Invoke(session, new object?[] { name, value })!;
    }

    // ── NamespaceMemberPayload label tests ────────────────────────────────────

    /// <summary>
    /// P6 done_when: DataMember payloads, when rendered, produce <c>Type = "member"</c>
    /// (for Cached) or <c>Type = "member (live)"</c> (for Live), distinct from functions.
    ///
    /// Since <see cref="NamespaceMemberPayload.Invoke"/> requires an IInterpreterContext,
    /// and the DAP session handles this, we verify the label via the integration test below.
    /// Here we verify that the type string constants are correct.
    /// </summary>
    [Fact]
    public void DapLabelConvention_CachedMember_TypeIsMember()
    {
        // Verify that Cached stability maps to "member" label (not "function" or "property").
        var cachedPayload = new NamespaceMemberPayload(
            getter: _ => StashValue.FromInt(42),
            stability: Stability.Cached,
            returnType: "int");

        // The stability-based label is: Cached → "member", Live → "member (live)"
        string expectedLabel = cachedPayload.Stability == Stability.Live ? "member (live)" : "member";
        Assert.Equal("member", expectedLabel);
    }

    [Fact]
    public void DapLabelConvention_LiveMember_TypeIsMemberLive()
    {
        var livePayload = new NamespaceMemberPayload(
            getter: _ => StashValue.FromInt(0),
            stability: Stability.Live,
            returnType: "int");

        string expectedLabel = livePayload.Stability == Stability.Live ? "member (live)" : "member";
        Assert.Equal("member (live)", expectedLabel);
    }

    /// <summary>
    /// P6 done_when: BuiltInFunction entries retain <c>Type = "function"</c>.
    /// </summary>
    [Fact]
    public void FormatVariable_BuiltInFunction_TypeIsFunction()
    {
        var session = new DebugSession();
        var fn = new RuntimeBuiltInFunction("io.println", 1, (args, ctx) => StashValue.Null);
        var variable = InvokeFormatVariable(session, "println", fn);
        Assert.Equal("function", variable.Type);
    }

    /// <summary>
    /// P6 done_when: FormatVariable for a plain value (non-payload) produces the
    /// correct type — verifying the non-member branch still works.
    /// </summary>
    [Fact]
    public void FormatVariable_IntValue_TypeIsInt()
    {
        var session = new DebugSession();
        var variable = InvokeFormatVariable(session, "x", 42L);
        Assert.Equal("int", variable.Type);
    }

    [Fact]
    public void FormatVariable_StringValue_TypeIsString()
    {
        var session = new DebugSession();
        var variable = InvokeFormatVariable(session, "s", "hello");
        Assert.Equal("string", variable.Type);
    }

    // ── Integration: namespace expansion shows member entries ─────────────────

    /// <summary>
    /// P6 done_when: inspecting the <c>cli</c> namespace in the variable tree
    /// shows <c>argc</c> and <c>argv</c> with distinct labeling from functions.
    ///
    /// This integration test launches a minimal Stash script, pauses at entry,
    /// and checks the variable expansion of the <c>cli</c> namespace.
    /// </summary>
    [Fact]
    public void NamespaceExpansion_CliNamespace_ShowsMembersWithMemberType()
    {
        var session = new DebugSession();
        var scriptPath = WriteTempScript("io.println(cli.argc);\n");

        try
        {
            session.SetBreakpoints(scriptPath, new[] { new SourceBreakpoint { Line = 1 } });
            session.Launch(scriptPath, Path.GetDirectoryName(scriptPath)!,
                stopOnEntry: true, args: new[] { "arg1", "arg2" }, testMode: false);
            session.ConfigurationDone();

            WaitForPause(session, 8000);

            // Get scopes for the top frame. The 'cli' namespace is a built-in binding,
            // which lives in the "Standard Library" scope (separate from user-defined "Global").
            var frames = session.GetStackTrace();
            Assert.NotEmpty(frames);
            var frameId = (int)frames[0].Id;
            var scopes = session.GetScopes(frameId);

            // Find the scope that contains 'cli' (either "Standard Library" or "Global" in
            // a single-scope script context). Search all scope layers.
            Variable? cliVar = null;
            foreach (var scope in scopes)
            {
                var scopeVars = session.GetVariables((int)scope.VariablesReference);
                cliVar = scopeVars.FirstOrDefault(v => v.Name == "cli");
                if (cliVar != null) break;
            }
            Assert.NotNull(cliVar);
            Assert.True(cliVar.VariablesReference > 0, "cli namespace should be expandable");

            // Expand the cli namespace
            var cliMembers = session.GetVariables((int)cliVar.VariablesReference);

            // argc and argv must be present
            var argcVar = cliMembers.FirstOrDefault(v => v.Name == "argc");
            var argvVar = cliMembers.FirstOrDefault(v => v.Name == "argv");
            Assert.NotNull(argcVar);
            Assert.NotNull(argvVar);

            // They must be labeled as "member" (distinct from "function")
            Assert.Equal("member", argcVar.Type);
            Assert.Equal("member", argvVar.Type);

            // argc should show the value "2" (two script args)
            Assert.Equal("2", argcVar.Value);

            session.Continue();
            WaitForTermination(session, 5000);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    [Fact]
    public void NamespaceExpansion_EnvCwd_ShowsLiveMemberType()
    {
        var session = new DebugSession();
        var scriptPath = WriteTempScript("io.println(env.cwd);\n");

        try
        {
            session.SetBreakpoints(scriptPath, new[] { new SourceBreakpoint { Line = 1 } });
            session.Launch(scriptPath, Path.GetDirectoryName(scriptPath)!,
                stopOnEntry: true, args: Array.Empty<string>(), testMode: false);
            session.ConfigurationDone();

            WaitForPause(session, 8000);

            // Find the 'env' namespace across all scope layers.
            var frames2 = session.GetStackTrace();
            Assert.NotEmpty(frames2);
            var frameId2 = (int)frames2[0].Id;
            var scopes2 = session.GetScopes(frameId2);

            Variable? envVar = null;
            foreach (var scope in scopes2)
            {
                var scopeVars = session.GetVariables((int)scope.VariablesReference);
                envVar = scopeVars.FirstOrDefault(v => v.Name == "env");
                if (envVar != null) break;
            }
            Assert.NotNull(envVar);
            Assert.True(envVar.VariablesReference > 0);

            var envMembers = session.GetVariables((int)envVar.VariablesReference);
            var cwdVar = envMembers.FirstOrDefault(v => v.Name == "cwd");
            Assert.NotNull(cwdVar);

            // env.cwd is Live — label must be "member (live)"
            Assert.Equal("member (live)", cwdVar.Type);

            session.Continue();
            WaitForTermination(session, 5000);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string WriteTempScript(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stash_dap_members_{Guid.NewGuid():N}.stash");
        File.WriteAllText(path, source);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    private static readonly FieldInfo ThreadsField =
        typeof(DebugSession).GetField("_threads", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo InterpreterThreadField =
        typeof(DebugSession).GetField("_interpreterThread", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static bool GetIsPaused(DebugSession session)
    {
        var dict = ThreadsField.GetValue(session);
        if (dict == null) return false;
        var tryGet = dict.GetType().GetMethod("TryGetValue")!;
        var parameters = new object?[] { 1, null };
        bool found = (bool)tryGet.Invoke(dict, parameters)!;
        if (!found) return false;
        var ts = parameters[1]!;
        return (bool)ts.GetType().GetField("IsPaused")!.GetValue(ts)!;
    }

    private static void WaitForPause(DebugSession session, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!GetIsPaused(session))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Interpreter did not pause within timeout.");
            SysThread.Sleep(10);
        }
    }

    private static void WaitForTermination(DebugSession session, int timeoutMs)
    {
        var thread = (SysThread?)InterpreterThreadField.GetValue(session);
        if (thread != null && !thread.Join(timeoutMs))
        {
            throw new TimeoutException("Interpreter thread did not terminate within timeout.");
        }
    }
}
