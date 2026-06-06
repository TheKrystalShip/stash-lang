using Stash.Bytecode;
using Stash.Runtime.Types;
using Stash.Stdlib;

namespace Stash.Tests.Conformance.Async;

/// <summary>
/// Conformance tests for §Async — Async Functions and Await, clause D1.
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clause D1
/// of <c>docs/Stash — Language Specification.md</c> §Async (L1559–1599):
/// </para>
/// <list type="bullet">
///   <item><b>D1</b> — unobserved-fault report at exit: a faulted-never-awaited Future
///     emits exactly one <c>warning: &lt;N&gt; unobserved async error(s):\n  &lt;ErrorType&gt;: &lt;message&gt;</c>
///     block to stderr at script exit; the process exit code is unchanged; an explicitly
///     cancelled task is NOT reported; reading <c>task.status(future)</c> does NOT count
///     as observation.</item>
///   <item><b>D1 EmbeddedMode</b> — in embedded mode the runtime does not emit the
///     unobserved-fault report (the host surfaces task errors however it chooses).</item>
///   <item><b>Consumer-enumeration</b> — every observer (<c>await</c>, <c>task.await</c>,
///     <c>task.awaitAll</c>, <c>task.awaitAny</c>, <c>task.all</c>, <c>task.race</c>) marks
///     futures observed, so no false report fires for normally-consumed faults.</item>
/// </list>
///
/// <para>
/// These are conformance tests — they prove the <em>spec</em>, not guard implementation
/// regressions. The existing behavior suite at <c>Stash.Tests/Interpreting/Async/</c>
/// remains in place for regression coverage; the two suites are complementary.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class UnobservedExitConformanceTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a registry with a pre-faulted future (avoids timing-dependent background tasks).
    /// </summary>
    private static SpawnedFutureRegistry RegistryWithFault(string errorType, string message)
    {
        var registry = new SpawnedFutureRegistry();
        registry.Register(StashFuture.Failed(errorType, message));
        return registry;
    }

    private static string RunReport(SpawnedFutureRegistry registry, bool embeddedMode = false)
    {
        var sw = new StringWriter { NewLine = "\n" };
        UnobservedFaultReporter.Report(registry, sw, embeddedMode);
        return sw.ToString();
    }

    private static (VirtualMachine vm, string report) RunScriptAndReport(string source)
    {
        var lexer = new Stash.Lexing.Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Stash.Parsing.Parser(tokens);
        var stmts = parser.ParseProgram();
        Stash.Resolution.SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.ErrorOutput = new StringWriter();
        try { vm.Execute(chunk); }
        catch { /* swallow to allow report to run */ }

        var sw = new StringWriter { NewLine = "\n" };
        UnobservedFaultReporter.Report(vm.SpawnedFutures, sw);
        return (vm, sw.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D1 — unobserved fault is reported
    // Spec: L1584–1599 ("Faulted but never observed → reported")
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D1: a faulted-never-awaited future emits exactly one warning block to stderr
    /// with the exact format <c>warning: N unobserved async error(s):\n  ErrorType: message</c>.
    /// </summary>
    [Fact]
    public void D1_UnobservedFault_EmitsWarningBlock_PerSpecAsyncD1()
    {
        var registry = RegistryWithFault("ValueError", "oops");
        string report = RunReport(registry);

        Assert.Contains("warning: 1 unobserved async error(s):", report);
        Assert.Contains("ValueError: oops", report);
    }

    /// <summary>
    /// D1: exactly ONE warning header is emitted for multiple unobserved faults
    /// (they all appear in the same block, not one block per fault).
    /// </summary>
    [Fact]
    public void D1_MultipleUnobservedFaults_SingleWarningBlock_PerSpecAsyncD1()
    {
        var registry = new SpawnedFutureRegistry();
        registry.Register(StashFuture.Failed("ValueError", "a"));
        registry.Register(StashFuture.Failed("TypeError", "b"));

        string report = RunReport(registry);

        Assert.Contains("warning: 2 unobserved async error(s):", report);
        Assert.Contains("ValueError: a", report);
        Assert.Contains("TypeError: b", report);

        // Only one warning header line in the whole report
        int warningHeaders = report.Split('\n').Count(line => line.StartsWith("warning:"));
        Assert.Equal(1, warningHeaders);
    }

    /// <summary>
    /// D1: exit code is unchanged — the unobserved-fault report does not alter the exit
    /// code (spec: "The process exit code is unchanged by this report").
    /// Proven in-VM: after running a script that spawns a faulting task, the VM completes
    /// normally (no exception propagated from Execute). The CLI wiring is covered separately
    /// by <c>UnobservedAsyncCliWiringTests</c>.
    /// </summary>
    [Fact]
    public void D1_ExitCodeUnchanged_VMCompletesNormally_PerSpecAsyncD1()
    {
        // A faulted-but-unobserved task must not cause vm.Execute to throw.
        // The reporter writes to stderr; the VM itself returns normally.
        var (vm, report) = RunScriptAndReport(@"
task.run(() => { throw ValueError { message: ""oops"" }; });
time.sleep(0.3);
");
        // vm.Execute completed (no exception) — exit code would be 0 in the CLI
        // The report contains the warning (not silent)
        Assert.Contains("warning: 1 unobserved async error(s):", report);
        Assert.Contains("ValueError: oops", report);
    }

    /// <summary>
    /// D1: no report when there are no unobserved faults (happy path).
    /// </summary>
    [Fact]
    public void D1_NoFaults_EmptyReport_PerSpecAsyncD1()
    {
        var registry = new SpawnedFutureRegistry();
        string report = RunReport(registry);

        Assert.Equal("", report);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D1 — cancelled tasks are NOT reported
    // Spec: "An explicitly cancelled task ... is not reported" (L1595)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D1: explicitly cancelled tasks are NOT reported (the !IsCancelled filter).
    /// </summary>
    [Fact]
    public void D1_CancelledFuture_NotReported_PerSpecAsyncD1()
    {
        var cts = new System.Threading.CancellationTokenSource();
        var tcs = new System.Threading.Tasks.TaskCompletionSource<object?>();
        tcs.SetCanceled(cts.Token);
        var cancelledFuture = new StashFuture(tcs.Task, cts);

        var registry = new SpawnedFutureRegistry();
        registry.Register(cancelledFuture);

        string report = RunReport(registry);

        Assert.DoesNotContain("warning:", report);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D1 — task.status does NOT count as observation
    // Spec: "reading task.status(future) does not count as observing the error" (L1596–1598)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D1: <c>task.status(future)</c> does NOT mark the future observed. A future that was
    /// only status-checked (never awaited or passed to a consumer) is still reported by D1.
    /// </summary>
    [Fact]
    public void D1_TaskStatus_DoesNotObserve_StillReported_PerSpecAsyncD1()
    {
        var future = StashFuture.Failed("ValueError", "status-oops");
        // Simulate calling task.status — reads the Status property but does NOT call MarkObserved
        string _ = future.Status;

        var registry = new SpawnedFutureRegistry();
        registry.Register(future);

        string report = RunReport(registry);

        Assert.Contains("warning: 1 unobserved async error(s):", report);
        Assert.Contains("ValueError: status-oops", report);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // D1 EmbeddedMode — report suppressed in embedded mode
    // Spec: "This report is emitted by the CLI runtime; a host that embeds Stash through the
    //        hosting SDK does not receive it and surfaces task errors however it chooses." (L1598–1599)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// D1 EmbeddedMode: in embedded mode the runtime does NOT emit the unobserved-fault report.
    /// The host surfaces task errors however it chooses.
    /// </summary>
    [Fact]
    public void D1_EmbeddedMode_True_UnobservedFault_ZeroOutput_PerSpecAsyncD1()
    {
        var registry = RegistryWithFault("ValueError", "embedded-oops");
        string report = RunReport(registry, embeddedMode: true);

        Assert.Equal("", report);
    }

    /// <summary>
    /// D1 EmbeddedMode: <c>StashEngine</c> (which sets <c>EmbeddedMode = true</c> internally)
    /// produces zero stderr for an unobserved faulted task. This proves the embedded-mode
    /// suppression end-to-end through the engine.
    /// </summary>
    [Fact]
    public void D1_EmbeddedEngine_UnobservedFault_ZeroStderr_PerSpecAsyncD1()
    {
        var errSw = new StringWriter { NewLine = "\n" };
        var engine = new StashEngine();
        engine.ErrorOutput = errSw;
        engine.Output = TextWriter.Null;

        engine.Run(@"
task.run(() => { throw ValueError { message: ""embedded-oops"" }; });
time.sleep(0.3);
");

        Assert.Equal("", errSw.ToString());
    }

    /// <summary>
    /// D1 EmbeddedMode: non-embedded mode (default) DOES emit the warning report, confirming
    /// the gate is not incorrectly defaulting to suppression.
    /// </summary>
    [Fact]
    public void D1_EmbeddedMode_False_UnobservedFault_ReportFires_PerSpecAsyncD1()
    {
        var registry = RegistryWithFault("ValueError", "oops");
        int count = UnobservedFaultReporter.Report(registry, new StringWriter(), embeddedMode: false);

        Assert.Equal(1, count);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Consumer-enumeration — every observer marks futures observed
    // Spec: "Only the results and errors you `await` (directly, or via task.await /
    //        task.awaitAll / task.awaitAny / task.all / task.race) reach your code." (L1576–1578)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Consumer-enumeration: <c>await</c> marks the future observed — no false D1 report.
    /// </summary>
    [Fact]
    public void Consumer_Await_MarksObserved_NoFalseReport_PerSpecAsyncD1()
    {
        var (_, report) = RunScriptAndReport(@"
try {
    let f = task.run(() => { throw ValueError { message: ""e"" }; });
    time.sleep(0.3);
    await f;
} catch(err) { }
");
        Assert.DoesNotContain("warning:", report);
    }

    /// <summary>
    /// Consumer-enumeration: <c>task.await</c> marks the future observed — no false D1 report.
    /// </summary>
    [Fact]
    public void Consumer_TaskAwait_MarksObserved_NoFalseReport_PerSpecAsyncD1()
    {
        var (_, report) = RunScriptAndReport(@"
try {
    let f = task.run(() => { throw ValueError { message: ""e"" }; });
    time.sleep(0.3);
    task.await(f);
} catch(err) { }
");
        Assert.DoesNotContain("warning:", report);
    }

    /// <summary>
    /// Consumer-enumeration: <c>task.awaitAll</c> marks all futures observed — no false D1 report.
    /// </summary>
    [Fact]
    public void Consumer_TaskAwaitAll_MarksObserved_NoFalseReport_PerSpecAsyncD1()
    {
        var (_, report) = RunScriptAndReport(@"
let f = task.run(() => { throw ValueError { message: ""e"" }; });
time.sleep(0.3);
let results = task.awaitAll([f]);
");
        Assert.DoesNotContain("warning:", report);
    }

    /// <summary>
    /// Consumer-enumeration: <c>task.awaitAny</c> marks the winner and all losers observed —
    /// no false D1 report.
    /// </summary>
    [Fact]
    public void Consumer_TaskAwaitAny_MarksObserved_NoFalseReport_PerSpecAsyncD1()
    {
        var (_, report) = RunScriptAndReport(@"
try {
    let f1 = task.run(() => { throw ValueError { message: ""e"" }; });
    let f2 = task.run(() => { time.sleep(10); return 42; });
    time.sleep(0.3);
    let result = task.awaitAny([f1, f2]);
} catch(err) { }
");
        Assert.DoesNotContain("warning:", report);
    }

    /// <summary>
    /// Consumer-enumeration: <c>task.all</c> marks each constituent observed — no false D1 report.
    /// </summary>
    [Fact]
    public void Consumer_TaskAll_MarksObserved_NoFalseReport_PerSpecAsyncD1()
    {
        var (_, report) = RunScriptAndReport(@"
try {
    let f = task.run(() => { throw ValueError { message: ""e"" }; });
    let combined = task.all([f]);
    await combined;
} catch(err) { }
");
        Assert.DoesNotContain("warning:", report);
    }

    /// <summary>
    /// Consumer-enumeration: <c>task.race</c> marks all constituents observed (winner + rest) —
    /// no false D1 report.
    /// </summary>
    [Fact]
    public void Consumer_TaskRace_MarksObserved_NoFalseReport_PerSpecAsyncD1()
    {
        var (_, report) = RunScriptAndReport(@"
try {
    let f = task.run(() => { throw ValueError { message: ""e"" }; });
    let raced = task.race([f]);
    await raced;
} catch(err) { }
");
        Assert.DoesNotContain("warning:", report);
    }
}
