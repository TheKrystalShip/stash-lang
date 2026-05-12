using System.Linq;
using Stash.Analysis;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for SA0860 — "Use built-in error type instead of dict literal".
/// Fires when <c>throw { type: "&lt;built-in name&gt;", ... }</c> is used and
/// the type string matches a known built-in error type.
/// </summary>
public class SA0860Tests : AnalysisTestBase
{
    // =========================================================================
    // SA0860 — fires on built-in error type names
    // =========================================================================

    [Fact]
    public void SA0860_ThrowDictWithIOError_EmitsWarning()
    {
        var diagnostics = Validate("""throw { type: "IOError", message: "file not found" };""");
        Assert.Contains(diagnostics, d =>
            d.Code == "SA0860" &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0860_ThrowDictWithCommandError_EmitsWarning()
    {
        var diagnostics = Validate("""throw { type: "CommandError", message: "failed" };""");
        Assert.Contains(diagnostics, d =>
            d.Code == "SA0860" &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0860_ThrowDictWithValueError_EmitsWarning()
    {
        var diagnostics = Validate("""throw { type: "ValueError", message: "bad value" };""");
        Assert.Contains(diagnostics, d =>
            d.Code == "SA0860" &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0860_ThrowDictWithTypeError_EmitsWarning()
    {
        var diagnostics = Validate("""throw { type: "TypeError", message: "wrong type" };""");
        Assert.Contains(diagnostics, d => d.Code == "SA0860");
    }

    [Fact]
    public void SA0860_ThrowDictWithParseError_EmitsWarning()
    {
        var diagnostics = Validate("""throw { type: "ParseError", message: "bad syntax" };""");
        Assert.Contains(diagnostics, d => d.Code == "SA0860");
    }

    // =========================================================================
    // SA0860 — does NOT fire for non-built-in type names
    // =========================================================================

    [Fact]
    public void SA0860_ThrowDictWithUserDefinedType_NoDiagnostic()
    {
        var diagnostics = Validate("""throw { type: "MyAppError", message: "something went wrong" };""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0860");
    }

    [Fact]
    public void SA0860_ThrowDictWithCustomErrorName_NoDiagnostic()
    {
        var diagnostics = Validate("""throw { type: "NetworkError", message: "connection refused" };""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0860");
    }

    // =========================================================================
    // SA0860 — does NOT fire for non-literal type values
    // =========================================================================

    [Fact]
    public void SA0860_ThrowDictWithDynamicType_NoDiagnostic()
    {
        var diagnostics = Validate("""
            let errType = "IOError";
            throw { type: errType, message: "file not found" };
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0860");
    }

    // =========================================================================
    // SA0860 — does NOT fire for non-dict throw expressions
    // =========================================================================

    [Fact]
    public void SA0860_ThrowVariable_NoDiagnostic()
    {
        var diagnostics = Validate("""
            let e = { type: "IOError", message: "file not found" };
            throw e;
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0860");
    }

    [Fact]
    public void SA0860_ThrowDictWithoutTypeKey_NoDiagnostic()
    {
        var diagnostics = Validate("""throw { message: "something went wrong" };""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0860");
    }
}
