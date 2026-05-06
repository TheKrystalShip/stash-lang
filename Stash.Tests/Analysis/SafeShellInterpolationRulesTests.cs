using System.Linq;
using Stash.Analysis;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for static analysis rules SA0815 (redundant quotes around interpolation slot),
/// SA0816 (implicit array splat in command), and SA0817 (whitespace-string migration).
/// </summary>
public class SafeShellInterpolationRulesTests : AnalysisTestBase
{
    // =========================================================================
    // SA0815 — redundant quotes around interpolation slot
    // =========================================================================

    [Fact]
    public void SA0815_DoubleQuotedInterpolation_EmitsWarning()
    {
        var diagnostics = Validate("""let x = "hi"; $(echo "${x}");""");
        Assert.Contains(diagnostics, d => d.Code == "SA0815" && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0815_SingleQuotedInterpolation_EmitsWarning()
    {
        var diagnostics = Validate("""let x = "hi"; $(echo '${x}');""");
        Assert.Contains(diagnostics, d => d.Code == "SA0815" && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0815_UnquotedInterpolation_NoDiagnostic()
    {
        var diagnostics = Validate("""let x = "hi"; $(echo ${x});""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0815");
    }

    [Fact]
    public void SA0815_QuotedPrefixBeforeInterpolation_NoDiagnostic()
    {
        // The open quote is followed by literal text ("prefix") before the slot — not isolating the slot
        var diagnostics = Validate("""let x = "hi"; $(echo "prefix${x}");""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0815");
    }

    [Fact]
    public void SA0815_LiteralNoInterpolation_NoDiagnostic()
    {
        var diagnostics = Validate("""$(echo "literal");""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0815");
    }

    // =========================================================================
    // SA0816 — implicit array splat in command interpolation
    // =========================================================================

    [Fact]
    public void SA0816_ArrayVariableInterpolated_EmitsInfo()
    {
        var diagnostics = Validate("""let arr = ["a", "b"]; $(echo ${arr});""");
        Assert.Contains(diagnostics, d => d.Code == "SA0816" && d.Level == DiagnosticLevel.Information);
    }

    [Fact]
    public void SA0816_ExplicitSpreadArrayVariable_NoDiagnostic()
    {
        var diagnostics = Validate("""let arr = ["a", "b"]; $(echo ${...arr});""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0816");
    }

    [Fact]
    public void SA0816_InlineArrayLiteral_EmitsInfo()
    {
        var diagnostics = Validate("""$(echo ${[1, 2, 3]});""");
        Assert.Contains(diagnostics, d => d.Code == "SA0816" && d.Level == DiagnosticLevel.Information);
    }

    [Fact]
    public void SA0816_StringVariableInterpolated_NoDiagnostic()
    {
        var diagnostics = Validate("""let s = "hi"; $(echo ${s});""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0816");
    }

    [Fact]
    public void SA0816_IntVariableInterpolated_NoDiagnostic()
    {
        var diagnostics = Validate("let i = 5; $(echo ${i});");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0816");
    }

    // =========================================================================
    // SA0817 — likely-broken whitespace-string migration
    // =========================================================================

    [Fact]
    public void SA0817_StringWithInternalWhitespace_EmitsWarning()
    {
        var diagnostics = Validate("""let opts = "-la /tmp"; $(ls ${opts});""");
        Assert.Contains(diagnostics, d => d.Code == "SA0817" && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0817_StringWithNoWhitespace_NoDiagnostic()
    {
        var diagnostics = Validate("""let opts = "-la"; $(ls ${opts});""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0817");
    }

    [Fact]
    public void SA0817_StringWithOnlyLeadingTrailingWhitespace_NoDiagnostic()
    {
        // "  -la " has only leading and trailing whitespace — no internal whitespace
        var diagnostics = Validate("""let opts = "  -la "; $(ls ${opts});""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0817");
    }

    [Fact]
    public void SA0817_StringWithInternalWhitespaceSingleWord_EmitsWarning()
    {
        var diagnostics = Validate("""let opts = "a b"; $(echo ${opts});""");
        Assert.Contains(diagnostics, d => d.Code == "SA0817" && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0817_CallInitializer_NoDiagnostic()
    {
        // Initializer is a function call — too risky to flag (conservative)
        var diagnostics = Validate("""let opts = str.join(["-l", "-a"], " "); $(ls ${opts});""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0817");
    }
}
