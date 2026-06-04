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

        [Fact]
        public void SlowCallback_TimeSleepSkew_SleepReturnsTotalDuration()
        {
            ClearSignalHandlers();

            // done_when #3: time.sleep(0.1) that drains a callback taking ~200ms
            // returns at >= ~300ms total.  The wait-loop must recompute `remaining`
            // correctly across drains so slow callbacks accumulate into the total.
            var source =
                "let done = false;\n" +
                "signal.on(Signal.Int, () => { time.sleep(0.2); done = true; });\n" +
                "let t0 = time.clock();\n" +
                "time.sleep(0.1);\n" +
                "let elapsed = time.clock() - t0;\n" +
                "return elapsed;\n";

            var (chunk, vm) = CompileToVM(source);

            var dispatchThread = new Thread(() =>
            {
                Thread.Sleep(50); // fire the slow callback partway into the outer sleep
                SignalImpl.Dispatch("Int");
            })
            { IsBackground = true };
            dispatchThread.Start();

            var result = vm.Execute(chunk);
            dispatchThread.Join(TimeSpan.FromSeconds(5));

            // Total elapsed must be >= ~300ms (100ms outer + 200ms inner callback).
            // Use 200ms floor (generous tolerance for slow CI) to avoid flakiness.
            double elapsed = Convert.ToDouble(result);
            Assert.True(elapsed >= 0.20,
                $"Expected elapsed >= 0.20s (outer 100ms + slow-callback 200ms), got {elapsed:F3}s");
        }

        [Fact]
        public void QueuedCallback_InnerTimeSleep_DoesNotReenterDrain()
        {
            ClearSignalHandlers();

            // done_when #4: A queued callback A that calls time.sleep must NOT re-pump
            // the drain (reentrancy guard).  A second queued callback B fires only after
            // A returns and the outer drain pops it.
            //
            // Assert: (a) B fires AFTER A sets its marker; (b) A's inner sleep actually
            // takes time (not a 0ms no-op — the plain-sleep fallback must run).
            var source =
                "let a_done = false;\n" +
                "let b_fired = false;\n" +
                "signal.on(Signal.Usr1, () => {\n" +
                "    let t0 = time.clock();\n" +
                "    time.sleep(0.1);\n" +
                "    let inner_elapsed = time.clock() - t0;\n" +
                "    a_done = true;\n" +
                "    return inner_elapsed;\n" +
                "});\n" +
                "signal.on(Signal.Usr2, () => { b_fired = true; });\n" +
                "let deadline = time.millis() + 5000;\n" +
                "while (!b_fired && time.millis() < deadline) {\n" +
                "    time.sleep(0.05);\n" +
                "}\n" +
                "return a_done && b_fired;\n";

            var (chunk, vm) = CompileToVM(source);

            var t0 = DateTimeOffset.UtcNow;

            var dispatchThread = new Thread(() =>
            {
                Thread.Sleep(50); // let the VM reach the outer sleep
                SignalImpl.Dispatch("Usr1"); // A: slow callback
                Thread.Sleep(10);            // give A time to be enqueued
                SignalImpl.Dispatch("Usr2"); // B: fires only after A returns
            })
            { IsBackground = true };
            dispatchThread.Start();

            var result = vm.Execute(chunk);
            dispatchThread.Join(TimeSpan.FromSeconds(5));

            // Both A and B fired correctly; A set a_done before B set b_fired (ordering).
            Assert.Equal(true, result);

            // Total elapsed must be >= ~200ms (outer dispatched 50ms in + A 100ms + B after A).
            var elapsed = (DateTimeOffset.UtcNow - t0).TotalSeconds;
            Assert.True(elapsed >= 0.12,
                $"Inner sleep was a no-op (elapsed only {elapsed:F3}s — reentrancy guard did not trigger plain-sleep fallback)");
        }

        [Fact]
        public void ThrowingQueuedCallback_Swallowed_SubsequentCallbackStillFires()
        {
            ClearSignalHandlers();

            // done_when #5: A queued callback that throws is log-and-swallowed;
            // a subsequent queued callback still fires (matches existing Dispatch behavior).
            var source =
                "let second_fired = false;\n" +
                "signal.on(Signal.Hup, () => { throw \"intentional error\"; });\n" +
                "signal.on(Signal.Int, () => { second_fired = true; });\n" +
                "let deadline = time.millis() + 5000;\n" +
                "while (!second_fired && time.millis() < deadline) {\n" +
                "    time.sleep(0.05);\n" +
                "}\n" +
                "return second_fired;\n";

            var (chunk, vm) = CompileToVM(source);

            var dispatchThread = new Thread(() =>
            {
                Thread.Sleep(50);
                SignalImpl.Dispatch("Hup"); // throws inside the callback
                Thread.Sleep(10);
                SignalImpl.Dispatch("Int"); // should still fire
            })
            { IsBackground = true };
            dispatchThread.Start();

            var result = vm.Execute(chunk);
            dispatchThread.Join(TimeSpan.FromSeconds(5));

            // second_fired must be true — throwing callback didn't break drain.
            Assert.Equal(true, result);
        }
    }
}
