using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Async;

/// <summary>
/// Conformance tests for §Async — Two-systems model, D5 (Process handle boundary)
/// and D11 (event-queue vs Futures non-interaction).
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clauses of
/// <c>docs/Stash — Language Specification.md</c> §Async, specifically:
/// </para>
/// <list type="bullet">
///   <item><b>D5 — Process handle boundary</b>: Using a parent's
///     <c>process.spawn()</c> handle inside a child task throws <c>StateError</c> with a
///     message that names the cross-task boundary. The enforcement applies to all
///     <c>process.*</c> operations that consume a <c>Process</c> handle.
///     <para>
///     Note: D5 is <b>intended</b> to apply equally to socket handles
///     (<c>TcpConnection</c>, <c>TcpServer</c>, etc.), but socket enforcement is
///     <b>not yet built</b>. Cross-task socket-handle use is unsupported and unsafe
///     (silent data corruption; wrong error type on the async path). This is a known,
///     tracked gap — see
///     <c>.kanban/0-backlog/bugs/tcp-socket-handle-task-boundary-enforcement.md</c>
///     for the planned enforcement work. Only Process handle enforcement is proven here.</para></item>
///   <item><b>D11 — Two-systems non-interaction</b> (L1440–1442, L1661+):
///     <list type="bullet">
///       <item><c>event.poll()</c> does NOT advance a Future (System A); the Future's status
///         remains <c>task.Status.Running</c> after the poll returns.</item>
///       <item><c>await</c> does NOT drain the event queue (System B); a callback enqueued
///         before an <c>await</c> is not fired by the await — only by a subsequent drain
///         point (<c>event.poll()</c>).</item>
///     </list></item>
/// </list>
///
/// <para>
/// These are conformance tests — they prove the <em>spec</em>, not guard implementation
/// regressions. The existing behavior suite at <c>Stash.Tests/Interpreting/Async/TwoSystemsBoundary/</c>
/// remains in place for regression coverage; the two suites are complementary.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class TwoSystemsConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────────
    // D5 — Process handle boundary
    // Spec: §Async "Process handle boundary (D5)" ~L1543–1549
    // ─────────────────────────────────────────────────────────────────────────────

    // OS-guard: process.spawn is Unix-only in these tests (spawning 'sleep' / 'echo').
    // The guard mirrors the CrossVmHandleTests pattern. Tests are registered on all
    // platforms; the early return prevents execution on Windows (where 'sleep' is not
    // available) without counting as a skipped test.

    /// <summary>
    /// D5: Using a parent's <c>process.spawn()</c> handle inside a child task
    /// (<c>process.wait</c>) throws <c>StateError</c>. The faulted result is observable
    /// via <c>task.awaitAll</c> as an error value with <c>.type == "StateError"</c>.
    /// Spec: L1543–1549 — "Using a parent's handle inside a child task throws StateError."
    /// </summary>
    [Fact]
    public void D5_ProcessWait_CrossTask_ThrowsStateError_PerSpecAsyncD5()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run(@"
let h = process.spawn(""sleep 100"");
let f = task.run(() => process.wait(h));
let results = task.awaitAll([f]);
let errType = results[0].type;
process.kill(h);
process.wait(h);
let result = errType;
");
        Assert.Equal("StateError", result);
    }

    /// <summary>
    /// D5: The <c>StateError</c> message for a cross-task process handle access names
    /// the task boundary. The impl emits:
    /// "'&lt;funcName&gt;': process handle does not cross task boundaries. Spawn the process
    /// inside the same task that uses it."
    /// The test pins both stable sentence fragments verbatim so any future drift in
    /// either the spec or the impl fails loud.
    /// Spec: §Async "Process handle boundary (D5)".
    /// </summary>
    [Fact]
    public void D5_ProcessWait_CrossTask_ErrorMessage_NamesBoundary_PerSpecAsyncD5()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run(@"
let h = process.spawn(""sleep 100"");
let f = task.run(() => process.wait(h));
let results = task.awaitAll([f]);
let msg = results[0].message;
process.kill(h);
process.wait(h);
let result = msg;
");
        var msg = Assert.IsType<string>(result);
        Assert.Contains("process handle does not cross task boundaries", msg, StringComparison.Ordinal);
        Assert.Contains("Spawn the process inside the same task that uses it", msg, StringComparison.Ordinal);
    }

    /// <summary>
    /// D5: <c>process.kill</c> on a cross-task handle throws <c>StateError</c>.
    /// The boundary applies to all <c>process.*</c> operations, not just <c>process.wait</c>.
    /// Spec: L1543–1549 — "The boundary is enforced for all process.* operations."
    /// </summary>
    [Fact]
    public void D5_ProcessKill_CrossTask_ThrowsStateError_PerSpecAsyncD5()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run(@"
let h = process.spawn(""sleep 100"");
let f = task.run(() => process.kill(h));
let results = task.awaitAll([f]);
let errType = results[0].type;
process.kill(h);
process.wait(h);
let result = errType;
");
        Assert.Equal("StateError", result);
    }

    /// <summary>
    /// D5: <c>process.read</c> on a cross-task handle throws <c>StateError</c>.
    /// Spec: L1543–1549.
    /// </summary>
    [Fact]
    public void D5_ProcessRead_CrossTask_ThrowsStateError_PerSpecAsyncD5()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run(@"
let h = process.spawn(""sleep 100"");
let f = task.run(() => process.read(h));
let results = task.awaitAll([f]);
let errType = results[0].type;
process.kill(h);
process.wait(h);
let result = errType;
");
        Assert.Equal("StateError", result);
    }

    /// <summary>
    /// D5 negative space — same-context process operations are NOT blocked by the
    /// task-boundary enforcement. Spawning and waiting in the same context is normal usage.
    /// This regression guard ensures the D5 check does not over-fire.
    /// Spec: L1543–1549 (boundary applies cross-task only; same-context is unaffected).
    /// </summary>
    [Fact]
    public void D5_SameContext_ProcessWait_Succeeds_PerSpecAsyncD5()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run(@"
let h = process.spawn(""echo hello"");
let r = process.wait(h);
let result = r.exitCode;
");
        Assert.Equal(0L, result);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D11 — Two-systems non-interaction
    // Spec: §Async "The two systems are non-interacting" ~L1440–1442, L1661+
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D11: <c>event.poll()</c> does NOT advance a System-A Future. A Future that is still
    /// Running when <c>event.poll()</c> is called remains Running after it returns.
    /// <c>event.poll()</c> drains the System-B event queue only; it has no effect on
    /// thread-pool tasks.
    /// Spec: L1440–1442 — "event.poll() does not advance a Future."
    /// </summary>
    [Fact]
    public void D11_EventPoll_DoesNotAdvanceFuture_PerSpecAsyncD11()
    {
        var result = Run(@"
let f = task.run(() => { time.sleep(10); return ""done""; });
// At this point f is Running (thread sleeping for 10 seconds).
event.poll();
// event.poll() is System-B machinery — it cannot wake a System-A Future.
let statusAfterPoll = task.status(f);
task.cancel(f);
let result = statusAfterPoll;
");
        // The future must still be Running after event.poll().
        var status = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Running", status.MemberName);
    }

    /// <summary>
    /// D11: Multiple consecutive <c>event.poll()</c> calls do not advance a Future.
    /// Spec: L1440–1442.
    /// </summary>
    [Fact]
    public void D11_MultipleEventPolls_DoNotAdvanceFuture_PerSpecAsyncD11()
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

    /// <summary>
    /// D11: <c>await</c> does NOT drain the event queue (System B).
    ///
    /// <para>
    /// A callback enqueued into System B before an <c>await</c> is NOT fired by the
    /// <c>await</c> — awaiting a Future is a System-A join, not an event-queue drain.
    /// The callback remains pending until a subsequent drain point.
    /// </para>
    ///
    /// <para>
    /// The test uses the deterministic <see cref="VMContext.EnqueueCallback"/> API
    /// (available via InternalsVisibleTo) to simulate a System-B event that was detected
    /// but not yet delivered — analogous to an <c>fs.watch</c> callback that fired on a
    /// background OS thread but has not been drained on the main VM thread yet.
    /// This avoids filesystem-timing races while proving the spec's non-interaction invariant.
    /// </para>
    /// Spec: L1440–1442 — "await does not drain the event queue."
    /// </summary>
    [Fact]
    public void D11_Await_DoesNotDrainEventQueue_PerSpecAsyncD11()
    {
        // Build the VM manually to access VMContext.EnqueueCallback (internal).
        var lexer = new Lexer(@"
let f = task.run(() => 42);
let result = await f;
", "<test>");
        var tokens = lexer.ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());

        // Install a System-B callback (the "queued" event-queue entry).
        bool callbackFired = false;
        var flagCallback = new BuiltInFunction("_flag", 0, (ctx, args) =>
        {
            callbackFired = true;
            return StashValue.Null;
        });

        // Enqueue BEFORE script execution — simulates an fs.watch event that was
        // detected by the OS but not yet delivered to the main VM thread.
        var vmContext = (VMContext)vm.Context;
        vmContext.EnqueueCallback(flagCallback, []);

        // Execute the script: it awaits a Future (System A join).
        // The await must NOT drain the System-B queue.
        vm.Execute(chunk);

        // Callback must NOT have fired — await is System-A only.
        Assert.False(callbackFired,
            "D11 violated: await drained the event queue (System B). " +
            "await must be a System-A join only and must not drain the B-queue.");
    }

    /// <summary>
    /// D11: After an <c>await</c> that did NOT drain, a subsequent <c>event.poll()</c>
    /// DOES fire the pending callback — proving the callback was queued (not lost),
    /// just not drained prematurely by <c>await</c>.
    /// Spec: L1440–1442 (non-interaction); the drain-at-event.poll clause at L1695.
    /// </summary>
    [Fact]
    public void D11_EventPollAfterAwait_FiresQueuedCallback_PerSpecAsyncD11()
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

        // Execute: await (must NOT drain) → event.poll() (MUST drain).
        vm.Execute(chunk);

        // After event.poll() the callback must have fired.
        Assert.True(callbackFired,
            "D11: event.poll() must drain queued callbacks. " +
            "The queued callback was not fired — the queue may be broken.");
    }

    /// <summary>
    /// D11 positive-space baseline: System A (Futures) works correctly in the absence
    /// of any System-B activity. This regression guard ensures the two-systems separation
    /// does not break normal async behavior.
    /// Spec: L1430–1442 (two systems are independent).
    /// </summary>
    [Fact]
    public void D11_SystemAWorksWithoutSystemB_PerSpecAsyncD11()
    {
        var result = Run(@"
let f = task.run(() => { return 42; });
let result = await f;
");
        Assert.Equal(42L, result);
    }

    /// <summary>
    /// D11 positive-space baseline: <c>event.poll()</c> with an empty System-B queue
    /// returns immediately without error — System B is silent when idle.
    /// Spec: L1695 — "event.poll() drains everything currently queued and returns
    /// immediately without blocking."
    /// </summary>
    [Fact]
    public void D11_EventPollEmptyQueue_DoesNotThrow_PerSpecAsyncD11()
    {
        // Should execute without throwing.
        RunStatements(@"event.poll();");
    }
}
