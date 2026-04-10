using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Analysis;
using Xunit;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for the 10 new best-practice analysis rules:
/// SA0901, SA1002, SA1102, SA1103, SA1105, SA1106, SA1107, SA1108, SA1401, SA1402.
/// </summary>
public class BestPracticeRuleTests
{
    private static List<SemanticDiagnostic> Validate(string source)
    {
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var uri = new Uri("file:///test.stash");
        var result = engine.Analyze(uri, source, noImports: true);
        return result.SemanticDiagnostics;
    }

    // ── SA0901 — NoUnnecessaryElse ────────────────────────────────

    [Fact]
    public void SA0901_ReturnInThen_WithElse_ReportsUnnecessaryElse()
    {
        var diagnostics = Validate("fn test(cond) { if (cond) { return 1; } else { io.println(cond); } }");
        Assert.Contains(diagnostics, d => d.Code == "SA0901");
    }

    [Fact]
    public void SA0901_NoReturnInThen_WithElse_NoReport()
    {
        var diagnostics = Validate("fn test(cond) { if (cond) { io.println(cond); } else { io.println(\"b\"); } }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0901");
    }

    [Fact]
    public void SA0901_ReturnInThen_NoElse_NoReport()
    {
        var diagnostics = Validate("fn test(cond) { if (cond) { return 1; } }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0901");
    }

    // ── SA1002 — MaxDepth ─────────────────────────────────────────

    [Fact]
    public void SA1002_DeepNesting_ReportsHighDepth()
    {
        string source = @"fn deep() {
  if (true) {
    if (true) {
      if (true) {
        if (true) {
          if (true) {
            if (true) {
              let x = 1;
            }
          }
        }
      }
    }
  }
}";
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA1002");
    }

    [Fact]
    public void SA1002_ShallowNesting_NoReport()
    {
        // 3 nested ifs → maxDepth = 3 ≤ 5 (threshold).
        var diagnostics = Validate(@"fn shallow() {
  if (true) {
    if (true) {
      if (true) {
        let x = 1;
      }
    }
  }
}");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1002");
    }

    // ── SA1102 — NoSelfAssign ─────────────────────────────────────

    [Fact]
    public void SA1102_SelfAssign_Reports()
    {
        var diagnostics = Validate("let x = 1; x = x;");
        Assert.Contains(diagnostics, d => d.Code == "SA1102");
    }

    [Fact]
    public void SA1102_DifferentAssign_NoReport()
    {
        var diagnostics = Validate("let x = 1; x = 2;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1102");
    }

    [Fact]
    public void SA1102_SelfAssign_HasFix()
    {
        Assert.True(DiagnosticDescriptors.SA1102.IsFixable);
        Assert.Equal(FixApplicability.Safe, DiagnosticDescriptors.SA1102.DefaultFixApplicability);
    }

    // ── SA1103 — NoDuplicateCase ──────────────────────────────────

    [Fact]
    public void SA1103_DuplicateLiteral_Reports()
    {
        var diagnostics = Validate("let x = 1; let y = x switch { 1 => \"a\", 1 => \"b\", _ => \"c\" };");
        Assert.Contains(diagnostics, d => d.Code == "SA1103");
    }

    [Fact]
    public void SA1103_UniqueValues_NoReport()
    {
        var diagnostics = Validate("let x = 1; let y = x switch { 1 => \"a\", 2 => \"b\", _ => \"c\" };");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1103");
    }

    // ── SA1105 — NoLoneBlocks ─────────────────────────────────────

    [Fact]
    public void SA1105_LoneBlock_Reports()
    {
        var diagnostics = Validate("{ let x = 1; }");
        Assert.Contains(diagnostics, d => d.Code == "SA1105");
    }

    [Fact]
    public void SA1105_LoneBlockInsideFunction_Reports()
    {
        var diagnostics = Validate("fn test() { { let x = 1; } }");
        Assert.Contains(diagnostics, d => d.Code == "SA1105");
    }

    [Fact]
    public void SA1105_IfBlock_NoReport()
    {
        var diagnostics = Validate("let x = 1; if (x) { let y = 2; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1105");
    }

    // ── SA1106 — NoSelfCompare ────────────────────────────────────

    [Fact]
    public void SA1106_SameIdentifier_Equals_Reports()
    {
        var diagnostics = Validate("let x = 1; let cond = x == x;");
        Assert.Contains(diagnostics, d => d.Code == "SA1106");
    }

    [Fact]
    public void SA1106_DifferentIdentifiers_NoReport()
    {
        var diagnostics = Validate("let x = 1; let y = 2; let cond = x == y;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1106");
    }

    // ── SA1107 — NoConstantCondition ─────────────────────────────

    [Fact]
    public void SA1107_IfTrue_Reports()
    {
        var diagnostics = Validate("if (true) { }");
        Assert.Contains(diagnostics, d => d.Code == "SA1107");
    }

    [Fact]
    public void SA1107_IfFalse_Reports()
    {
        var diagnostics = Validate("if (false) { }");
        Assert.Contains(diagnostics, d => d.Code == "SA1107");
    }

    [Fact]
    public void SA1107_WhileTrue_NoReport()
    {
        // while(true) is exempted — it's the canonical infinite-loop idiom.
        var diagnostics = Validate("while (true) { break; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1107");
    }

    [Fact]
    public void SA1107_WhileFalse_Reports()
    {
        var diagnostics = Validate("while (false) { }");
        Assert.Contains(diagnostics, d => d.Code == "SA1107");
    }

    [Fact]
    public void SA1107_IfVariable_NoReport()
    {
        var diagnostics = Validate("let x = 1; if (x) { }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1107");
    }

    // ── SA1108 — NoUnreachableLoop ────────────────────────────────

    [Fact]
    public void SA1108_LoopWithUnconditionalReturn_Reports()
    {
        var diagnostics = Validate("fn test() { while (true) { return; } }");
        Assert.Contains(diagnostics, d => d.Code == "SA1108");
    }

    [Fact]
    public void SA1108_LoopWithBreak_NoReport()
    {
        // break is the expected exit mechanism and is explicitly excluded.
        var diagnostics = Validate("while (true) { break; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1108");
    }

    [Fact]
    public void SA1108_LoopWithConditionalReturn_NoReport()
    {
        // The last statement is an if, not an unconditional return.
        var diagnostics = Validate("fn test(x) { while (true) { if (x) { return; } } }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1108");
    }

    // ── SA1401 — UseOptionalChaining ─────────────────────────────

    [Fact]
    public void SA1401_NullCheckWithMemberAccess_Reports()
    {
        var diagnostics = Validate("let a = null; let y = a != null ? a.b : null;");
        Assert.Contains(diagnostics, d => d.Code == "SA1401");
    }

    [Fact]
    public void SA1401_NoNullCheck_NoReport()
    {
        var diagnostics = Validate("let a = 1; let y = a > 0 ? a : null;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1401");
    }

    // ── SA1402 — UseNullCoalescing ────────────────────────────────

    [Fact]
    public void SA1402_NullCheckTernary_Reports()
    {
        var diagnostics = Validate("let a = null; let y = a != null ? a : \"default\";");
        Assert.Contains(diagnostics, d => d.Code == "SA1402");
    }

    [Fact]
    public void SA1402_NoNullCheck_NoReport()
    {
        var diagnostics = Validate("let a = 1; let y = a > 0 ? a : \"default\";");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1402");
    }
}
