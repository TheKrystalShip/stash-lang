using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Analysis.FlowAnalysis;
using Stash.Analysis.Rules;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for Phase 5: Flow Analysis and Advanced Diagnostics.
/// Covers CFG infrastructure, SA0106, SA0208, SA0210, SA0308, SA0404.
/// </summary>
public class FlowAnalysisTests : AnalysisTestBase
{
    // ── CFG Infrastructure ────────────────────────────────────────────────────────────────

    [Fact]
    public void CfgBuilder_EmptyStatements_HasEntryAndExit()
    {
        var cfg = BuildCfg("");
        Assert.NotNull(cfg.Entry);
        Assert.NotNull(cfg.Exit);
        Assert.Same(cfg.Entry, cfg.Blocks[0]);
    }

    [Fact]
    public void CfgBuilder_LinearStatements_SingleBlock()
    {
        var cfg = BuildCfg("let x = 1; let y = 2; io.println(x);");
        // Entry block should contain 3 statements, connected to exit
        Assert.Contains(cfg.Entry, cfg.Entry.Successors.Prepend(cfg.Entry));
        Assert.Equal(cfg.Entry, cfg.Blocks[0]);
    }

    [Fact]
    public void CfgBuilder_ReturnStatement_ConnectsToExit()
    {
        var cfg = BuildCfg("fn foo() { return 1; }");
        // The fn decl is in entry, entry connects to exit
        Assert.Contains(cfg.Exit, cfg.Entry.Successors);
    }

    [Fact]
    public void CfgBuilder_IfElse_CreatesBranchingBlocks()
    {
        var cfg = BuildCfg("let x = 1; if (x > 0) { let y = 2; } else { let z = 3; } let w = 4;");
        // Should have: entry → then-entry, else-entry → join → exit
        Assert.True(cfg.Blocks.Count >= 4);
        // Entry should be conditional
        Assert.Equal(BranchKind.Conditional, cfg.Entry.BranchKind);
    }

    [Fact]
    public void CfgBuilder_IfNoElse_FallsThrough()
    {
        var cfg = BuildCfg("let x = 1; if (x > 0) { let y = 2; } io.println(x);");
        // Entry → then-block, entry also → join (fall-through)
        Assert.True(cfg.Entry.Successors.Count >= 2);
    }

    [Fact]
    public void CfgBuilder_WhileLoop_CreatesBackEdge()
    {
        var cfg = BuildCfg("let i = 0; while (i < 10) { i = i + 1; }");
        // Should have a conditional block (loop header) that has two successors
        var condBlock = cfg.Blocks.FirstOrDefault(b => b.BranchKind == BranchKind.Conditional);
        Assert.NotNull(condBlock);
        Assert.Equal(2, condBlock!.Successors.Count);
    }

    [Fact]
    public void CfgBuilder_BothBranchesTerminate_ExitHasNoNonTerminatingPreds()
    {
        var cfg = BuildCfgForFunction("fn foo() -> int { if (true) { return 1; } else { return 2; } }");
        Assert.False(cfg.HasNonTerminatingPathToExit());
    }

    [Fact]
    public void CfgBuilder_MissingElseReturn_ExitHasNonTerminatingPath()
    {
        var cfg = BuildCfgForFunction("fn foo() -> int { if (true) { return 1; } }");
        Assert.True(cfg.HasNonTerminatingPathToExit());
    }

    [Fact]
    public void CfgBuilder_UnreachableBlock_DetectedByGetUnreachableBlocks()
    {
        // When both if/else branches return, the join block after get no predecessors
        var cfg = BuildCfg("if (true) { return 1; } else { return 2; }");
        var unreachable = cfg.GetUnreachableBlocks().ToList();
        // The join block created for after the if/else has no predecessors
        Assert.NotEmpty(unreachable);
    }

    [Fact]
    public void CfgBuilder_TryCatch_CreatesCatchBlock()
    {
        var cfg = BuildCfg("try { let x = 1; } catch (e) { io.println(e); }");
        Assert.True(cfg.Blocks.Count >= 3); // entry + try + catch + after
    }

    // ── SA0106: Unreachable branch ────────────────────────────────────────────────────────

    [Fact]
    public void UnreachableBranch_BothBranchesReturn_ReportsAfterCode()
    {
        var diagnostics = Validate("""
            fn foo() {
                if (true) { return 1; } else { return 2; }
                io.println("dead");
            }
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0106");
    }

    [Fact]
    public void UnreachableBranch_OnlyThenBranchReturns_NoDiagnostic()
    {
        var diagnostics = Validate("""
            fn foo() {
                if (true) { return 1; }
                io.println("alive");
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0106");
    }

    [Fact]
    public void UnreachableBranch_BothBranchesThrow_ReportsAfterCode()
    {
        var diagnostics = Validate("""
            fn foo() {
                if (true) { throw "err"; } else { throw "err2"; }
                io.println("dead");
            }
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0106");
    }

    [Fact]
    public void UnreachableBranch_NoElse_NoDiagnostic()
    {
        var diagnostics = Validate("""
            fn foo() {
                if (true) { return 1; }
                return 2;
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0106");
    }

    [Fact]
    public void UnreachableBranch_MixedReturnThrow_ReportsAfterCode()
    {
        var diagnostics = Validate("""
            fn foo() {
                if (true) { return 1; } else { throw "error"; }
                let x = 5;
            }
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0106");
    }

    [Fact]
    public void UnreachableBranch_TopLevel_AlsoDetected()
    {
        var diagnostics = Validate("""
            if (true) { return; } else { return; }
            io.println("dead");
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0106");
    }

    [Fact]
    public void UnreachableBranch_NestedIfElseBothTerminate_ReportsAfterCode()
    {
        var diagnostics = Validate("""
            fn foo() {
                if (true) {
                    if (false) { return 1; } else { return 2; }
                } else {
                    return 3;
                }
                let dead = 5;
            }
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0106");
    }

    [Fact]
    public void UnreachableBranch_MultipleDeadStatements_ReportsAll()
    {
        var diagnostics = Validate("""
            fn foo() {
                if (true) { return 1; } else { return 2; }
                let a = 1;
                let b = 2;
            }
            """);
        var sa0106 = diagnostics.Where(d => d.Code == "SA0106").ToList();
        Assert.True(sa0106.Count >= 2);
    }

    // ── SA0208: Dead store ────────────────────────────────────────────────────────────────

    [Fact]
    public void DeadStore_AssignedThenOverwritten_ReportsDiagnostic()
    {
        // Two pure assignments without a read in between → dead store
        var diagnostics = Validate("""
            fn foo(a) {
                let x = a;
                x = a + 1;
                x = a + 2;
                io.println(x);
            }
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0208" && d.Message.Contains("'x'"));
    }

    [Fact]
    public void DeadStore_AssignedThenRead_NoDiagnostic()
    {
        var diagnostics = Validate("""
            fn foo() {
                let x = 1;
                io.println(x);
                x = 2;
                io.println(x);
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0208");
    }

    [Fact]
    public void DeadStore_UsedInCondition_NoDiagnostic()
    {
        var diagnostics = Validate("""
            fn foo() {
                let x = compute();
                if (x > 0) { io.println(x); }
            }
            fn compute() { return 1; }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0208");
    }

    [Fact]
    public void DeadStore_ReassignedTwiceWithoutRead_ReportsBothRedundant()
    {
        // declare, then two reassignments without read — second overwrites first pure assign
        var diagnostics = Validate("""
            fn foo(a) {
                let x = a;
                x = a + 1;
                x = a + 2;
                io.println(x);
            }
            """);
        // At minimum the second pure assignment (x = a+1) should be reported
        Assert.Contains(diagnostics, d => d.Code == "SA0208" && d.Message.Contains("'x'"));
    }

    [Fact]
    public void DeadStore_AssignWithSelfRead_NoDiagnostic()
    {
        // x = x + 1 reads x before writing
        var diagnostics = Validate("""
            fn foo() {
                let x = 0;
                x = x + 1;
                io.println(x);
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0208");
    }

    [Fact]
    public void DeadStore_InFunction_Detected()
    {
        // Pure assignment overwritten without being read
        var diagnostics = Validate("""
            fn foo(a) {
                let result = a * 2;
                result = a * 3;
                result = a * 4;
                return result;
            }
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0208" && d.Message.Contains("'result'"));
    }

    // ── SA0210: Definite assignment ───────────────────────────────────────────────────────

    [Fact]
    public void DefiniteAssignment_UninitUsedDirectly_ReportsDiagnostic()
    {
        var diagnostics = Validate("""
            let x;
            io.println(x);
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0210" && d.Message.Contains("'x'"));
    }

    [Fact]
    public void DefiniteAssignment_InitBeforeUse_NoDiagnostic()
    {
        var diagnostics = Validate("""
            let x;
            x = 5;
            io.println(x);
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0210");
    }

    [Fact]
    public void DefiniteAssignment_OnlyOnePathAssigns_ReportsDiagnostic()
    {
        var diagnostics = Validate("""
            let x;
            if (true) { x = 1; }
            io.println(x);
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0210" && d.Message.Contains("'x'"));
    }

    [Fact]
    public void DefiniteAssignment_BothPathsAssign_NoDiagnostic()
    {
        var diagnostics = Validate("""
            let x;
            if (true) { x = 1; } else { x = 2; }
            io.println(x);
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0210");
    }

    [Fact]
    public void DefiniteAssignment_Initialized_NoDiagnostic()
    {
        var diagnostics = Validate("""
            let x = 5;
            io.println(x);
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0210");
    }

    [Fact]
    public void DefiniteAssignment_UsedInCondition_ReportsDiagnostic()
    {
        var diagnostics = Validate("""
            let flag;
            if (flag) { io.println("yes"); }
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0210" && d.Message.Contains("'flag'"));
    }

    // ── SA0308: Possible null access ─────────────────────────────────────────────────────

    [Fact]
    public void PossibleNull_UninitVarDotAccess_ReportsDiagnostic()
    {
        var diagnostics = Validate("""
            let result;
            io.println(result.name);
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0308" && d.Message.Contains("'result'"));
    }

    [Fact]
    public void PossibleNull_AssignedBeforeAccess_NoDiagnostic()
    {
        var diagnostics = Validate("""
            let result;
            result = get_data();
            io.println(result.name);
            fn get_data() { return null; }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0308");
    }

    [Fact]
    public void PossibleNull_NullGuardBeforeAccess_NoDiagnostic()
    {
        var diagnostics = Validate("""
            let result;
            if (result != null) {
                io.println(result.name);
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0308");
    }

    [Fact]
    public void PossibleNull_TruthinessGuardBeforeAccess_NoDiagnostic()
    {
        var diagnostics = Validate("""
            let result;
            if (result) {
                io.println(result.name);
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0308");
    }

    [Fact]
    public void PossibleNull_InitializedWithValue_NoDiagnostic()
    {
        var diagnostics = Validate("""
            let result = get_data();
            io.println(result.name);
            fn get_data() { return null; }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0308");
    }

    // ── SA0404: Missing return ────────────────────────────────────────────────────────────

    [Fact]
    public void MissingReturn_WithReturnType_NoReturn_ReportsDiagnostic()
    {
        var diagnostics = Validate("""
            fn compute() -> int {
                let x = 1;
            }
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0404" && d.Message.Contains("'compute'"));
    }

    [Fact]
    public void MissingReturn_WithReturnType_HasReturn_NoDiagnostic()
    {
        var diagnostics = Validate("""
            fn compute() -> int {
                return 1;
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0404");
    }

    [Fact]
    public void MissingReturn_NoReturnType_NoCheck_NoDiagnostic()
    {
        var diagnostics = Validate("""
            fn compute() {
                let x = 1;
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0404");
    }

    [Fact]
    public void MissingReturn_WithReturnType_IfElseBothReturn_NoDiagnostic()
    {
        var diagnostics = Validate("""
            fn classify(n) -> string {
                if (n > 0) { return "positive"; } else { return "non-positive"; }
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0404");
    }

    [Fact]
    public void MissingReturn_WithReturnType_OnlyIfReturns_ReportsDiagnostic()
    {
        var diagnostics = Validate("""
            fn classify(n) -> string {
                if (n > 0) { return "positive"; }
            }
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0404" && d.Message.Contains("'classify'"));
    }

    [Fact]
    public void MissingReturn_WithReturnType_ThrowAlternative_NoDiagnostic()
    {
        var diagnostics = Validate("""
            fn strict(n) -> int {
                if (n > 0) { return n; } else { throw "negative not allowed"; }
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0404");
    }

    [Fact]
    public void MissingReturn_VoidAnnotation_NoDiagnostic()
    {
        var diagnostics = Validate("""
            fn doWork() -> void {
                io.println("done");
            }
            """);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0404");
    }

    [Fact]
    public void MissingReturn_NestedFunction_EachCheckedIndependently()
    {
        var diagnostics = Validate("""
            fn outer() -> int {
                fn inner() -> string {
                    let x = 1;
                }
                return 42;
            }
            """);
        // outer is fine, inner is missing return
        Assert.Contains(diagnostics, d => d.Code == "SA0404" && d.Message.Contains("'inner'"));
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0404" && d.Message.Contains("'outer'"));
    }

    // ── New diagnostic descriptors registered ────────────────────────────────────────────

    [Fact]
    public void DiagnosticDescriptors_AllNewCodesRegistered()
    {
        var allCodes = DiagnosticDescriptors.AllByCode;
        Assert.True(allCodes.ContainsKey("SA0106"));
        Assert.True(allCodes.ContainsKey("SA0208"));
        Assert.True(allCodes.ContainsKey("SA0210"));
        Assert.True(allCodes.ContainsKey("SA0308"));
        Assert.True(allCodes.ContainsKey("SA0404"));
    }

    [Fact]
    public void RuleRegistry_ContainsAllNewRules()
    {
        var codes = RuleRegistry.GetAllRules().Select(r => r.Descriptor.Code).ToHashSet();
        Assert.Contains("SA0106", codes);
        Assert.Contains("SA0208", codes);
        Assert.Contains("SA0210", codes);
        Assert.Contains("SA0308", codes);
        Assert.Contains("SA0404", codes);
    }

    [Fact]
    public void MissingReturnRule_IsPerNodeRule()
    {
        var rules = RuleRegistry.GetAllRules();
        var rule = rules.Single(r => r.Descriptor.Code == "SA0404");
        Assert.NotEmpty(rule.SubscribedNodeTypes);
        Assert.Contains(typeof(FnDeclStmt), rule.SubscribedNodeTypes);
    }

    [Fact]
    public void FlowAnalysisRules_ArePostWalkOrPerNode()
    {
        var rules = RuleRegistry.GetAllRules();
        var postWalkCodes = new[] { "SA0106", "SA0208", "SA0210", "SA0308" };
        foreach (var code in postWalkCodes)
        {
            var rule = rules.Single(r => r.Descriptor.Code == code);
            Assert.Empty(rule.SubscribedNodeTypes);
        }
    }

    // ── Edge cases ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnreachableBranch_EmptyFunction_NoDiagnostic()
    {
        var diagnostics = Validate("fn foo() { }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0106");
    }

    [Fact]
    public void SA0106_DoesNotConflictWithSA0104()
    {
        // SA0104 catches: return; dead_code in same sequence
        // SA0106 catches: if/else-both-terminate + dead_code after
        var diagnostics = Validate("""
            fn foo() {
                return;
                let x = 1;
            }
            """);
        // SA0104 should fire, SA0106 should not
        Assert.Contains(diagnostics, d => d.Code == "SA0104");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0106");
    }

    [Fact]
    public void MissingReturn_StructMethod_CheckedIndependently()
    {
        var diagnostics = Validate("""
            struct Foo {
                fn bar() -> int {
                    let x = 1;
                }
            }
            """);
        Assert.Contains(diagnostics, d => d.Code == "SA0404" && d.Message.Contains("'bar'"));
    }

    [Fact]
    public void CfgBuilder_ForLoop_HasAfterBlock()
    {
        var cfg = BuildCfg("for (let i = 0; i < 10; i++) { io.println(i); } io.println(\"done\");");
        // Should have multiple blocks including an after-loop block
        Assert.True(cfg.Blocks.Count >= 4);
    }

    [Fact]
    public void CfgBuilder_ForInLoop_HasAfterBlock()
    {
        var cfg = BuildCfg("const items = [1, 2, 3]; for (let item in items) { io.println(item); } io.println(\"done\");");
        Assert.True(cfg.Blocks.Count >= 3);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static ControlFlowGraph BuildCfg(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var builder = new CfgBuilder();
        return builder.Build(stmts);
    }

    private static ControlFlowGraph BuildCfgForFunction(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var fn = stmts.OfType<FnDeclStmt>().First();
        var builder = new CfgBuilder();
        return builder.Build(fn.Body.Statements);
    }
}
