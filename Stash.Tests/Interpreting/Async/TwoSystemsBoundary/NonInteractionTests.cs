namespace Stash.Tests.Interpreting.Async.TwoSystemsBoundary;

using System.IO;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Tests.Interpreting;

/// <summary>
/// D11 — Two-systems non-interaction: the Futures system (System A) and the event-queue
/// system (System B) do not bridge.
///
/// Positive contract (D11):
///   - <c>event.poll()</c> does NOT advance an async-fn Future. A Future that is still
///     Running stays Running after event.poll() returns.
///   - <c>await</c> does NOT drain the event-queue. A queued callback fires only at the
///     next true park point (time.sleep / event.poll / event.loop).
/// </summary>
public class NonInteractionTests : StashTestBase
{
    // ── Part 1: event.poll() does NOT advance a Future ────────────────────────

    /// <summary>
    /// Spawn a long-running task (10s sleep), call event.poll(), then assert the Future
    /// is still Running. event.poll() is System-B machinery — it cannot wake a System-A
    /// Future. Cancel after the assertion to avoid leaving a live thread.
    /// </summary>
    [Fact]
    public void TwoSystemsBoundary_EventPoll_DoesNotAdvanceFuture()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); return ""done""; });
// At this point f is Running.
event.poll();
// event.poll() drains the B-queue; it must NOT advance the A-future.
let statusAfterPoll = task.status(f);
task.cancel(f);
let result = statusAfterPoll;
");
        // The future must still be Running after event.poll() (System-B operation cannot
        // advance a System-A thread-pool task).
        var status = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Running", status.MemberName);
    }

    [Fact]
    public void TwoSystemsBoundary_MultipleEventPolls_DoNotAdvanceFuture()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); return ""done""; });
event.poll();
event.poll();
event.poll();
let statusAfterPolls = task.status(f);
task.cancel(f);
let result = statusAfterPolls;
");
        var status = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Running", status.MemberName);
    }

    // ── Part 2: await does NOT drain the event-queue ──────────────────────────

    /// <summary>
    /// Programmatically enqueue a callback (System B) before awaiting a Future (System A).
    /// After the await returns, the callback must still be pending — because await is a
    /// System-A join and never drains the B-queue.
    /// Only after a real drain point (event.poll) does the callback fire.
    ///
    /// Uses VMContext.EnqueueCallback directly for deterministic scheduling — no OS-level
    /// file-watch timing involved.
    ///
    /// InternalsVisibleTo: Stash.Bytecode → Stash.Tests allows accessing internal VMContext.
    /// </summary>
    [Fact]
    public void TwoSystemsBoundary_Await_DoesNotDrainEventQueue()
    {
        // Build the VM manually so we can reach VMContext.EnqueueCallback.
        var lexer = new Lexer(@"
let f = task.run(() => 42);
let result = await f;
", "<test>");
        var tokens = lexer.ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());

        // Install a callback that flips a flag when it runs.
        bool callbackFired = false;
        var flagCallback = new BuiltInFunction("_flag", 0, (ctx, args) =>
        {
            callbackFired = true;
            return StashValue.Null;
        });

        // Enqueue the callback BEFORE the script executes — this simulates an fs.watch
        // event that was detected but not yet delivered.
        var vmContext = (VMContext)vm.Context;
        vmContext.EnqueueCallback(flagCallback, []);

        // Execute the script — it awaits a Future (System A) but must NOT drain B-queue.
        vm.Execute(chunk);

        // After script execution the callback is still NOT fired (await didn't drain).
        Assert.False(callbackFired,
            "await must not drain the event queue (System-B). " +
            "The callback was fired by await, which crosses the two-systems boundary.");
    }

    /// <summary>
    /// After the await (which must NOT drain), a subsequent event.poll() DOES fire the
    /// queued callback — confirming it was queued and not lost, just not drained by await.
    /// </summary>
    [Fact]
    public void TwoSystemsBoundary_EventPollAfterAwait_FiresQueuedCallback()
    {
        // Build the VM manually.
        var lexer = new Lexer(@"
let f = task.run(() => 42);
await f;
event.poll();
", "<test>");
        var tokens = lexer.ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());

        bool callbackFired = false;
        var flagCallback = new BuiltInFunction("_flag", 0, (ctx, args) =>
        {
            callbackFired = true;
            return StashValue.Null;
        });

        var vmContext = (VMContext)vm.Context;
        vmContext.EnqueueCallback(flagCallback, []);

        // Execute: await (must not drain) then event.poll() (must drain).
        vm.Execute(chunk);

        // After event.poll() the callback should have fired.
        Assert.True(callbackFired,
            "event.poll() must drain queued callbacks — the queued callback was not fired.");
    }

    // ── Positive-contract baseline: System A works independently of System B ──

    [Fact]
    public void TwoSystemsBoundary_AwaitResolvesCorrectly_WithoutEventPoll()
    {
        // Confirm that await works without any event.poll() — System A is independent.
        var result = Run(@"
let f = task.run(() => { return 42; });
let result = await f;
");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void TwoSystemsBoundary_EventPollWithNoWatchers_DoesNotThrow()
    {
        // event.poll() with empty queue must not throw — it simply returns immediately.
        RunStatements(@"event.poll();");
    }
}
