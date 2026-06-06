namespace Stash.Tests.Interpreting.Async.UnobservedAndExit;

using System.IO;
using System.Linq;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime.Types;
using Stash.Stdlib;

/// <summary>
/// D1 — row-10 in-flight-drop boundary: a still-running task at script exit is
/// silently dropped (not reported, does not block exit). This is documented behavior.
///
/// <para>
/// <strong>Category=Gotcha change-detector:</strong> these tests assert the CURRENT
/// behavior (drop without blocking). If D1 is changed to drain or join still-running
/// tasks, these tests will go RED — that is the intended signal to update the
/// assertion to match the new behavior.
/// </para>
///
/// <para>
/// Linked to <c>.claude/agents/stash-author.gotchas.md</c> entry
/// <c>async-in-flight-drop-at-exit</c>.
/// </para>
/// </summary>
[Trait("Category", "Gotcha")]
public class InFlightDropGotchaTests
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

    /// <summary>
    /// A still-running task at script exit is NOT reported (it hasn't faulted yet —
    /// it's still running). This is the row-10 drop boundary: results are fire-and-forget.
    /// </summary>
    [Fact]
    public void InFlightTask_AtExit_NotReported()
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
    /// Script exit must NOT block waiting for still-running tasks (the join/drain option
    /// was explicitly rejected in D1 — see brief §D1 "Joining in-flight tasks at exit").
    /// </summary>
    [Fact]
    public void InFlightTask_AtExit_DoesNotBlockExit()
    {
        // This test has a task sleeping for 10 seconds. The script exits immediately.
        // If exit were to join/drain, this test would take 10+ seconds and time out.
        var (_, elapsedMs) = RunAndMeasure(@"
task.run(() => { time.sleep(10); return 42; });
// Script exits here immediately — must NOT wait for the 10s sleep
");

        // Exit should be near-instantaneous (< 2 seconds in CI)
        Assert.True(elapsedMs < 2000,
            $"Script exit blocked for {elapsedMs}ms — in-flight tasks must NOT block exit");
    }

    /// <summary>
    /// Baseline: a completed faulted task IS reported (not dropped).
    /// This validates that the "still-running" filter is checking IsFaulted, not
    /// something broader that would mask actually-faulted tasks.
    /// </summary>
    [Fact]
    public void CompletedFaultedTask_IsReported_NotDropped()
    {
        var (report, _) = RunAndMeasure(@"
task.run(() => { throw ValueError { message: ""completed-fault"" }; });
time.sleep(0.3);  // wait for task to complete
");

        // A completed (not still-running) faulted task IS reported
        Assert.Contains("warning: 1 unobserved async error(s):", report);
        Assert.Contains("ValueError: completed-fault", report);
    }
}
