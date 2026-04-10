using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Analysis.Rules;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for the Phase 3 Rule Architecture: IAnalysisRule, RuleContext, RuleRegistry, and the
/// refactored SemanticValidator thin dispatcher.
/// </summary>
public class RuleArchitectureTests : AnalysisTestBase
{
    // ── RuleRegistry ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RuleRegistry_GetAllRules_ReturnsNonEmptyList()
    {
        var rules = RuleRegistry.GetAllRules();
        Assert.NotEmpty(rules);
    }

    [Fact]
    public void RuleRegistry_GetAllRules_ContainsAllExpectedCodes()
    {
        var rules = RuleRegistry.GetAllRules();
        var codes = rules.Select(r => r.Descriptor.Code).ToHashSet();

        string[] expectedCodes =
        [
            "SA0101", "SA0102", "SA0103", "SA0104", "SA0105",
            "SA0201", "SA0202", "SA0203", "SA0205", "SA0206", "SA0207",
            "SA0301", "SA0302", "SA0303", "SA0304", "SA0305",
            "SA0401", "SA0402", "SA0403",
            "SA0501",
            "SA0701", "SA0702",
            "SA0802",
        ];

        foreach (var code in expectedCodes)
        {
            Assert.Contains(code, codes);
        }
    }

    [Fact]
    public void RuleRegistry_GetAllRules_NoDuplicateCodes()
    {
        var rules = RuleRegistry.GetAllRules();
        var codes = rules.Select(r => r.Descriptor.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void RuleRegistry_GetAllRules_EachRuleHasNonNullDescriptor()
    {
        var rules = RuleRegistry.GetAllRules();
        foreach (var rule in rules)
        {
            Assert.NotNull(rule.Descriptor);
            Assert.False(string.IsNullOrEmpty(rule.Descriptor.Code));
            Assert.False(string.IsNullOrEmpty(rule.Descriptor.Title));
        }
    }

    [Fact]
    public void RuleRegistry_GetAllRules_EachRuleHasNonNullSubscribedTypes()
    {
        var rules = RuleRegistry.GetAllRules();
        foreach (var rule in rules)
        {
            Assert.NotNull(rule.SubscribedNodeTypes);
        }
    }

    // ── Rule categorization ───────────────────────────────────────────────────────────────

    [Fact]
    public void RuleRegistry_PostWalkRules_HaveEmptySubscribedTypes()
    {
        var rules = RuleRegistry.GetAllRules();
        var postWalkCodes = new HashSet<string> { "SA0201", "SA0202", "SA0205", "SA0206", "SA0207", "SA0802" };

        foreach (var rule in rules)
        {
            if (postWalkCodes.Contains(rule.Descriptor.Code))
            {
                Assert.Empty(rule.SubscribedNodeTypes);
            }
        }
    }

    [Fact]
    public void RuleRegistry_PerNodeRules_HaveNonEmptySubscribedTypes()
    {
        var rules = RuleRegistry.GetAllRules();
        var perNodeCodes = new HashSet<string> { "SA0101", "SA0102", "SA0103", "SA0105", "SA0203", "SA0303", "SA0304", "SA0305" };

        foreach (var rule in rules)
        {
            if (perNodeCodes.Contains(rule.Descriptor.Code))
            {
                Assert.NotEmpty(rule.SubscribedNodeTypes);
            }
        }
    }

    [Fact]
    public void UnreachableCodeRule_HasEmptySubscribedTypes()
    {
        var rules = RuleRegistry.GetAllRules();
        var unreachableRule = rules.Single(r => r.Descriptor.Code == "SA0104");
        Assert.Empty(unreachableRule.SubscribedNodeTypes);
    }

    // ── Rule filtering via constructor ────────────────────────────────────────────────────

    [Fact]
    public void SemanticValidator_WithNoRules_ProducesNoDiagnostics()
    {
        var lexer = new Lexer("break;", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);

        var validator = new SemanticValidator(scopeTree, new List<IAnalysisRule>());
        var diagnostics = validator.Validate(stmts);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void SemanticValidator_WithOnlySA0101_ProducesOnlyBreakDiagnostic()
    {
        var lexer = new Lexer("break; return 1;", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);

        var rules = new List<IAnalysisRule> { new BreakOutsideLoopRule() };
        var validator = new SemanticValidator(scopeTree, rules);
        var diagnostics = validator.Validate(stmts);

        Assert.Single(diagnostics);
        Assert.Equal("SA0101", diagnostics[0].Code);
    }

    [Fact]
    public void SemanticValidator_DisablingSA0101_SuppressesBreakDiagnostic()
    {
        // Load all rules except SA0101
        var rules = RuleRegistry.GetAllRules()
            .Where(r => r.Descriptor.Code != "SA0101")
            .ToList();

        var lexer = new Lexer("break;", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);

        var validator = new SemanticValidator(scopeTree, rules);
        var diagnostics = validator.Validate(stmts);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void SemanticValidator_DisablingSA0201_SuppressesUnusedDeclaration()
    {
        var rules = RuleRegistry.GetAllRules()
            .Where(r => r.Descriptor.Code != "SA0201" && r.Descriptor.Code != "SA0205")
            .ToList();

        var lexer = new Lexer("let x = 1;", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);

        var validator = new SemanticValidator(scopeTree, rules);
        var diagnostics = validator.Validate(stmts);

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0201");
    }

    // ── Regression: rule system produces identical diagnostics ────────────────────────────

    [Fact]
    public void RuleSystem_BreakOutsideLoop_MatchesExpectedDiagnostic()
    {
        var diagnostics = Validate("break;");

        var d = Assert.Single(diagnostics);
        Assert.Equal("SA0101", d.Code);
        Assert.Equal(DiagnosticLevel.Error, d.Level);
    }

    [Fact]
    public void RuleSystem_ContinueOutsideLoop_MatchesExpectedDiagnostic()
    {
        var diagnostics = Validate("continue;");

        var d = Assert.Single(diagnostics);
        Assert.Equal("SA0102", d.Code);
        Assert.Equal(DiagnosticLevel.Error, d.Level);
    }

    [Fact]
    public void RuleSystem_ReturnOutsideFunction_MatchesExpectedDiagnostic()
    {
        var diagnostics = Validate("return 1;");

        var d = Assert.Single(diagnostics);
        Assert.Equal("SA0103", d.Code);
        Assert.Equal(DiagnosticLevel.Error, d.Level);
    }

    [Fact]
    public void RuleSystem_UnreachableCode_MatchesExpectedDiagnostic()
    {
        var diagnostics = Validate("fn foo() { return 1; let x = 2; }");

        Assert.Contains(diagnostics, d => d.Code == "SA0104");
    }

    [Fact]
    public void RuleSystem_EmptyBlock_MatchesExpectedDiagnostic()
    {
        var diagnostics = Validate("while (true) {}");

        Assert.Contains(diagnostics, d => d.Code == "SA0105");
    }

    [Fact]
    public void RuleSystem_ConstantReassignment_MatchesExpectedDiagnostic()
    {
        var diagnostics = Validate("const X = 1; X = 2;");

        Assert.Contains(diagnostics, d => d.Code == "SA0203" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void RuleSystem_LetCouldBeConst_MatchesExpectedDiagnostic()
    {
        var diagnostics = Validate("let x = 1;");

        Assert.Contains(diagnostics, d => d.Code == "SA0205");
    }

    [Fact]
    public void RuleSystem_UnusedDeclaration_MatchesExpectedDiagnostic()
    {
        // Use const to avoid the SA0205 hint mixing in
        var diagnostics = Validate("const x = 1;");

        Assert.Contains(diagnostics, d => d.Code == "SA0201");
    }

    [Fact]
    public void RuleSystem_ArityMismatch_MatchesExpectedDiagnostic()
    {
        var diagnostics = Validate("fn foo(a, b) {} foo(1);");

        Assert.Contains(diagnostics, d => d.Code == "SA0401" && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void RuleSystem_ShadowVariable_MatchesExpectedDiagnostic()
    {
        var diagnostics = Validate("let x = 1; fn foo() { let x = 2; }");

        Assert.Contains(diagnostics, d => d.Code == "SA0207");
    }

    // ── IAnalysisRule interface contract ──────────────────────────────────────────────────

    [Fact]
    public void IAnalysisRule_AllRulesImplementInterface()
    {
        var rules = RuleRegistry.GetAllRules();
        foreach (var rule in rules)
        {
            Assert.IsAssignableFrom<IAnalysisRule>(rule);
        }
    }

    [Fact]
    public void RuleContext_Properties_ArePopulatedDuringDispatch()
    {
        // Verify that a custom rule receives a properly populated RuleContext
        var receivedContext = new List<RuleContext>();
        var probeRule = new ProbeRule(typeof(Stash.Parsing.AST.BreakStmt), ctx => receivedContext.Add(ctx));

        var lexer = new Lexer("while (true) { break; }", "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);

        var validator = new SemanticValidator(scopeTree, [probeRule]);
        validator.Validate(stmts);

        var ctx = Assert.Single(receivedContext);
        Assert.NotNull(ctx.ScopeTree);
        Assert.NotNull(ctx.BuiltInNames);
        Assert.NotNull(ctx.ValidBuiltInTypes);
        Assert.IsType<Stash.Parsing.AST.BreakStmt>(ctx.Statement);
        Assert.Equal(1, ctx.LoopDepth); // inside while loop
        Assert.NotNull(ctx.ReportDiagnostic);
    }

    /// <summary>Helper rule that invokes a callback when dispatched, for testing context values.</summary>
    private sealed class ProbeRule : IAnalysisRule
    {
        private readonly Type _nodeType;
        private readonly Action<RuleContext> _callback;

        public ProbeRule(Type nodeType, Action<RuleContext> callback)
        {
            _nodeType = nodeType;
            _callback = callback;
        }

        public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0101;
        public IReadOnlySet<Type> SubscribedNodeTypes => new HashSet<Type> { _nodeType };
        public void Analyze(RuleContext context) => _callback(context);
    }
}
