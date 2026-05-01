using System.Linq;
using Stash.Analysis;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for static analysis rules SA0850 (invalid alias name) and SA0851 (empty confirm prompt).
/// </summary>
public class AliasDefineRuleTests : AnalysisTestBase
{
    // =========================================================================
    // SA0850 — invalid alias name
    // =========================================================================

    [Fact]
    public void SA0850_ValidName_NoDiagnostic()
    {
        var diagnostics = Validate("""alias.define("g", "git ${args}");""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0850");
    }

    [Fact]
    public void SA0850_ValidNameWithUnderscore_NoDiagnostic()
    {
        var diagnostics = Validate("""alias.define("git_status", "git status");""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0850");
    }

    [Fact]
    public void SA0850_ValidNameWithDigitNotFirst_NoDiagnostic()
    {
        var diagnostics = Validate("""alias.define("g2", "git ${args}");""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0850");
    }

    [Fact]
    public void SA0850_NameWithDot_EmitsError()
    {
        var diagnostics = Validate("""alias.define("g.s", "git status");""");
        Assert.Contains(diagnostics, d =>
            d.Code == "SA0850" &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0850_NameStartsWithDigit_EmitsError()
    {
        var diagnostics = Validate("""alias.define("3g", "git ${args}");""");
        Assert.Contains(diagnostics, d =>
            d.Code == "SA0850" &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0850_EmptyName_EmitsError()
    {
        var diagnostics = Validate("""alias.define("", "git ${args}");""");
        Assert.Contains(diagnostics, d =>
            d.Code == "SA0850" &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0850_NameWithSpace_EmitsError()
    {
        var diagnostics = Validate("""alias.define("g s", "git status");""");
        Assert.Contains(diagnostics, d =>
            d.Code == "SA0850" &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0850_NameWithSlash_EmitsError()
    {
        var diagnostics = Validate("""alias.define("usr/bin", "git ${args}");""");
        Assert.Contains(diagnostics, d =>
            d.Code == "SA0850" &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0850_VariableName_NoDiagnostic()
    {
        // Non-literal first arg — no static diagnostic; runtime check covers it
        var diagnostics = Validate("""
            let name = "g";
            alias.define(name, "git ${args}");
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0850");
    }

    [Fact]
    public void SA0850_MessageContainsName()
    {
        var diagnostics = Validate("""alias.define("bad name", "git ${args}");""");
        var d = diagnostics.FirstOrDefault(x => x.Code == "SA0850");
        Assert.NotNull(d);
        Assert.Contains("bad name", d!.Message);
    }

    // =========================================================================
    // SA0851 — empty confirm prompt
    // =========================================================================

    [Fact]
    public void SA0851_EmptyConfirm_EmitsWarning()
    {
        var diagnostics = Validate("""alias.define("g", "git ${args}", AliasOptions { confirm: "" });""");
        Assert.Contains(diagnostics, d =>
            d.Code == "SA0851" &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0851_NonEmptyConfirm_NoDiagnostic()
    {
        var diagnostics = Validate("""alias.define("g", "git ${args}", AliasOptions { confirm: "are you sure?" });""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0851");
    }

    [Fact]
    public void SA0851_NoOpts_NoDiagnostic()
    {
        var diagnostics = Validate("""alias.define("g", "git ${args}");""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0851");
    }

    [Fact]
    public void SA0851_OptsWithoutConfirm_NoDiagnostic()
    {
        var diagnostics = Validate("""alias.define("g", "git ${args}", AliasOptions { description: "git shorthand" });""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0851");
    }

    // =========================================================================
    // Both SA0850 + SA0851 together
    // =========================================================================

    [Fact]
    public void BothDiagnostics_EmittedTogether()
    {
        var diagnostics = Validate("""alias.define("bad name", "git ${args}", AliasOptions { confirm: "" });""");
        Assert.Contains(diagnostics, d => d.Code == "SA0850");
        Assert.Contains(diagnostics, d => d.Code == "SA0851");
    }
}
