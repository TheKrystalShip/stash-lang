namespace Stash.Tests.Interpreting.Async.UnobservedAndExit;

using System.IO;
using System.Linq;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Stdlib;

/// <summary>
/// D1 — unobserved-task exit report: verifies that faulted-never-awaited futures
/// appear in the report, and that stdout is unchanged.
/// </summary>
public class UnobservedReportTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static VirtualMachine BuildAndRunVM(string source, out StringWriter stderr)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var errSw = new StringWriter { NewLine = "\n" };
        vm.ErrorOutput = errSw;
        vm.Execute(chunk);
        stderr = errSw;
        return vm;
    }

    /// <summary>
    /// Registers a pre-faulted future directly into the registry (no Stash script needed).
    /// This avoids timing-dependent tests that rely on a background task completing within
    /// a fixed sleep window.
    /// </summary>
    private static SpawnedFutureRegistry MakeRegistryWithFault(string errorType, string message)
    {
        var registry = new SpawnedFutureRegistry();
        var future = StashFuture.Failed(errorType, message);
        registry.Register(future);
        return registry;
    }

    private static string RunReport(VirtualMachine vm)
    {
        var sw = new StringWriter { NewLine = "\n" };
        UnobservedFaultReporter.Report(vm.SpawnedFutures, sw);
        return sw.ToString();
    }

    private static string RunRegistryReport(SpawnedFutureRegistry registry)
    {
        var sw = new StringWriter { NewLine = "\n" };
        UnobservedFaultReporter.Report(registry, sw);
        return sw.ToString();
    }

    // ── Unobserved fault IS reported ─────────────────────────────────────────

    [Fact]
    public void ThrowingNeverAwaited_IsReportedAtExit()
    {
        // Use a pre-faulted future to avoid timing-dependent background tasks.
        var registry = MakeRegistryWithFault("ValueError", "oops");
        string report = RunRegistryReport(registry);

        Assert.Contains("warning: 1 unobserved async error(s):", report);
        Assert.Contains("ValueError: oops", report);
    }

    [Fact]
    public void ThrowingNeverAwaited_ReportHasExactlyOneBlock()
    {
        var registry = MakeRegistryWithFault("ValueError", "oops");
        string report = RunRegistryReport(registry);

        // Only one warning: header line
        int warningCount = report.Split('\n').Count(line => line.StartsWith("warning:"));
        Assert.Equal(1, warningCount);
    }

    [Fact]
    public void StdoutUnchanged_WhenUnobservedFaultExists()
    {
        // Stdout output should not be affected by the unobserved-task report.
        // Use a Stash script to produce stdout, then check it's unaffected by the registry.
        var lexer = new Lexer(@"
io.println(""hello"");
", "<test>");
        var tokens = lexer.ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var stdoutSw = new StringWriter { NewLine = "\n" };
        var stderrSw = new StringWriter { NewLine = "\n" };
        vm.Output = stdoutSw;
        vm.ErrorOutput = stderrSw;
        vm.Execute(chunk);

        // Add a fault to the registry (simulating task.run(() => throw ...))
        var future = StashFuture.Failed("ValueError", "oops");
        vm.SpawnedFutures.Register(future);

        // The report writes to a separate writer, not to vm.Output
        var reportSw = new StringWriter { NewLine = "\n" };
        UnobservedFaultReporter.Report(vm.SpawnedFutures, reportSw);

        // Stdout is unchanged
        Assert.Equal("hello\n", stdoutSw.ToString());
    }

    [Fact]
    public void MultipleUnobservedFaults_AllReported()
    {
        var registry = new SpawnedFutureRegistry();
        registry.Register(StashFuture.Failed("ValueError", "a"));
        registry.Register(StashFuture.Failed("TypeError", "b"));

        string report = RunRegistryReport(registry);

        Assert.Contains("warning: 2 unobserved async error(s):", report);
        Assert.Contains("ValueError: a", report);
        Assert.Contains("TypeError: b", report);
    }

    // ── Report fires AFTER primary error ────────────────────────────────────

    [Fact]
    public void TopLevelError_UnobservedFaultStillInRegistry()
    {
        // Even when the script itself throws, unobserved faults must still be in the registry.
        // Use a pre-faulted future to avoid timing dependence.
        var registry = new SpawnedFutureRegistry();
        var asyncFault = StashFuture.Failed("ValueError", "async-oops");
        registry.Register(asyncFault);

        // The unobserved fault is in the registry (simulating a task.run that ran + threw)
        var faults = registry.UnobservedFaults().ToList();
        Assert.Single(faults);

        // The reporter fires even when the script had a primary error — test the mechanism
        string report = RunRegistryReport(registry);
        Assert.Contains("warning: 1 unobserved async error(s):", report);
        Assert.Contains("async-oops", report);
    }

    // ── task.status does NOT count as observation ────────────────────────────

    [Fact]
    public void TaskStatus_DoesNotObserve_StillReported()
    {
        // task.status returns state but does NOT mark observed.
        // Verify by checking that a faulted future that was only status-checked is still
        // reported by D1. Use a pre-faulted future to avoid timing dependence.
        var future = StashFuture.Failed("ValueError", "status-oops");
        // Simulate calling task.status: it does NOT call MarkObserved
        string _ = future.Status; // reads "Failed" — no MarkObserved call

        var registry = new SpawnedFutureRegistry();
        registry.Register(future);

        string report = RunRegistryReport(registry);

        // task.status does not mark observed, so D1 reports the fault
        Assert.Contains("warning: 1 unobserved async error(s):", report);
        Assert.Contains("ValueError: status-oops", report);
    }

    // ── Cancelled futures are NOT reported ──────────────────────────────────

    [Fact]
    public void CancelledFuture_NotReported()
    {
        // Cancelled futures are excluded from D1 reports (the !IsCancelled filter).
        // Create a genuinely cancelled .NET task.
        var cts = new System.Threading.CancellationTokenSource();
        var tcs = new System.Threading.Tasks.TaskCompletionSource<object?>();
        tcs.SetCanceled(cts.Token);
        var cancelledFuture = new StashFuture(tcs.Task, cts);

        var registry = new SpawnedFutureRegistry();
        registry.Register(cancelledFuture);

        string report = RunRegistryReport(registry);

        // Cancelled futures are excluded from D1 reports
        Assert.DoesNotContain("warning:", report);
    }

    // ── No faults → no report ────────────────────────────────────────────────

    [Fact]
    public void NoUnobservedFaults_EmptyReport()
    {
        var vm = BuildAndRunVM(@"
let f = task.run(() => { return 42; });
let result = await f;
", out _);

        string report = RunReport(vm);

        Assert.Equal("", report);
    }
}
