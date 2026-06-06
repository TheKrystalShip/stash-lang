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
/// D1 — no-false-positives guarantee: programmatically asserts that for every
/// outcome-consuming combinator (await, task.await, task.awaitAll, task.awaitAny,
/// task.all, task.race), a normally-awaited faulted future produces ZERO unobserved
/// lines in the D1 report.
///
/// <para>
/// This is the consumer-enumeration floor test: adding a new consumer that returns/throws
/// a Future's outcome without calling MarkObserved() will cause one of these cases to
/// produce a false-positive warning.
/// </para>
/// </summary>
public class ConsumerEnumerationTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static (VirtualMachine vm, string reportOutput) RunAndReport(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.ErrorOutput = new StringWriter();

        // Script may throw when consuming a faulted future — swallow it
        try { vm.Execute(chunk); }
        catch { /* expected for faulted-future consumption */ }

        var sw = new StringWriter { NewLine = "\n" };
        UnobservedFaultReporter.Report(vm.SpawnedFutures, sw);
        return (vm, sw.ToString());
    }

    // ── await keyword ─────────────────────────────────────────────────────────

    [Fact]
    public void Await_FaultedFuture_ZeroUnobservedLines()
    {
        var (_, report) = RunAndReport(@"
try {
    let f = task.run(() => { throw ValueError { message: ""e"" }; });
    time.sleep(0.3);
    await f;
} catch(err) { }
");
        Assert.DoesNotContain("warning:", report);
    }

    // ── task.await ────────────────────────────────────────────────────────────

    [Fact]
    public void TaskAwait_FaultedFuture_ZeroUnobservedLines()
    {
        var (_, report) = RunAndReport(@"
try {
    let f = task.run(() => { throw ValueError { message: ""e"" }; });
    time.sleep(0.3);
    task.await(f);
} catch(err) { }
");
        Assert.DoesNotContain("warning:", report);
    }

    // ── task.awaitAll ─────────────────────────────────────────────────────────

    [Fact]
    public void TaskAwaitAll_FaultedFuture_ZeroUnobservedLines()
    {
        var (_, report) = RunAndReport(@"
let f = task.run(() => { throw ValueError { message: ""e"" }; });
time.sleep(0.3);
let results = task.awaitAll([f]);
");
        Assert.DoesNotContain("warning:", report);
    }

    // ── task.awaitAny ─────────────────────────────────────────────────────────

    [Fact]
    public void TaskAwaitAny_FaultedFuture_ZeroUnobservedLines()
    {
        // awaitAny marks the winner AND the cancelled losers as observed.
        var (_, report) = RunAndReport(@"
try {
    let f1 = task.run(() => { throw ValueError { message: ""e"" }; });
    let f2 = task.run(() => { time.sleep(10); return 42; });
    time.sleep(0.3);
    let result = task.awaitAny([f1, f2]);
} catch(err) { }
");
        Assert.DoesNotContain("warning:", report);
    }

    // ── task.all ─────────────────────────────────────────────────────────────

    [Fact]
    public void TaskAll_FaultedFuture_ZeroUnobservedLines()
    {
        // task.all marks each constituent observed when it collects results.
        var (_, report) = RunAndReport(@"
try {
    let f = task.run(() => { throw ValueError { message: ""e"" }; });
    let combined = task.all([f]);
    await combined;
} catch(err) { }
");
        Assert.DoesNotContain("warning:", report);
    }

    // ── task.race ─────────────────────────────────────────────────────────────

    [Fact]
    public void TaskRace_FaultedFuture_ZeroUnobservedLines()
    {
        // task.race marks all constituents observed (winner + rest).
        var (_, report) = RunAndReport(@"
try {
    let f = task.run(() => { throw ValueError { message: ""e"" }; });
    let raced = task.race([f]);
    await raced;
} catch(err) { }
");
        Assert.DoesNotContain("warning:", report);
    }

    // ── task.awaitAny losers are also marked observed ─────────────────────────

    [Fact]
    public void TaskAwaitAny_Loser_AlsoObserved()
    {
        // When f1 wins and f2 loses (cancelled), f2 must also be marked observed
        // so its potential fault (from cancellation) is not reported.
        var (_, report) = RunAndReport(@"
let f1 = task.run(() => { return 1; });
let f2 = task.run(() => { time.sleep(10); return 2; });
let result = task.awaitAny([f1, f2]);
time.sleep(0.3);
");
        Assert.DoesNotContain("warning:", report);
    }

    // ── Programmatic consumer enumeration table ───────────────────────────────

    [Theory]
    [InlineData("await")]
    [InlineData("task.await")]
    [InlineData("task.awaitAll")]
    [InlineData("task.awaitAny")]
    [InlineData("task.all")]
    [InlineData("task.race")]
    public void AllConsumers_SuccessfulFuture_ZeroUnobservedLines(string consumerName)
    {
        // A SUCCESSFUL future consumed by any combinator should never appear in the report.
        string source = consumerName switch
        {
            "await" => @"
let f = task.run(() => { return 42; });
let r = await f;
",
            "task.await" => @"
let f = task.run(() => { return 42; });
let r = task.await(f);
",
            "task.awaitAll" => @"
let f = task.run(() => { return 42; });
let results = task.awaitAll([f]);
",
            "task.awaitAny" => @"
let f = task.run(() => { return 42; });
let r = task.awaitAny([f]);
",
            "task.all" => @"
let f = task.run(() => { return 42; });
let combined = task.all([f]);
let r = await combined;
",
            "task.race" => @"
let f = task.run(() => { return 42; });
let raced = task.race([f]);
let r = await raced;
",
            _ => throw new ArgumentException($"Unknown consumer: {consumerName}")
        };

        var (_, report) = RunAndReport(source);
        Assert.DoesNotContain("warning:", report);
    }
}
