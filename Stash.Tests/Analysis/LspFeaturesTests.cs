using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Lsp.Analysis;

namespace Stash.Tests.Analysis;

public class LspFeaturesTests
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
        return new AnalysisResult(tokens, stmts, new List<string>(), new List<string>(), new List<DiagnosticError>(), new List<DiagnosticError>(), tree, diagnostics);
    }

    // ──────────────────────────────────────────────────────────
    // 1. Rename Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Rename_FindsAllOccurrences_OfVariable()
    {
        // Line 1: let x = 1;
        // Line 2: let y = x + 2;   <- Read reference
        // Line 3: x = 5;           <- Write reference
        const string src = "let x = 1;\nlet y = x + 2;\nx = 5;";
        var tree = Analyze(src);

        // x is declared at line 1, col 5 (1-based: "let " = 4 chars, x at col 5)
        var refs = tree.FindReferences("x", 1, 5);

        // declaration (as Write) + 1 Read + 1 Write = 3
        Assert.Equal(3, refs.Count);
    }

    [Fact]
    public void Rename_FindsAllOccurrences_OfFunction()
    {
        // Line 1: fn greet(name) {
        // Line 2:   println(name);
        // Line 3: }
        // Line 4: greet("world");   <- Call reference
        const string src = "fn greet(name) {\n  println(name);\n}\ngreet(\"world\");";
        var tree = Analyze(src);

        // greet declared at line 1, col 4 (after "fn ")
        var refs = tree.FindReferences("greet", 1, 4);

        // declaration (as Write) + call on line 4 = 2
        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.Span.StartLine == 1); // declaration
        Assert.Contains(refs, r => r.Span.StartLine == 4); // call
    }

    [Fact]
    public void Rename_DoesNotFindShadowed()
    {
        // Line 1: let x = 1;
        // Line 2: fn test() {
        // Line 3:   let x = 2;   <- inner x declaration
        // Line 4:   x;           <- resolves to inner x
        // Line 5: }
        // Line 6: x;             <- resolves to outer x
        const string src = "let x = 1;\nfn test() {\n  let x = 2;\n  x;\n}\nx;";
        var tree = Analyze(src);

        // Outer x declared at line 1, col 5
        var outerRefs = tree.FindReferences("x", 1, 5);
        // Should NOT include references to the inner x (lines 3-4)
        Assert.DoesNotContain(outerRefs, r => r.Span.StartLine == 3);
        Assert.DoesNotContain(outerRefs, r => r.Span.StartLine == 4);

        // Inner x declared at line 3, col 7 (2 spaces + "let " + x)
        var innerRefs = tree.FindReferences("x", 3, 7);
        // Should NOT include references to the outer x (lines 1, 6)
        Assert.DoesNotContain(innerRefs, r => r.Span.StartLine == 1);
        Assert.DoesNotContain(innerRefs, r => r.Span.StartLine == 6);
    }

    // ──────────────────────────────────────────────────────────
    // 2. Folding Range Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void FoldingRange_FnDecl_HasMultiLineSpan()
    {
        // Line 1: fn test() {
        // Line 2:   let x = 1;
        // Line 3:   x;
        // Line 4: }
        const string src = "fn test() {\n  let x = 1;\n  x;\n}";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        var fnDecl = Assert.IsType<FnDeclStmt>(stmts[0]);
        Assert.Equal(1, fnDecl.Span.StartLine);
        Assert.Equal(4, fnDecl.Span.EndLine);
        Assert.True(fnDecl.Span.EndLine > fnDecl.Span.StartLine);
    }

    [Fact]
    public void FoldingRange_IfElse_HasSeparateSpans()
    {
        // Line 1: if (true) {
        // Line 2:   1;
        // Line 3: } else {
        // Line 4:   2;
        // Line 5: }
        const string src = "if (true) {\n  1;\n} else {\n  2;\n}";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        var ifStmt = Assert.IsType<IfStmt>(stmts[0]);
        var thenBranch = Assert.IsType<BlockStmt>(ifStmt.ThenBranch);
        var elseBranch = Assert.IsType<BlockStmt>(ifStmt.ElseBranch);

        Assert.True(thenBranch.Span.EndLine > thenBranch.Span.StartLine);
        Assert.True(elseBranch.Span.EndLine > elseBranch.Span.StartLine);
        Assert.True(elseBranch.Span.StartLine > thenBranch.Span.StartLine);
    }

    [Fact]
    public void FoldingRange_NestedBlocks_AllFoldable()
    {
        // Line 1: fn outer() {
        // Line 2:   while (true) {
        // Line 3:     if (true) {
        // Line 4:       1;
        // Line 5:     }
        // Line 6:   }
        // Line 7: }
        const string src = "fn outer() {\n  while (true) {\n    if (true) {\n      1;\n    }\n  }\n}";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();

        var fnDecl = Assert.IsType<FnDeclStmt>(stmts[0]);
        var whileStmt = Assert.IsType<WhileStmt>(fnDecl.Body.Statements[0]);
        var ifStmt = Assert.IsType<IfStmt>(whileStmt.Body.Statements[0]);

        Assert.True(fnDecl.Span.EndLine > fnDecl.Span.StartLine);
        Assert.True(whileStmt.Span.EndLine > whileStmt.Span.StartLine);
        Assert.True(ifStmt.Span.EndLine > ifStmt.Span.StartLine);
    }

    // ──────────────────────────────────────────────────────────
    // 3. Contextual Completion Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Completion_StructFieldsVisible_ForDotAccess()
    {
        const string src = "struct Point { x, y }\nlet p = Point { x: 1, y: 2 };";
        var tree = Analyze(src);

        var fields = tree.All.Where(s => s.Kind == SymbolKind.Field && s.ParentName == "Point").ToList();
        Assert.Contains(fields, f => f.Name == "x");
        Assert.Contains(fields, f => f.Name == "y");
    }

    [Fact]
    public void Completion_EnumMembersVisible_ForDotAccess()
    {
        const string src = "enum Color { Red, Green, Blue }";
        var tree = Analyze(src);

        var members = tree.All.Where(s => s.Kind == SymbolKind.EnumMember && s.ParentName == "Color").ToList();
        Assert.Contains(members, m => m.Name == "Red");
        Assert.Contains(members, m => m.Name == "Green");
        Assert.Contains(members, m => m.Name == "Blue");
    }

    [Fact]
    public void Completion_VisibleSymbols_ScopedCorrectly()
    {
        // Line 1: let a = 1;
        // Line 2: fn test() {
        // Line 3:   let b = 2;
        // Line 4:   b;
        // Line 5: }
        // Line 6: a;
        const string src = "let a = 1;\nfn test() {\n  let b = 2;\n  b;\n}\na;";
        var tree = Analyze(src);

        // At line 4 col 3 (inside function body, b already declared at line 3)
        var visibleInFn = tree.GetVisibleSymbols(4, 3).ToList();
        Assert.Contains(visibleInFn, s => s.Name == "a");
        Assert.Contains(visibleInFn, s => s.Name == "b");

        // At line 6 col 1 (global scope, after function; b is only in function scope)
        var visibleAtEnd = tree.GetVisibleSymbols(6, 1).ToList();
        Assert.Contains(visibleAtEnd, s => s.Name == "a");
        Assert.DoesNotContain(visibleAtEnd, s => s.Name == "b");
    }

    // ──────────────────────────────────────────────────────────
    // 4. Semantic Tokens Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void SemanticTokens_IdentifiesFunction()
    {
        const string src = "fn greet() {\n  1;\n}";
        var tree = Analyze(src);

        // "fn " = 3 chars, greet starts at col 4
        var sym = tree.FindDefinition("greet", 1, 4);
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Function, sym!.Kind);
    }

    [Fact]
    public void SemanticTokens_IdentifiesConstant()
    {
        const string src = "const PI = 3.14;";
        var tree = Analyze(src);

        // "const " = 6 chars, PI starts at col 7
        var sym = tree.FindDefinition("PI", 1, 7);
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Constant, sym!.Kind);
    }

    [Fact]
    public void SemanticTokens_IdentifiesParameter()
    {
        const string src = "fn add(a, b) {\n  a + b;\n}";
        var tree = Analyze(src);

        // Line 2: "  a + b;" — a is at col 3, inside function scope where parameters are visible
        var sym = tree.FindDefinition("a", 2, 3);
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Parameter, sym!.Kind);
    }

    [Fact]
    public void SemanticTokens_IdentifiesStruct()
    {
        const string src = "struct Point { x, y }";
        var tree = Analyze(src);

        // "struct " = 7 chars, Point starts at col 8
        var sym = tree.FindDefinition("Point", 1, 8);
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Struct, sym!.Kind);
    }

    [Fact]
    public void SemanticTokens_IdentifiesEnum()
    {
        const string src = "enum Color { Red, Green, Blue }";
        var tree = Analyze(src);

        // "enum " = 5 chars, Color starts at col 6
        var sym = tree.FindDefinition("Color", 1, 6);
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Enum, sym!.Kind);
    }

    [Fact]
    public void SemanticTokens_IdentifiesEnumMember()
    {
        const string src = "enum Color { Red, Green, Blue }";
        var tree = Analyze(src);

        // "enum Color { " = 13 chars, Red starts at col 14
        var sym = tree.FindDefinition("Red", 1, 14);
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.EnumMember, sym!.Kind);
    }

    [Fact]
    public void SemanticTokens_IdentifiesLoopVariable()
    {
        const string src = "for (let i in [1,2,3]) {\n  i;\n}";
        var tree = Analyze(src);

        // Line 2: "  i;" — i is at col 3, inside loop body where loop variable is visible
        var sym = tree.FindDefinition("i", 2, 3);
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.LoopVariable, sym!.Kind);
    }

    // ──────────────────────────────────────────────────────────
    // 5. Comment Detection and Token Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void CommentRanges_ConsecutiveLineComments_Detected()
    {
        // Comments are stripped by the lexer; verify surrounding code still parses correctly
        const string src = "// comment 1\n// comment 2\nlet x = 1;";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();

        // Comment text is stripped — no identifier "comment" should appear
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Identifier && t.Lexeme == "comment");
        // The let declaration following the comments is still present
        Assert.Contains(tokens, t => t.Type == TokenType.Let);
        Assert.Contains(tokens, t => t.Type == TokenType.Identifier && t.Lexeme == "x");
    }

    [Fact]
    public void Tokens_KeywordsPresent()
    {
        const string src = "let x = 1;\nconst y = 2;\nfn test() { return x; }";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();

        Assert.Contains(tokens, t => t.Type == TokenType.Let);
        Assert.Contains(tokens, t => t.Type == TokenType.Const);
        Assert.Contains(tokens, t => t.Type == TokenType.Fn);
        Assert.Contains(tokens, t => t.Type == TokenType.Return);
    }
}
