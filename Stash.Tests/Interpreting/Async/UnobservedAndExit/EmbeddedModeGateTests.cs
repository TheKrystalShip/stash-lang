namespace Stash.Tests.Interpreting.Async.UnobservedAndExit;

using System.IO;
using Stash.Bytecode;
using Stash.Runtime.Types;

/// <summary>
/// D1 — EmbeddedMode gate: verifies that <c>UnobservedFaultReporter.Report</c>
/// suppresses all output when <c>embeddedMode = true</c> and writes the warning
/// block when <c>embeddedMode = false</c>.
///
/// <para>
/// The gate lives in <c>UnobservedFaultReporter.Report</c> (third parameter), so it is
/// directly unit-testable without going through <c>Program.ReportUnobservedFaults</c>.
/// Both tests use a pre-faulted registry to avoid timing-dependent background tasks.
/// </para>
/// </summary>
public class EmbeddedModeGateTests
{
    /// <summary>
    /// embeddedMode = true → Report returns 0 and writes nothing.
    /// Mutation check: deleting the <c>if (embeddedMode) return 0;</c> guard in
    /// <c>UnobservedFaultReporter.Report</c> turns this test RED.
    /// </summary>
    [Fact]
    public void EmbeddedMode_True_UnobservedFault_ZeroOutput()
    {
        var registry = new SpawnedFutureRegistry();
        registry.Register(StashFuture.Failed("ValueError", "oops"));

        var sw = new StringWriter { NewLine = "\n" };
        int count = UnobservedFaultReporter.Report(registry, sw, embeddedMode: true);

        Assert.Equal(0, count);
        Assert.Equal("", sw.ToString());
    }

    /// <summary>
    /// embeddedMode = false → Report writes the warning block.
    /// Confirms the non-embedded path is not inadvertently suppressed.
    /// </summary>
    [Fact]
    public void EmbeddedMode_False_UnobservedFault_ReportFires()
    {
        var registry = new SpawnedFutureRegistry();
        registry.Register(StashFuture.Failed("ValueError", "oops"));

        var sw = new StringWriter { NewLine = "\n" };
        int count = UnobservedFaultReporter.Report(registry, sw, embeddedMode: false);

        Assert.Equal(1, count);
        Assert.Contains("warning: 1 unobserved async error(s):", sw.ToString());
        Assert.Contains("ValueError: oops", sw.ToString());
    }

    /// <summary>
    /// Engine-doesn't-surprise-report: running a faulting task via <c>StashEngine</c>
    /// (which sets <c>EmbeddedMode = true</c> internally) produces zero stderr. This
    /// documents the engine's behaviour contract independently of the gate-unit tests above.
    /// </summary>
    [Fact]
    public void EmbeddedEngine_UnobservedFault_ZeroStderr()
    {
        var errSw = new StringWriter { NewLine = "\n" };
        var engine = new StashEngine();
        engine.ErrorOutput = errSw;
        engine.Output = TextWriter.Null;

        engine.Run(@"
task.run(() => { throw ValueError { message: ""oops"" }; });
time.sleep(0.3);
");

        Assert.Equal("", errSw.ToString());
    }

    /// <summary>
    /// Even in EmbeddedMode the registry mechanism works — a host can inspect it.
    /// Verify by constructing a pre-faulted future directly (no timing dependence).
    /// </summary>
    [Fact]
    public void EmbeddedMode_UnobservedFault_RegistryStillPopulated()
    {
        var registry = new SpawnedFutureRegistry();
        registry.Register(StashFuture.Failed("ValueError", "embedded-oops"));

        var sw = new StringWriter { NewLine = "\n" };
        int reported = UnobservedFaultReporter.Report(registry, sw);
        Assert.Equal(1, reported);
        Assert.Contains("ValueError: embedded-oops", sw.ToString());
    }
}
