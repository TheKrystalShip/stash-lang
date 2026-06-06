using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime.Types;
using Stash.Stdlib;

namespace Stash.Tests.Conformance.Async;

/// <summary>
/// Conformance tests for §Async — Edit 8: still-running at exit is dropped, not drained.
///
/// <para>
/// These tests prove that the Stash implementation honors the normative negative-space clause
/// introduced by Edit 8 in <c>docs/Stash — Language Specification.md</c> §Async:
/// <em>"A Future whose status is <c>task.Status.Running</c> when the main script returns is
/// silently abandoned. The runtime does not wait, does not drain pending work, and does not
/// report."</em>
/// </para>
///
/// <para>
/// <strong>Sealed negative space (Edit 8):</strong> still-running-at-exit drop is intentional
/// law, not a doc/reality mismatch. This class was previously
/// <c>InFlightDropGotchaTests</c> (<c>Category=Gotcha</c>); it has been reclassified to
/// <c>Category=Conformance</c> because spec L1554 + Edit 8 promotes the drop behavior from a
/// behavioral note to a normative clause. A <c>Gotcha</c> test asserts <em>current-buggy</em>
/// behavior; a <c>Conformance</c> test asserts <em>sealed-law</em> behavior. The classification
/// was wrong; the assertion was always right.
/// </para>
///
/// <para>
/// These are conformance tests — they prove the <em>spec</em>, not guard implementation
/// regressions. The existing behavior suite at <c>Stash.Tests/Interpreting/Async/</c>
/// remains in place for regression coverage; the two suites are complementary.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class InFlightDropConformanceTests
{
    private static (string reportOutput, long elapsedMs) RunAndMeasure(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.ErrorOutput = new StringWriter();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        vm.Execute(chunk);
        sw.Stop();

        var reportSw = new StringWriter { NewLine = "\n" };
        UnobservedFaultReporter.Report(vm.SpawnedFutures, reportSw);
        return (reportSw.ToString(), sw.ElapsedMilliseconds);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Edit 8 — still-running at exit is NOT reported
    // Spec: "The runtime does not wait, does not drain pending work, and does not report."
    //        (Edit 8 normative clause)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edit 8: a still-running task at script exit is silently abandoned — it is NOT
    /// reported by D1 (it hasn't faulted; it's still <c>task.Status.Running</c>). D1
    /// scans faulted-and-unobserved tasks only.
    /// </summary>
    [Fact]
    public void StillRunningAtExit_Dropped_NotReported_PerSpecAsyncNegativeSpace()
    {
        // task.run with a long sleep — the script exits before the task finishes.
        // The task is still Running, not Faulted, so D1 must not report it.
        var (report, _) = RunAndMeasure(@"
task.run(() => { time.sleep(10); throw ValueError { message: ""never"" }; });
// Script exits here — task is still in-flight (sleeping)
");

        // Still-running tasks are NOT in the unobserved faults list (they aren't faulted yet)
        Assert.DoesNotContain("warning:", report);
    }

    /// <summary>
    /// Edit 8: script exit must NOT block waiting for still-running tasks. The runtime
    /// does not wait or drain — exit is instantaneous regardless of in-flight work.
    /// </summary>
    [Fact]
    public void StillRunningAtExit_DoesNotBlockExit_PerSpecAsyncNegativeSpace()
    {
        // This test has a task sleeping for 10 seconds. The script exits immediately.
        // If the runtime were to join/drain, this test would take 10+ seconds and time out.
        var (_, elapsedMs) = RunAndMeasure(@"
task.run(() => { time.sleep(10); return 42; });
// Script exits here immediately — must NOT wait for the 10s sleep
");

        // Exit should be near-instantaneous (< 2 seconds in CI)
        Assert.True(elapsedMs < 2000,
            $"Script exit blocked for {elapsedMs}ms — in-flight tasks must NOT block exit " +
            "(spec Edit 8: still-running at exit is dropped, not drained)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Baseline: completed faulted task IS reported (D1 scope is faulted-and-unobserved)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Baseline (Edit 8 boundary guard): a completed faulted task IS reported by D1 —
    /// it is faulted-and-unobserved, not still-running. This validates that the
    /// still-running filter is checking <c>IsFaulted</c>, not something broader that
    /// would mask actually-faulted tasks.
    /// </summary>
    [Fact]
    public void CompletedFaultedTask_IsReported_NotDropped_PerSpecAsyncD1Boundary()
    {
        var (report, _) = RunAndMeasure(@"
task.run(() => { throw ValueError { message: ""completed-fault"" }; });
time.sleep(0.3);  // wait for task to complete
");

        // A completed (not still-running) faulted task IS reported by D1
        Assert.Contains("warning: 1 unobserved async error(s):", report);
        Assert.Contains("ValueError: completed-fault", report);
    }
}
