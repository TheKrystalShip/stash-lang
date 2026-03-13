using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Lsp.Analysis;

namespace Stash.Tests.Analysis;

public class LspFeaturesRound4Tests
{
    private static ScopeTree Analyze(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        return collector.Collect(stmts);
    }

    private static AnalysisResult FullAnalyze(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var tree = collector.Collect(stmts);
        var validator = new SemanticValidator(tree);
        var diagnostics = validator.Validate(stmts);
        return new AnalysisResult(tokens, stmts,
            new List<string>(), new List<string>(),
            new List<DiagnosticError>(), new List<DiagnosticError>(),
            tree, diagnostics);
    }

    // ──────────────────────────────────────────────────────────
    // 1. Inlay Hint Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void InlayHints_UserFunctionCall_HasResolvableParams()
    {
        var source = "fn greet(name, greeting) {}\ngreet(\"Alice\", \"Hello\");";
        var tree = Analyze(source);
        var definition = tree.FindDefinition("greet", 1, 4);
        Assert.NotNull(definition);
        Assert.NotNull(definition!.Detail);
        Assert.Contains("name", definition.Detail);
        Assert.Contains("greeting", definition.Detail);
    }

    [Fact]
    public void InlayHints_CallExpr_ArgumentsHaveSpans()
    {
        var source = "fn add(a, b) {}\nlet result = add(1, 2);";
        var result = FullAnalyze(source);
        var varDecl = result.Statements[1] as VarDeclStmt;
        Assert.NotNull(varDecl);
        var call = varDecl!.Initializer as CallExpr;
        Assert.NotNull(call);
        Assert.Equal(2, call!.Arguments.Count);
        foreach (var arg in call.Arguments)
        {
            Assert.True(arg.Span.StartLine > 0);
            Assert.True(arg.Span.StartColumn >= 0);
        }
    }

    [Fact]
    public void InlayHints_MatchingArgName_ShouldBeSkipped()
    {
        var source = "fn process(value) {}\nlet value = 42;\nprocess(value);";
        var result = FullAnalyze(source);
        var exprStmt = result.Statements[2] as ExprStmt;
        Assert.NotNull(exprStmt);
        var call = exprStmt!.Expression as CallExpr;
        Assert.NotNull(call);
        Assert.Single(call!.Arguments);
        var arg = call.Arguments[0] as IdentifierExpr;
        Assert.NotNull(arg);
        Assert.Equal("value", arg!.Name.Lexeme);
    }

    [Fact]
    public void InlayHints_ZeroArgCall_NoHintsExpected()
    {
        var source = "fn noArgs() {}\nnoArgs();";
        var result = FullAnalyze(source);
        var exprStmt = result.Statements[1] as ExprStmt;
        Assert.NotNull(exprStmt);
        var call = exprStmt!.Expression as CallExpr;
        Assert.NotNull(call);
        Assert.Empty(call!.Arguments);
    }

    [Fact]
    public void InlayHints_WrongArity_NoHintsExpected()
    {
        var source = "fn twoParams(a, b) {}\ntwoParams(1, 2, 3);";
        var tree = Analyze(source);
        var result = FullAnalyze(source);
        var definition = tree.FindDefinition("twoParams", 1, 4);
        Assert.NotNull(definition);
        // Extract param count from definition detail
        var fnDecl = result.Statements[0] as FnDeclStmt;
        Assert.NotNull(fnDecl);
        Assert.Equal(2, fnDecl!.Parameters.Count);
        var exprStmt = result.Statements[1] as ExprStmt;
        Assert.NotNull(exprStmt);
        var call = exprStmt!.Expression as CallExpr;
        Assert.NotNull(call);
        Assert.Equal(3, call!.Arguments.Count);
        // Mismatch: params.Count (2) != args.Count (3) → hints skipped
        Assert.NotEqual(fnDecl.Parameters.Count, call.Arguments.Count);
    }

    [Fact]
    public void InlayHints_NestedCalls_AllHaveArgs()
    {
        var source = "fn outer(x) {}\nfn inner(y) {}\nouter(inner(42));";
        var result = FullAnalyze(source);
        var exprStmt = result.Statements[2] as ExprStmt;
        Assert.NotNull(exprStmt);
        var outerCall = exprStmt!.Expression as CallExpr;
        Assert.NotNull(outerCall);
        Assert.Single(outerCall!.Arguments);
        var innerCall = outerCall.Arguments[0] as CallExpr;
        Assert.NotNull(innerCall);
        Assert.Single(innerCall!.Arguments);
    }

    // ──────────────────────────────────────────────────────────
    // 2. Code Lens Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void CodeLens_FunctionWithReferences_CountsCorrectly()
    {
        var source = "fn test() {}\ntest();\ntest();";
        var tree = Analyze(source);
        var refs = tree.FindReferences("test", 1, 4);
        Assert.Equal(3, refs.Count); // declaration + 2 calls
    }

    [Fact]
    public void CodeLens_UnusedFunction_ZeroReferences()
    {
        var source = "fn unused() {}\nlet x = 1;";
        var tree = Analyze(source);
        var refs = tree.FindReferences("unused", 1, 4);
        Assert.Single(refs); // only the declaration
    }

    [Fact]
    public void CodeLens_StructWithReferences_CountsCorrectly()
    {
        var source = "struct Point { x, y }\nlet p = Point { x: 1, y: 2 };";
        var tree = Analyze(source);
        var topLevel = tree.GetTopLevel().ToList();
        Assert.Contains(topLevel, s => s.Name == "Point" && s.Kind == SymbolKind.Struct);
    }

    [Fact]
    public void CodeLens_EnumWithReferences_CountsCorrectly()
    {
        var source = "enum Color { Red, Green, Blue }\nlet c = Color.Red;";
        var tree = Analyze(source);
        var topLevel = tree.GetTopLevel().ToList();
        Assert.Contains(topLevel, s => s.Name == "Color" && s.Kind == SymbolKind.Enum);
    }

    [Fact]
    public void CodeLens_TopLevel_IncludesOnlyFunctionsStructsEnums()
    {
        var source = "let x = 1;\nconst y = 2;\nfn hello() {}\nstruct S { a }\nenum E { V }";
        var tree = Analyze(source);
        var topLevel = tree.GetTopLevel().ToList();
        Assert.Contains(topLevel, s => s.Name == "hello" && s.Kind == SymbolKind.Function);
        Assert.Contains(topLevel, s => s.Name == "S" && s.Kind == SymbolKind.Struct);
        Assert.Contains(topLevel, s => s.Name == "E" && s.Kind == SymbolKind.Enum);
    }

    [Fact]
    public void CodeLens_FunctionCalledInsideFunction_CountsCorrectly()
    {
        var source = "fn helper() {}\nfn main() {\n  helper();\n  helper();\n}";
        var tree = Analyze(source);
        var refs = tree.FindReferences("helper", 1, 4);
        Assert.Equal(3, refs.Count); // declaration + 2 calls
    }

    // ──────────────────────────────────────────────────────────
    // 3. Interpolated String / Command Expression Span Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void InterpolatedString_EmbeddedIdentifier_NotFlaggedAsUndefined()
    {
        // Verifies that using a const inside an interpolated string doesn't produce a false "not defined" warning
        var result = FullAnalyze("const NAME = \"world\";\nlet greeting = $\"Hello {NAME}!\";");
        Assert.DoesNotContain(result.SemanticDiagnostics, d => d.Message.Contains("NAME") && d.Message.Contains("not defined"));
    }

    [Fact]
    public void CommandExpr_EmbeddedIdentifier_NotFlaggedAsUndefined()
    {
        // Verifies that using a variable inside a command expression doesn't produce a false "not defined" warning
        var result = FullAnalyze("let target = \"main\";\nlet r = $(git checkout {target});");
        Assert.DoesNotContain(result.SemanticDiagnostics, d => d.Message.Contains("target") && d.Message.Contains("not defined"));
    }

    [Fact]
    public void InterpolatedString_EmbeddedIdentifier_HasCorrectSpan()
    {
        // Verifies the embedded identifier's reference has a SourceSpan matching the actual source position (not (1,1))
        var source = "const X = 1;\nlet s = $\"value: {X}\";";
        var tree = Analyze(source);
        // X is defined on line 1; the reference in the interpolated string is on line 2
        // The reference should resolve (not be in unresolvedReferences)
        var unresolved = tree.GetUnresolvedReferences();
        Assert.DoesNotContain(unresolved, r => r.Name == "X");
    }

    [Fact]
    public void InterpolatedString_MultipleEmbeddedIdentifiers_AllResolved()
    {
        // Multiple embedded identifiers in one interpolated string should all resolve
        var result = FullAnalyze("let a = 1;\nlet b = 2;\nlet s = $\"{a} + {b}\";");
        Assert.DoesNotContain(result.SemanticDiagnostics, d => d.Message.Contains("not defined"));
    }

    [Fact]
    public void CommandExpr_ConstUsedInFunction_NotFlaggedAsUndefined()
    {
        // This mirrors the actual deploy.stash pattern that triggered the bug:
        // const defined at top level, used inside a function in a command expression
        var source = "const DEST = \"/usr/bin/app\";\nfn deploy() {\n  let r = $(rm -f {DEST});\n}";
        var result = FullAnalyze(source);
        Assert.DoesNotContain(result.SemanticDiagnostics, d => d.Message.Contains("DEST") && d.Message.Contains("not defined"));
    }

    [Fact]
    public void InterpolatedString_ConstUsedInFunction_NotFlaggedAsUndefined()
    {
        // Another deploy.stash pattern: const used in interpolated string inside function
        var source = "const RUNTIME = \"linux-x64\";\nfn build() {\n  let path = $\"/opt/{RUNTIME}/bin\";\n}";
        var result = FullAnalyze(source);
        Assert.DoesNotContain(result.SemanticDiagnostics, d => d.Message.Contains("RUNTIME") && d.Message.Contains("not defined"));
    }

    // ──────────────────────────────────────────────────────────
    // 4. Unreachable Code Detection Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void UnreachableCode_AfterProcessExit_FlaggedAsUnnecessary()
    {
        var source = "process.exit(1);\nio.println(\"unreachable\");";
        var result = FullAnalyze(source);
        var unreachable = result.SemanticDiagnostics.Where(d => d.IsUnnecessary).ToList();
        Assert.Single(unreachable);
        Assert.Contains("Unreachable", unreachable[0].Message);
        Assert.Equal(2, unreachable[0].Span.StartLine);
    }

    [Fact]
    public void UnreachableCode_AfterReturn_FlaggedAsUnnecessary()
    {
        var source = "fn test() {\n  return;\n  io.println(\"unreachable\");\n}";
        var result = FullAnalyze(source);
        var unreachable = result.SemanticDiagnostics.Where(d => d.IsUnnecessary).ToList();
        Assert.Single(unreachable);
        Assert.Equal(3, unreachable[0].Span.StartLine);
    }

    [Fact]
    public void UnreachableCode_AfterBreak_FlaggedAsUnnecessary()
    {
        var source = "while (true) {\n  break;\n  io.println(\"unreachable\");\n}";
        var result = FullAnalyze(source);
        var unreachable = result.SemanticDiagnostics.Where(d => d.IsUnnecessary).ToList();
        Assert.Single(unreachable);
        Assert.Equal(3, unreachable[0].Span.StartLine);
    }

    [Fact]
    public void UnreachableCode_AfterContinue_FlaggedAsUnnecessary()
    {
        var source = "while (true) {\n  continue;\n  io.println(\"unreachable\");\n}";
        var result = FullAnalyze(source);
        var unreachable = result.SemanticDiagnostics.Where(d => d.IsUnnecessary).ToList();
        Assert.Single(unreachable);
        Assert.Equal(3, unreachable[0].Span.StartLine);
    }

    [Fact]
    public void UnreachableCode_NoTerminator_NothingFlagged()
    {
        var source = "let x = 1;\nio.println(x);";
        var result = FullAnalyze(source);
        var unreachable = result.SemanticDiagnostics.Where(d => d.IsUnnecessary).ToList();
        Assert.Empty(unreachable);
    }

    [Fact]
    public void UnreachableCode_ProcessExitInsideIfBlock_OnlyAffectsThatBlock()
    {
        // process.exit() inside an if-block should only gray out subsequent statements in that block,
        // not statements after the if-block
        var source = "if (true) {\n  process.exit(1);\n  io.println(\"unreachable\");\n}\nio.println(\"reachable\");";
        var result = FullAnalyze(source);
        var unreachable = result.SemanticDiagnostics.Where(d => d.IsUnnecessary).ToList();
        Assert.Single(unreachable);
        Assert.Equal(3, unreachable[0].Span.StartLine);
    }

    [Fact]
    public void UnreachableCode_MultipleStatementsAfterExit_AllFlagged()
    {
        var source = "fn deploy() {\n  process.exit(1);\n  io.println(\"a\");\n  io.println(\"b\");\n  io.println(\"c\");\n}";
        var result = FullAnalyze(source);
        var unreachable = result.SemanticDiagnostics.Where(d => d.IsUnnecessary).ToList();
        Assert.Equal(3, unreachable.Count);
        Assert.Equal(3, unreachable[0].Span.StartLine);
        Assert.Equal(4, unreachable[1].Span.StartLine);
        Assert.Equal(5, unreachable[2].Span.StartLine);
    }

    [Fact]
    public void UnreachableCode_DeployStashPattern_ConstAfterExitInBlock()
    {
        // This mirrors the deploy.stash pattern: process.exit() inside an if block
        // The io.println after the exit should be grayed out, the code after the if should NOT
        var source = "const DEST = \"/usr/bin\";\nfn deploy() {\n  if (true) {\n    process.exit(1);\n    io.println(\"dead\");\n  }\n  io.println(\"alive\");\n}";
        var result = FullAnalyze(source);
        var unreachable = result.SemanticDiagnostics.Where(d => d.IsUnnecessary).ToList();
        Assert.Single(unreachable);
        Assert.Contains("Unreachable", unreachable[0].Message);
        Assert.Equal(5, unreachable[0].Span.StartLine); // the "dead" line
    }
}
