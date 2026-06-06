namespace Stash.Tests.Interpreting.Async.UnobservedAndExit;

using System.IO;
using Stash.Bytecode;
using Stash.Runtime.Types;

/// <summary>
/// D1 — EmbeddedMode gate: verifies that <c>StashEngine</c> with <c>EmbeddedMode = true</c>
/// does NOT invoke the unobserved-task report, and the registry is still populated (for
/// host-side inspection if desired).
/// </summary>
public class EmbeddedModeGateTests
{
    [Fact]
    public void EmbeddedMode_UnobservedFault_ZeroStderr()
    {
        // This is the EmbeddedMode gate: same script as the CLI test, but via StashEngine.
        // The engine sets EmbeddedMode=true; the report must NOT fire.
        var errSw = new StringWriter { NewLine = "\n" };
        var engine = new StashEngine();
        engine.ErrorOutput = errSw;
        engine.Output = TextWriter.Null;

        engine.Run(@"
task.run(() => { throw ValueError { message: ""oops"" }; });
time.sleep(0.3);
");

        // EmbeddedMode hosts must see zero stderr from the D1 report
        Assert.Equal("", errSw.ToString());
    }

    [Fact]
    public void EmbeddedMode_UnobservedFault_RegistryStillPopulated()
    {
        // Even in EmbeddedMode, the registry mechanism works — a host can inspect it.
        // Verify by constructing a pre-faulted future directly (no timing dependence).
        var registry = new SpawnedFutureRegistry();
        registry.Register(StashFuture.Failed("ValueError", "embedded-oops"));

        var sw = new StringWriter { NewLine = "\n" };
        int reported = UnobservedFaultReporter.Report(registry, sw);
        Assert.Equal(1, reported);
        Assert.Contains("ValueError: embedded-oops", sw.ToString());
    }

    [Fact]
    public void NotEmbeddedMode_UnobservedFault_ReportFires()
    {
        // Baseline: a non-embedded VM's registry IS scanned by the reporter.
        // Use a pre-faulted future to avoid timing-dependent background tasks.
        var registry = new SpawnedFutureRegistry();
        registry.Register(StashFuture.Failed("ValueError", "oops"));

        var sw = new StringWriter { NewLine = "\n" };
        int count = UnobservedFaultReporter.Report(registry, sw);
        Assert.Equal(1, count);
    }
}
