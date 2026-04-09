using Stash.Analysis;

namespace Stash.Tests.Analysis;

public class Phase1DiagnosticsTests : AnalysisTestBase
{
    // ── SA0205: let-could-be-const ─────────────────────────────────

    [Fact]
    public void LetCouldBeConst_NeverReassigned_ReportsDiagnostic()
    {
        var diagnostics = Validate("let x = 5; io.println(x);");
        Assert.Contains(diagnostics, d => d.Code == "SA0205" && d.Message.Contains("'x'"));
    }

    [Fact]
    public void LetCouldBeConst_Reassigned_NoDiagnostic()
    {
        var diagnostics = Validate("let x = 5; x = 10; io.println(x);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0205");
    }

    [Fact]
    public void LetCouldBeConst_Const_NoDiagnostic()
    {
        var diagnostics = Validate("const x = 5; io.println(x);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0205");
    }

    [Fact]
    public void LetCouldBeConst_Underscore_Ignored()
    {
        var diagnostics = Validate("let _ = 5;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0205");
    }

    [Fact]
    public void LetCouldBeConst_UpdateExpr_NoDiagnostic()
    {
        var diagnostics = Validate("let x = 0; x++; io.println(x);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0205");
    }

    [Fact]
    public void LetCouldBeConst_CompoundAssign_NoDiagnostic()
    {
        var diagnostics = Validate("let x = 0; x += 1; io.println(x);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0205");
    }

    [Fact]
    public void LetCouldBeConst_CatchVariable_NoDiagnostic()
    {
        var diagnostics = Validate("try { io.println(1); } catch (e) { io.println(e); }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0205" && d.Message.Contains("'e'"));
    }

    // ── SA0206: Unused parameter ───────────────────────────────────

    [Fact]
    public void UnusedParam_ReportsDiagnostic()
    {
        var diagnostics = Validate("fn foo(x) { return 1; }");
        Assert.Contains(diagnostics, d => d.Code == "SA0206" && d.Message.Contains("'x'"));
    }

    [Fact]
    public void UnusedParam_UsedInBody_NoDiagnostic()
    {
        var diagnostics = Validate("fn foo(x) { return x; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0206");
    }

    [Fact]
    public void UnusedParam_UnderscorePrefix_Ignored()
    {
        var diagnostics = Validate("fn foo(_x) { return 1; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0206");
    }

    [Fact]
    public void UnusedParam_BarUnderscore_Ignored()
    {
        var diagnostics = Validate("fn foo(_) { return 1; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0206");
    }

    [Fact]
    public void UnusedParam_MultipleParams_OnlyUnusedReported()
    {
        var diagnostics = Validate("fn foo(a, b) { return a; }");
        Assert.Contains(diagnostics, d => d.Code == "SA0206" && d.Message.Contains("'b'"));
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0206" && d.Message.Contains("'a'"));
    }

    // ── SA0207: Shadow variable ────────────────────────────────────

    [Fact]
    public void ShadowVariable_InnerBlock_ReportsDiagnostic()
    {
        var diagnostics = Validate("const x = 1; { const x = 2; io.println(x); } io.println(x);");
        Assert.Contains(diagnostics, d => d.Code == "SA0207" && d.Message.Contains("'x'"));
    }

    [Fact]
    public void ShadowVariable_DifferentName_NoDiagnostic()
    {
        var diagnostics = Validate("const x = 1; { const y = 2; io.println(y); } io.println(x);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0207");
    }

    [Fact]
    public void ShadowVariable_FunctionParam_ReportsDiagnostic()
    {
        var diagnostics = Validate("const x = 1; fn foo(x) { return x; } foo(2); io.println(x);");
        Assert.Contains(diagnostics, d => d.Code == "SA0207" && d.Message.Contains("'x'"));
    }

    [Fact]
    public void ShadowVariable_NestedFunction_ReportsDiagnostic()
    {
        var diagnostics = Validate("const x = 1; fn foo() { const x = 2; return x; } foo(); io.println(x);");
        Assert.Contains(diagnostics, d => d.Code == "SA0207" && d.Message.Contains("'x'"));
    }

    // ── SA0105: Empty block body ───────────────────────────────────

    [Fact]
    public void EmptyBlock_If_ReportsDiagnostic()
    {
        var diagnostics = Validate("if (true) {}");
        Assert.Contains(diagnostics, d => d.Code == "SA0105" && d.Message.Contains("if"));
    }

    [Fact]
    public void EmptyBlock_Else_ReportsDiagnostic()
    {
        var diagnostics = Validate("if (true) { io.println(1); } else {}");
        Assert.Contains(diagnostics, d => d.Code == "SA0105" && d.Message.Contains("else"));
    }

    [Fact]
    public void EmptyBlock_While_ReportsDiagnostic()
    {
        var diagnostics = Validate("while (false) {}");
        Assert.Contains(diagnostics, d => d.Code == "SA0105" && d.Message.Contains("while"));
    }

    [Fact]
    public void EmptyBlock_For_ReportsDiagnostic()
    {
        var diagnostics = Validate("for (let i = 0; i < 10; i++) {}");
        Assert.Contains(diagnostics, d => d.Code == "SA0105" && d.Message.Contains("for"));
    }

    [Fact]
    public void EmptyBlock_ForIn_ReportsDiagnostic()
    {
        var diagnostics = Validate("for (let _ in [1, 2]) {}");
        Assert.Contains(diagnostics, d => d.Code == "SA0105" && d.Message.Contains("for-in"));
    }

    [Fact]
    public void EmptyBlock_DoWhile_ReportsDiagnostic()
    {
        var diagnostics = Validate("do {} while (false);");
        Assert.Contains(diagnostics, d => d.Code == "SA0105" && d.Message.Contains("do-while"));
    }

    [Fact]
    public void EmptyBlock_Try_ReportsDiagnostic()
    {
        var diagnostics = Validate("try {} catch (e) { io.println(e); }");
        Assert.Contains(diagnostics, d => d.Code == "SA0105" && d.Message.Contains("try"));
    }

    [Fact]
    public void EmptyBlock_Catch_ReportsDiagnostic()
    {
        var diagnostics = Validate("try { io.println(1); } catch (e) {}");
        Assert.Contains(diagnostics, d => d.Code == "SA0105" && d.Message.Contains("catch"));
    }

    [Fact]
    public void NonEmptyBlock_If_NoDiagnostic()
    {
        var diagnostics = Validate("if (true) { io.println(1); }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0105");
    }

    // ── Diagnostic help URLs ───────────────────────────────────────

    [Fact]
    public void DiagnosticDescriptor_HelpUrl_HasCorrectFormat()
    {
        Assert.Equal("https://stash-lang.dev/docs/rules/SA0101", DiagnosticDescriptors.SA0101.HelpUrl);
        Assert.Equal("https://stash-lang.dev/docs/rules/SA0205", DiagnosticDescriptors.SA0205.HelpUrl);
    }

    // ── New descriptor registration ────────────────────────────────

    [Fact]
    public void DiagnosticDescriptors_AllByCode_IncludesNewCodes()
    {
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0003"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0105"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0205"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0206"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0207"));
    }

    // ── SA0701: Nested elevate ─────────────────────────────────────

    [Fact]
    public void NestedElevate_ReportsWarning()
    {
        var diagnostics = Validate("elevate { elevate { io.println(1); } }");
        Assert.Contains(diagnostics, d => d.Code == "SA0701");
    }

    [Fact]
    public void SingleElevate_NoDiagnostic()
    {
        var diagnostics = Validate("elevate { io.println(1); }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0701");
    }

    // ── SA0104: Unreachable code edge cases ─────────────────────────

    [Fact]
    public void UnreachableCode_AfterReturn_ReportsDiagnostic()
    {
        var diagnostics = Validate("fn foo() { return 1; io.println(2); }");
        Assert.Contains(diagnostics, d => d.Code == "SA0104" && d.IsUnnecessary);
    }

    [Fact]
    public void UnreachableCode_AfterThrow_ReportsDiagnostic()
    {
        var diagnostics = Validate("fn foo() { throw \"error\"; io.println(2); }");
        Assert.Contains(diagnostics, d => d.Code == "SA0104" && d.IsUnnecessary);
    }

    [Fact]
    public void UnreachableCode_AfterBreak_ReportsDiagnostic()
    {
        var diagnostics = Validate("while (true) { break; io.println(2); }");
        Assert.Contains(diagnostics, d => d.Code == "SA0104" && d.IsUnnecessary);
    }

    [Fact]
    public void UnreachableCode_AfterContinue_ReportsDiagnostic()
    {
        var diagnostics = Validate("while (true) { continue; io.println(2); }");
        Assert.Contains(diagnostics, d => d.Code == "SA0104" && d.IsUnnecessary);
    }

    // ── SA0708: Backoff without delay ──────────────────────────────

    [Fact]
    public void RetryBackoffWithoutDelay_ReportsDiagnostic()
    {
        var source = @"fn foo() { return retry (3, backoff: ""exponential"") { io.println(1); }; }";
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0708");
    }
}
