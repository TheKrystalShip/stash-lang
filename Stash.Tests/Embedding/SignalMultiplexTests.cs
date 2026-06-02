using System.Collections.Generic;
using System.IO;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;
using Xunit;

namespace Stash.Tests.Embedding;

/// <summary>
/// Acceptance test for signal multiplexing across two engines.
///
/// done_when coverage:
///   #6 — SigTerm_TwoEngines_BothHandlersRun
///
/// NOTE: Signal handlers are registered in the process-global
/// <c>SignalImpl.SignalHandlers</c> registry.  Tests must clear the registry
/// before and after using <c>ClearSignalHandlers()</c> to avoid cross-test pollution.
/// </summary>
public class SignalMultiplexTests
{
    // ── constants ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The dict key for Signal.Term as registered by signal.on(Signal.Term, ...).
    /// Matches GlobalBuiltIns.Signal.Term's MemberName ("Term"), NOT "SIGTERM".
    /// </summary>
    private const string TermKey = "Term";

    // ── helpers ───────────────────────────────────────────────────────────────

    private static (VirtualMachine vm, StringWriter output) BuildAndRunWithOutput(
        string source, StringWriter sharedOutput)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);

        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = sharedOutput;
        vm.Execute(chunk);
        return (vm, sharedOutput);
    }

    private static void ClearSignalHandlers()
    {
        lock (SignalImpl.SignalLock)
        {
            var keys = new List<string>(SignalImpl.SignalHandlers.Keys);
            foreach (var key in keys)
            {
                if (SignalImpl.SignalHandlers.TryRemove(key, out var entries) && entries.Count > 0)
                {
                    entries[0].Registration?.Dispose();
                }
            }
        }
    }

    // ── #6: both handlers run on SIGTERM; signal.off leaves the other intact ──

    /// <summary>
    /// Two VMs each register signal.on(Signal.Term, ...).
    /// When SIGTERM fires (via synthetic <c>SignalImpl.Dispatch</c>), both handlers run.
    /// Then signal.off in VM-A removes only VM-A's handler; a second dispatch only fires VM-B.
    /// </summary>
    [Fact]
    public void SigTerm_TwoEngines_BothHandlersRun()
    {
        ClearSignalHandlers();
        var sharedOutput = new StringWriter { NewLine = "\n" };
        try
        {
            // VM 1 registers a handler that writes "vm1-fired".
            BuildAndRunWithOutput(
                "signal.on(Signal.Term, () => io.println(\"vm1-fired\"));",
                sharedOutput);

            // VM 2 registers a handler that writes "vm2-fired".
            BuildAndRunWithOutput(
                "signal.on(Signal.Term, () => io.println(\"vm2-fired\"));",
                sharedOutput);

            // Both handlers should be registered.
            lock (SignalImpl.SignalLock)
            {
                Assert.True(SignalImpl.SignalHandlers.TryGetValue(TermKey, out var entries));
                Assert.Equal(2, entries!.Count);
            }

            // Synthetic signal dispatch — BOTH handlers must fire.
            SignalImpl.Dispatch(TermKey);

            string output = sharedOutput.ToString();
            Assert.Contains("vm1-fired", output);
            Assert.Contains("vm2-fired", output);
        }
        finally
        {
            ClearSignalHandlers();
        }
    }

    /// <summary>
    /// signal.off in one VM removes only that VM's handler.
    /// A subsequent dispatch fires only the remaining VM's handler.
    /// </summary>
    [Fact]
    public void SigTerm_SignalOff_InOneEngine_LeavesOtherEngineHandler()
    {
        ClearSignalHandlers();
        var sharedOutput = new StringWriter { NewLine = "\n" };
        try
        {
            // VM 1 registers a handler.
            BuildAndRunWithOutput(
                "signal.on(Signal.Term, () => io.println(\"vm1-fired\"));",
                sharedOutput);

            // VM 2 registers, then immediately unregisters.
            BuildAndRunWithOutput(
                "signal.on(Signal.Term, () => io.println(\"vm2-fired\"));\n" +
                "signal.off(Signal.Term);",
                sharedOutput);

            // After VM-2's signal.off, only VM-1's handler should remain.
            lock (SignalImpl.SignalLock)
            {
                Assert.True(SignalImpl.SignalHandlers.TryGetValue(TermKey, out var entries));
                Assert.Equal(1, entries!.Count);
            }

            // Dispatch — only vm1-fired should appear (vm2 is unregistered).
            sharedOutput.GetStringBuilder().Clear();
            SignalImpl.Dispatch(TermKey);

            string output = sharedOutput.ToString();
            Assert.Contains("vm1-fired", output);
            Assert.DoesNotContain("vm2-fired", output);
        }
        finally
        {
            ClearSignalHandlers();
        }
    }
}
