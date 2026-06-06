namespace Stash.Tests.Interpreting.Async.TwoSystemsBoundary;

using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Tests.Interpreting;

/// <summary>
/// D5 — Cross-task process handle boundary contract:
///   A process handle spawned on the parent (task-owning) context is not valid
///   inside a child task created by task.run(). Any consumer function
///   (process.wait, process.read, process.kill, etc.) called on a cross-context
///   handle must throw StateError — not silently return an empty/false result.
/// </summary>
[Collection("SystemCwdTests")]
public class CrossVmHandleTests : TempDirectoryFixture
{
    public CrossVmHandleTests() : base("stash_crossvm_test") { }

    // ── process.wait cross-VM → StateError ───────────────────────────────────

    /// <summary>
    /// Spawn a long-running process on the parent, pass the handle into task.run,
    /// call process.wait inside the child. The child's task should fault with StateError.
    /// Verified via task.awaitAll: the element's .type field must be "StateError".
    /// </summary>
    [Fact]
    public void CrossVm_Wait_ThrowsStateError()
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
    /// Verify that the StateError message names the cross-task boundary.
    /// </summary>
    [Fact]
    public void CrossVm_Wait_ErrorMessageNamesBoundary()
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
        Assert.IsType<string>(result);
        Assert.Contains("task", (string)result!);
    }

    // ── process.kill cross-VM → StateError ───────────────────────────────────

    [Fact]
    public void CrossVm_Kill_ThrowsStateError()
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

    // ── process.read cross-VM → StateError ───────────────────────────────────

    [Fact]
    public void CrossVm_Read_ThrowsStateError()
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

    // ── same-context operations still work ───────────────────────────────────

    /// <summary>
    /// Sanity: spawning and waiting in the SAME context (no cross-VM) still works.
    /// Regression guard: this path must not throw StateError.
    /// </summary>
    [Fact]
    public void SameContext_Wait_Succeeds()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run(@"
let h = process.spawn(""echo hello"");
let r = process.wait(h);
let result = r.exitCode;
");
        Assert.Equal(0L, result);
    }

    /// <summary>
    /// Sanity: process.isAlive after process.wait in the SAME context returns false (not StateError).
    /// Regression guard for the ProcessWaitCache discriminator path.
    /// </summary>
    [Fact]
    public void SameContext_IsAliveAfterWait_ReturnsFalse()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run(@"
let h = process.spawn(""echo done"");
process.wait(h);
let result = process.isAlive(h);
");
        Assert.Equal(false, result);
    }

    /// <summary>
    /// Sanity: process.kill after process.wait in the SAME context returns false (not StateError).
    /// Regression guard for the ProcessWaitCache discriminator path.
    /// </summary>
    [Fact]
    public void SameContext_KillAfterWait_ReturnsFalse()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var result = Run(@"
let h = process.spawn(""echo done"");
process.wait(h);
let result = process.kill(h);
");
        Assert.Equal(false, result);
    }
}
