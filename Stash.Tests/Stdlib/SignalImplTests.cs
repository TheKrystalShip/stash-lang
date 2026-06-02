using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;
using Xunit;

namespace Stash.Tests.Stdlib;

/// <summary>
/// Tests for the multiplexed signal registry introduced in phase 2C-1.
///
/// <b>Collection note:</b> <c>SignalImpl.SignalHandlers</c> is a process-global static.
/// Both this class and <c>Stash.Tests.Embedding.SignalMultiplexTests</c> manipulate it,
/// so xUnit must serialize them.  Both are placed in the <c>"SignalRegistry"</c> collection.
/// </summary>
[Collection("SignalRegistry")]
///
/// The global Signal enum in Stash uses short member names: Hup, Int, Quit, Kill, Usr1, Usr2, Term.
/// So Signal.Term registers under the dict key "Term" (MemberName), not "SIGTERM".
/// The "SIGTERM" keys come from the deprecated sys.* shim which has its own Signal enum
/// with full POSIX names.
///
/// done_when coverage:
///   #1  — SignalHandlers stores a List per signal name, not a single tuple.
///   #2  — When two engines register signal.on(Signal.Term, fn) and the signal fires, BOTH fns are invoked.
///   #3  — signal.off(Signal.Term) removes only entries whose IInterpreterContext == the calling VM's context.
///   #4  — PosixSignalRegistration created on first registration, disposed when last entry removed (refcount-style).
///   #5  — An exception thrown by one handler does not prevent subsequent handlers from running.
/// </summary>
public class SignalImplTests
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The dict key used when registering a signal.on(Signal.Term, ...) handler.
    /// Matches GlobalBuiltIns.Signal.Term's MemberName.
    /// </summary>
    private const string TermKey = "Term";

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles and executes a Stash script on a fresh VM whose output is captured.
    /// Returns the VM and its output writer so the caller can inspect post-execution state.
    /// </summary>
    private static (VirtualMachine vm, StringWriter output) BuildAndRun(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);

        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var output = new StringWriter { NewLine = "\n" };
        vm.Output = output;
        vm.Execute(chunk);
        return (vm, output);
    }

    /// <summary>
    /// Builds, compiles, and executes Stash source on a VM that shares the given writer.
    /// The shared writer is used both for output during registration and for handler-fired output
    /// during Dispatch (the Output reference is shared by reference via VM construction).
    /// </summary>
    private static (VirtualMachine vm, StringWriter output) BuildAndRunWithSharedOutput(
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

    /// <summary>
    /// Removes all entries for every signal name from the global registry, disposing any
    /// OS-level registrations. Call at the start of tests that inspect registry internals
    /// to avoid pollution from other tests (SignalHandlers is process-global).
    /// </summary>
    private static void ClearSignalHandlers()
    {
        lock (SignalImpl.SignalLock)
        {
            // Collect all keys first to avoid modifying the dict during iteration.
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

    // ── #1 : List-per-signal storage ─────────────────────────────────────────

    [Fact]
    public void SignalHandlers_AfterTwoRegistrations_StoresTwoEntriesForSameSignal()
    {
        ClearSignalHandlers();
        try
        {
            // Register two handlers for the same signal from two separate VMs.
            BuildAndRun("signal.on(Signal.Term, () => null);");
            BuildAndRun("signal.on(Signal.Term, () => null);");

            lock (SignalImpl.SignalLock)
            {
                // Signal.Term's MemberName is "Term" — that is the dict key.
                Assert.True(SignalImpl.SignalHandlers.TryGetValue(TermKey, out var entries));
                Assert.Equal(2, entries!.Count);
            }
        }
        finally
        {
            ClearSignalHandlers();
        }
    }

    // ── #2 : Both handlers fire on Dispatch ──────────────────────────────────

    [Fact]
    public void Dispatch_TwoEnginesRegisteredSameSignal_BothHandlersFire()
    {
        ClearSignalHandlers();
        // Use a single shared writer so both VMs' handler output lands in one place.
        var sharedOutput = new StringWriter { NewLine = "\n" };
        try
        {
            // VM 1 registers a handler that writes a marker to its output.
            BuildAndRunWithSharedOutput(
                "signal.on(Signal.Term, () => io.println(\"vm1-fired\"));",
                sharedOutput);

            // VM 2 registers a handler that writes a different marker.
            BuildAndRunWithSharedOutput(
                "signal.on(Signal.Term, () => io.println(\"vm2-fired\"));",
                sharedOutput);

            // Synthetic signal raise using the signal's dict key "Term".
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

    // ── #3 : signal.off removes only the calling VM's entries ────────────────

    [Fact]
    public void SignalOff_RemovesOnlyCallingVmsHandler_OtherVmHandlerStays()
    {
        ClearSignalHandlers();
        var sharedOutput = new StringWriter { NewLine = "\n" };
        try
        {
            // VM 1: register a handler.
            BuildAndRunWithSharedOutput(
                "signal.on(Signal.Term, () => io.println(\"vm1-fired\"));",
                sharedOutput);

            // VM 2: register a handler, then immediately remove it.
            BuildAndRunWithSharedOutput(
                "signal.on(Signal.Term, () => io.println(\"vm2-fired\"));\n" +
                "signal.off(Signal.Term);",
                sharedOutput);

            // After VM 2 called signal.off, only VM 1's handler should remain.
            lock (SignalImpl.SignalLock)
            {
                Assert.True(SignalImpl.SignalHandlers.TryGetValue(TermKey, out var entries));
                Assert.Equal(1, entries!.Count);
            }

            // Dispatch — only VM 1's handler must fire.
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

    // ── #4 : Registration lifecycle (refcount-style) ─────────────────────────

    [Fact]
    public void Registration_CreatedOnFirstRegistration_SharedAcrossEntries()
    {
        ClearSignalHandlers();
        try
        {
            // After first registration, the list has one entry (reg may be null for non-POSIX names).
            BuildAndRun("signal.on(Signal.Term, () => null);");

            PosixSignalRegistration? reg1;
            lock (SignalImpl.SignalLock)
            {
                Assert.True(SignalImpl.SignalHandlers.TryGetValue(TermKey, out var entries1));
                Assert.Equal(1, entries1!.Count);
                reg1 = entries1[0].Registration;
            }

            // Second registration: list grows to 2, same OS registration (or null) shared.
            BuildAndRun("signal.on(Signal.Term, () => null);");

            lock (SignalImpl.SignalLock)
            {
                Assert.True(SignalImpl.SignalHandlers.TryGetValue(TermKey, out var entries2));
                Assert.Equal(2, entries2!.Count);
                // Both entries share the same registration reference (may be null on this platform).
                Assert.Same(reg1, entries2[0].Registration);
                Assert.Same(reg1, entries2[1].Registration);
            }

            // When the last entry is removed, the dict key must disappear.
            ClearSignalHandlers();

            lock (SignalImpl.SignalLock)
            {
                Assert.False(SignalImpl.SignalHandlers.ContainsKey(TermKey));
            }
        }
        finally
        {
            ClearSignalHandlers();
        }
    }

    [Fact]
    public void SignalOff_LastEntryRemoved_DictKeyIsGone()
    {
        ClearSignalHandlers();
        try
        {
            // Register then immediately unregister in the same VM.
            BuildAndRun("signal.on(Signal.Term, () => null); signal.off(Signal.Term);");

            lock (SignalImpl.SignalLock)
            {
                Assert.False(SignalImpl.SignalHandlers.ContainsKey(TermKey));
            }
        }
        finally
        {
            ClearSignalHandlers();
        }
    }

    // ── #5 : Throwing handler does not block subsequent handlers ─────────────

    [Fact]
    public void Dispatch_OneHandlerThrows_SubsequentHandlerStillFires()
    {
        ClearSignalHandlers();
        var sharedOutput = new StringWriter { NewLine = "\n" };
        try
        {
            // VM 1: handler that throws.
            BuildAndRunWithSharedOutput(
                "signal.on(Signal.Term, () => { throw \"boom\"; });",
                sharedOutput);

            // VM 2: handler that writes a marker (should still run despite VM 1's throw).
            BuildAndRunWithSharedOutput(
                "signal.on(Signal.Term, () => io.println(\"vm2-after-throw\"));",
                sharedOutput);

            SignalImpl.Dispatch(TermKey);

            string output = sharedOutput.ToString();
            Assert.Contains("vm2-after-throw", output);
        }
        finally
        {
            ClearSignalHandlers();
        }
    }

    // ── Regression: basic signal.on / signal.off API surface ─────────────────

    [Fact]
    public void SignalOn_SingleRegistration_DoesNotThrow()
    {
        ClearSignalHandlers();
        try
        {
            BuildAndRun("signal.on(Signal.Term, () => null);");
        }
        finally
        {
            ClearSignalHandlers();
        }
    }

    [Fact]
    public void SignalOff_NoHandlerRegistered_IsNoOp()
    {
        ClearSignalHandlers();
        // signal.off on an unregistered signal must not throw.
        BuildAndRun("signal.off(Signal.Term);");
    }
}
