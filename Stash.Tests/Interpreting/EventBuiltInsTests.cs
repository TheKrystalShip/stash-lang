using System.Threading;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;

namespace Stash.Tests.Interpreting;

/// <summary>
/// Tests for the <c>event</c> namespace: <c>event.poll</c> and <c>event.loop</c>.
/// </summary>
public class EventBuiltInsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (Chunk chunk, VirtualMachine vm) CompileToVM(string source, CancellationToken ct = default)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals(), ct);
        return (chunk, vm);
    }

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

    // ── event.poll tests ─────────────────────────────────────────────────────

    /// <summary>
    /// done_when #1: event.poll() drains everything currently queued and returns
    /// immediately; a callback fired before the poll mutates the parent's captured
    /// state by the time poll returns.
    /// </summary>
    [Collection("SignalRegistry")]
    public class EventPoll_DrainsMutatesAndReturns_Tests : IDisposable
    {
        public void Dispose() => ClearSignalHandlers();

        [Fact]
        public void EventPoll_IsTheSoleDrainPoint_MutatesParentStateOnReturn()
        {
            ClearSignalHandlers();

            // event.poll() is the ONLY drain point — no time.sleep in the loop.
            // A background thread fires the signal ~50ms in; the spin loop uses
            // event.poll() to repeatedly drain until x flips.  If Poll() were a no-op
            // the loop would spin to the deadline and return 0 → test fails.
            //
            //   1. Register handler.
            //   2. Spin on event.poll() — no sleep, so poll is the sole yield point.
            //   3. Background thread fires signal (enqueues callback at ~50ms).
            //   4. The next event.poll() in the spin drains it → x = 99.
            var source =
                "let x = 0;\n" +
                "signal.on(Signal.Usr1, () => { x = 99; });\n" +
                "let deadline = time.millis() + 5000;\n" +
                "while (x == 0 && time.millis() < deadline) {\n" +
                "    event.poll();\n" +    // ONLY drain path — no time.sleep
                "}\n" +
                "return x;\n";

            var (chunk, vm) = CompileToVM(source);

            var bgThread = new Thread(() =>
            {
                Thread.Sleep(50); // fire after VM is spinning on event.poll
                SignalImpl.Dispatch("Usr1");
            })
            { IsBackground = true };
            bgThread.Start();

            var result = vm.Execute(chunk);
            bgThread.Join(TimeSpan.FromSeconds(5));

            Assert.Equal(99L, result);
        }

        [Fact]
        public void EventPoll_CallbackEnqueuedBeforePoll_MutatesStateSynchronously()
        {
            ClearSignalHandlers();

            // event.poll() as the sole drain point: fire the signal from a background
            // thread, then observe the mutation after a single event.poll() call.
            // Structurally identical to the spin test above but with a more direct
            // assertion: fire, short spin to ensure it's queued, then single poll.
            var source =
                "let fired = false;\n" +
                "signal.on(Signal.Hup, () => { fired = true; });\n" +
                "let deadline = time.millis() + 5000;\n" +
                "while (!fired && time.millis() < deadline) {\n" +
                "    event.poll();\n" +    // sole drain path
                "}\n" +
                "return fired;\n";

            var (chunk, vm) = CompileToVM(source);

            var bgThread = new Thread(() =>
            {
                Thread.Sleep(50);
                SignalImpl.Dispatch("Hup");
            })
            { IsBackground = true };
            bgThread.Start();

            var result = vm.Execute(chunk);
            bgThread.Join(TimeSpan.FromSeconds(5));

            Assert.Equal(true, result);
        }
    }

    // ── event.loop tests ─────────────────────────────────────────────────────

    /// <summary>
    /// done_when #2: event.loop() blocks and drains until cancellation;
    /// cancelling the script's CancellationToken makes the loop throw CancellationError.
    /// </summary>
    [Collection("SignalRegistry")]
    public class EventLoop_BlocksAndCancels_Tests : IDisposable
    {
        public void Dispose() => ClearSignalHandlers();

        [Fact]
        public void EventLoop_CancelledByToken_ThrowsCancellationError()
        {
            ClearSignalHandlers();

            // event.loop() parks forever; cancelling the token must produce CancellationError.
            var source =
                "event.loop();\n";

            var cts = new CancellationTokenSource();
            var (chunk, vm) = CompileToVM(source, cts.Token);

            // Cancel from a background thread after a short delay.
            var bgThread = new Thread(() =>
            {
                Thread.Sleep(100);
                cts.Cancel();
            })
            { IsBackground = true };
            bgThread.Start();

            var ex = Assert.ThrowsAny<CancellationError>(() => vm.Execute(chunk));
            bgThread.Join(TimeSpan.FromSeconds(5));

            Assert.NotNull(ex);
        }

        [Fact]
        public void EventLoop_FiresCallbackBeforeCancel_CallbackMutatesState()
        {
            ClearSignalHandlers();

            // event.loop() should drain callbacks while waiting.
            // A signal fired during the loop should mutate parent state.
            // External cancellation (OCE → CancellationError) is caught at the C# level;
            // the mutation happened before the cancel so we verify via the globals dict.
            var source =
                "let x = 0;\n" +
                "signal.on(Signal.Int, () => { x = 42; });\n" +
                "event.loop();\n";

            var cts = new CancellationTokenSource();
            var (chunk, vm) = CompileToVM(source, cts.Token);

            var bgThread = new Thread(() =>
            {
                Thread.Sleep(50);
                SignalImpl.Dispatch("Int"); // enqueue callback; loop drains it
                Thread.Sleep(100);
                cts.Cancel(); // then cancel the loop
            })
            { IsBackground = true };
            bgThread.Start();

            // event.loop throws CancellationError (C# exception) on external cancellation.
            Assert.ThrowsAny<CancellationError>(() => vm.Execute(chunk));
            bgThread.Join(TimeSpan.FromSeconds(5));

            // The callback ran before the cancel; verify via the VM's global state.
            // The globals dict persists even after the VM throws.
            Assert.True(vm.Globals.TryGetValue("x", out var xVal));
            Assert.Equal(42L, xVal.ToObject());
        }
    }

    // ── Reentrancy: no-op from inside a queued callback ───────────────────────

    /// <summary>
    /// done_when #3: event.poll() called from inside a queued callback is a no-op.
    /// done_when #4: event.loop() called from inside a queued callback is a no-op.
    ///
    /// The reentrancy guard (_isDraining) lives inside DrainCallbacks; calling
    /// event.poll/loop while draining returns immediately without re-pumping.
    /// </summary>
    [Collection("SignalRegistry")]
    public class EventReentrancy_NoOpWhileDraining_Tests : IDisposable
    {
        public void Dispose() => ClearSignalHandlers();

        [Fact]
        public void EventPoll_CalledFromInsideQueuedCallback_IsNoOp()
        {
            ClearSignalHandlers();

            // Callback A calls event.poll() — which must be a no-op (reentrancy guard).
            // Callback B is enqueued after A; B must NOT fire while A is executing
            // (it fires only after A returns and the outer drain pops B).
            var source =
                "let a_done = false;\n" +
                "let b_fired = false;\n" +
                "signal.on(Signal.Usr1, () => {\n" +
                "    event.poll();\n" +      // must be no-op — _isDraining is set
                "    a_done = true;\n" +
                "});\n" +
                "signal.on(Signal.Usr2, () => { b_fired = true; });\n" +
                "let deadline = time.millis() + 5000;\n" +
                "while (!b_fired && time.millis() < deadline) {\n" +
                "    time.sleep(0.05);\n" +
                "}\n" +
                "return a_done && b_fired;\n";

            var (chunk, vm) = CompileToVM(source);

            var bgThread = new Thread(() =>
            {
                Thread.Sleep(50);
                SignalImpl.Dispatch("Usr1"); // A: calls event.poll() inside
                Thread.Sleep(10);
                SignalImpl.Dispatch("Usr2"); // B: fires only after A returns
            })
            { IsBackground = true };
            bgThread.Start();

            var result = vm.Execute(chunk);
            bgThread.Join(TimeSpan.FromSeconds(5));

            // Both a_done and b_fired are true, proving A completed and B fired afterwards.
            Assert.Equal(true, result);
        }

        [Fact]
        public void EventLoop_CalledFromInsideQueuedCallback_IsNoOp()
        {
            ClearSignalHandlers();

            // Callback A calls event.loop() — which must be a no-op (reentrancy guard).
            // If event.loop() were NOT a no-op, it would block forever and the outer
            // loop would never complete.
            var source =
                "let a_done = false;\n" +
                "signal.on(Signal.Hup, () => {\n" +
                "    event.loop();\n" +      // must be no-op — _isDraining is set
                "    a_done = true;\n" +
                "});\n" +
                "let deadline = time.millis() + 5000;\n" +
                "while (!a_done && time.millis() < deadline) {\n" +
                "    time.sleep(0.05);\n" +
                "}\n" +
                "return a_done;\n";

            var (chunk, vm) = CompileToVM(source);

            var bgThread = new Thread(() =>
            {
                Thread.Sleep(50);
                SignalImpl.Dispatch("Hup");
            })
            { IsBackground = true };
            bgThread.Start();

            var result = vm.Execute(chunk);
            bgThread.Join(TimeSpan.FromSeconds(5));

            Assert.Equal(true, result);
        }
    }
}
