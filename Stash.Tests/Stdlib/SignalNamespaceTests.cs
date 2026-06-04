using System.Threading;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;

namespace Stash.Tests.Stdlib;

/// <summary>
/// Tests for the <c>signal</c> namespace: signal.on, signal.off,
/// and the deprecated sys.onSignal / sys.offSignal shims.
/// </summary>
public class SignalNamespaceTests : Stash.Tests.Interpreting.StashTestBase
{
    [Fact]
    public void SignalOn_Term_RegistersWithoutThrowing()
    {
        // signal.on stores the handler. PosixSignalRegistration may not fire
        // in test context, but the call itself must not throw.
        RunStatements("signal.on(Signal.Term, () => null);");
    }

    [Fact]
    public void SignalOff_Term_RemovesWithoutThrowing()
    {
        // off on a signal with no existing handler is a no-op — must not throw.
        RunStatements("signal.off(Signal.Term);");
    }

    [Fact]
    public void SysOnSignal_DeprecatedAlias_RegistersWithoutThrowing()
    {
        // The deprecated sys.onSignal shim delegates to SignalImpl.OnSignal.
        RunStatements("sys.onSignal(Signal.Hup, () => null);");
    }

    [Fact]
    public void SysOffSignal_DeprecatedAlias_RemovesWithoutThrowing()
    {
        // The deprecated sys.offSignal shim delegates to SignalImpl.OffSignal.
        RunStatements("sys.offSignal(Signal.Hup);");
    }

    // ── Shared-mutation via queued delivery ───────────────────────────────────

    /// <summary>
    /// A signal.on handler that sets a parent <c>let stop = false</c> flips it to
    /// <c>true</c> for the parent loop after the next <c>time.sleep</c> returns.
    ///
    /// This is the canonical graceful-shutdown pattern and documents the core
    /// callback-marshaling guarantee: the queued callback runs inline on the VM thread
    /// at the next drain point, so mutations ARE visible to the parent.
    /// </summary>
    [Collection("SignalRegistry")]
    public class SignalSharedMutationTests : IDisposable
    {
        public void Dispose() => ClearSignalHandlers();

        private static void ClearSignalHandlers()
        {
            lock (SignalImpl.SignalLock)
            {
                var keys = new System.Collections.Generic.List<string>(SignalImpl.SignalHandlers.Keys);
                foreach (var key in keys)
                    if (SignalImpl.SignalHandlers.TryRemove(key, out var entries) && entries.Count > 0)
                        entries[0].Registration?.Dispose();
            }
        }

        private static (Chunk chunk, VirtualMachine vm) CompileToVM(string source)
        {
            var tokens = new Lexer(source, "<test>").ScanTokens();
            var stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            var chunk = Compiler.Compile(stmts);
            var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
            return (chunk, vm);
        }

        [Fact]
        public void SignalOn_Handler_FlipsParentFlagAfterTimeSleepDrains()
        {
            ClearSignalHandlers();

            // Graceful-shutdown pattern: signal handler sets stop = true;
            // the parent loop observes the flip after the next time.sleep returns.
            var source =
                "let stop = false;\n" +
                "signal.on(Signal.Hup, () => { stop = true; });\n" +
                "let deadline = time.millis() + 5000;\n" +
                "while (!stop && time.millis() < deadline) {\n" +
                "    time.sleep(0.05);\n" +
                "}\n" +
                "return stop;\n";

            var (chunk, vm) = CompileToVM(source);

            // Dispatch SIGHUP from a background thread shortly after the VM starts.
            var dispatchThread = new Thread(() =>
            {
                Thread.Sleep(100); // let the VM reach the sleep loop
                SignalImpl.Dispatch("Hup");
            })
            { IsBackground = true };
            dispatchThread.Start();

            var result = vm.Execute(chunk);

            dispatchThread.Join(TimeSpan.FromSeconds(5));

            // stop was flipped to true by the queued signal handler.
            Assert.Equal(true, result);
        }
    }
}
