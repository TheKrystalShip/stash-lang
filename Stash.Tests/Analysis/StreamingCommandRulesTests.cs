using System.Linq;
using Stash.Analysis;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for static analysis rules SA0711 (streaming command in pipe chain),
/// SA0712 (dual iteration requires streaming source), and SA0713 (streaming
/// command never consumed).
/// </summary>
public class StreamingCommandRulesTests : AnalysisTestBase
{
    // =========================================================================
    // SA0711 — streaming command in pipe chain
    // =========================================================================

    [Fact]
    public void SA0711_StreamingOnLeftOfPipe_EmitsError()
    {
        var diagnostics = Validate("$<(tail -f log) | $(grep error);");
        Assert.Contains(diagnostics, d => d.Code == "SA0711" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0711_StreamingOnRightOfPipe_EmitsError()
    {
        var diagnostics = Validate("$(cat file) | $<(grep error);");
        Assert.Contains(diagnostics, d => d.Code == "SA0711" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0711_CaptureInPipe_NoDiagnostic()
    {
        var diagnostics = Validate("$(cat file) | $(grep error);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0711");
    }

    [Fact]
    public void SA0711_StandaloneStreaming_NoDiagnostic()
    {
        var diagnostics = Validate("let s = $<(tail -f log);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0711");
    }

    // =========================================================================
    // SA0712 — dual iteration requires streaming source
    // =========================================================================

    [Fact]
    public void SA0712_DualIterOverCapture_EmitsError()
    {
        var diagnostics = Validate("for (let a, b in $(ls)) {}");
        Assert.Contains(diagnostics, d => d.Code == "SA0712" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0712_DualIterOverArrayLiteral_EmitsError()
    {
        var diagnostics = Validate("for (let a, b in [1, 2, 3]) {}");
        Assert.Contains(diagnostics, d => d.Code == "SA0712" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0712_DualIterOverStreaming_NoDiagnostic()
    {
        var diagnostics = Validate("for (let a, b in $<(tail -f log)) {}");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0712");
    }

    [Fact]
    public void SA0712_DualIterOverDictLiteral_NoDiagnostic()
    {
        var diagnostics = Validate("for (let k, v in {a: 1, b: 2}) {}");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0712");
    }

    [Fact]
    public void SA0712_SingleVarIterOverCapture_NoDiagnostic()
    {
        var diagnostics = Validate("for (let x in $(ls)) {}");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0712");
    }

    // =========================================================================
    // SA0713 — streaming command never consumed
    // =========================================================================

    [Fact]
    public void SA0713_BareStreamingStatement_EmitsWarning()
    {
        var diagnostics = Validate("$<(tail -f log);");
        Assert.Contains(diagnostics, d => d.Code == "SA0713" && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0713_AssignedStreaming_NoDiagnostic()
    {
        var diagnostics = Validate("let s = $<(tail -f log);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0713");
    }

    [Fact]
    public void SA0713_IteratedStreaming_NoDiagnostic()
    {
        var diagnostics = Validate("for (let l in $<(tail -f log)) {}");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0713");
    }

    [Fact]
    public void SA0713_StreamingMethodCall_NoDiagnostic()
    {
        var diagnostics = Validate("$<(tail -f log).lines();");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0713");
    }
}
