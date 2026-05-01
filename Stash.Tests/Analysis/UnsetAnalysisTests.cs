using System.Linq;
using Stash.Analysis;
using Stash.Lexing;
using Stash.Parsing;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for static analysis rules SA0840–SA0844 emitted by the <c>unset</c> statement,
/// and for interactions with SA0201/SA0202.
/// Covers spec §9.2.
/// </summary>
public class UnsetAnalysisTests : AnalysisTestBase
{
    /// <summary>
    /// Runs the full analysis pipeline with <c>isRepl = true</c> on the given source.
    /// </summary>
    private static List<SemanticDiagnostic> ValidateAsRepl(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var stmts = new Parser(tokens).ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        var validator = new SemanticValidator(scopeTree, isRepl: true);
        return validator.Validate(stmts);
    }

    // =========================================================================
    // SA0840 — unknown / never-declared target
    // =========================================================================

    [Fact]
    public void SA0840_UndeclaredTarget_EmitsWarning()
    {
        var diagnostics = Validate("unset undeclared_xyz;");

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0840" &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void SA0840_DeclaredTarget_NoWarning()
    {
        var diagnostics = Validate("""
            let x = 1;
            unset x;
            """);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0840");
    }

    // =========================================================================
    // SA0841 — cannot unset a built-in namespace or function
    // =========================================================================

    [Fact]
    public void SA0841_BuiltInNamespace_EmitsError()
    {
        var diagnostics = Validate("unset arr;");

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0841" &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0841_BuiltInGlobalFunction_EmitsError()
    {
        var diagnostics = Validate("unset print;");

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0841" &&
            d.Level == DiagnosticLevel.Error);
    }

    // =========================================================================
    // SA0842 — cannot unset an import binding
    // =========================================================================

    [Fact]
    public void SA0842_ImportBinding_EmitsError()
    {
        // `import { lib } from "lib"` → ImportStmt → detail "imported from lib" → SA0842.
        var diagnostics = Validate("""import { lib } from "lib"; unset lib;""");

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0842" &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0842_ImportAlias_EmitsError()
    {
        // `import "lib" as L` → ImportAsStmt → detail "namespace from lib".
        // SA0842 should fire per spec §5.1 / §6.2.
        var diagnostics = Validate("""import "lib" as L; unset L;""");

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0842" &&
            d.Level == DiagnosticLevel.Error);
    }

    // =========================================================================
    // SA0843 — cannot unset a const in a script; allowed in REPL
    // =========================================================================

    [Fact]
    public void SA0843_ScriptConst_EmitsError()
    {
        var diagnostics = Validate("const C = 1; unset C;");

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0843" &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0843_ReplConst_NoError()
    {
        // In REPL mode (isRepl: true), unsetting a const is explicitly allowed.
        var diagnostics = ValidateAsRepl("const C = 1; unset C;");

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0843");
    }

    // =========================================================================
    // SA0844 — unset must be at the top level
    // =========================================================================

    [Fact]
    public void SA0844_InsideFunction_EmitsError()
    {
        var diagnostics = Validate("""
            fn f() {
                let x = 1;
                unset x;
            }
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0844" &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void SA0844_InsideBlock_EmitsError()
    {
        // `unset` inside an `if` block is not top-level — SA0844 should fire.
        var diagnostics = Validate("""
            if (true) {
                let x = 1;
                unset x;
            }
            """);

        Assert.Contains(diagnostics, d =>
            d.Code == "SA0844" &&
            d.Level == DiagnosticLevel.Error);
    }

    // =========================================================================
    // SA0202 — subsequent reference after unset is undefined
    // =========================================================================

    [Fact]
    public void SA0202_AccessAfterUnset_EmitsUndefinedError()
    {
        // After `unset x`, any reference to x in the same compilation unit
        // is undefined — SA0202 should be emitted.
        var diagnostics = Validate("""
            let x = 1;
            unset x;
            print(x);
            """);

        Assert.Contains(diagnostics, d => d.Code == "SA0202");
    }

    [Fact]
    public void NoSA0202_RebindAfterUnset_NoError()
    {
        // Re-declaring `x` after `unset x` is a clean declaration — no SA0202.
        var diagnostics = Validate("""
            let x = 1;
            unset x;
            let x = 2;
            print(x);
            """);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code is "SA0202" or "SA0201");
    }

    // =========================================================================
    // SA0841 — mixed valid/invalid targets: error only on the built-in
    // =========================================================================

    [Fact]
    public void SA0841_MixedTargets_OnlyBuiltInGetsError()
    {
        var diagnostics = Validate("""
            let validVar = 1;
            unset arr, validVar;
            """);

        // SA0841 on 'arr'
        Assert.Contains(diagnostics, d =>
            d.Code == "SA0841" &&
            d.Message.Contains("arr"));

        // No SA0841 on validVar
        Assert.DoesNotContain(diagnostics, d =>
            d.Code == "SA0841" &&
            d.Message.Contains("validVar"));
    }

    // =========================================================================
    // SA0201 — unset counts as a use; no unused-variable warning
    // =========================================================================

    [Fact]
    public void NoSA0201_UnsetCountsAsUse_NoUnusedVarWarning()
    {
        // `let x = 1;` followed only by `unset x;` — x is "used" by being unset.
        var diagnostics = Validate("""
            let x = 1;
            unset x;
            """);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == "SA0201" &&
            d.Message.Contains("'x'"));
    }
}
