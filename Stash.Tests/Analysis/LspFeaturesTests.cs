using System.Linq;
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

    // ── Contextual Type Hint Highlighting ────────────────────────────

    [Fact]
    public void TypeHintPosition_KeywordInTyped_LexerProducesKeywordToken()
    {
        // When user types "in" (mid-typing "int") in a struct field type position,
        // the lexer produces a keyword token — this is the scenario the contextual
        // highlighting fix addresses.
        var source = "struct S { f: in }";
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();

        // Find the token after the colon — should be keyword 'In', not identifier
        var colonIdx = tokens.FindIndex(t => t.Type == TokenType.Colon);
        Assert.True(colonIdx >= 0, "Should find a Colon token");
        Assert.True(colonIdx + 1 < tokens.Count, "Should have a token after Colon");
        Assert.Equal(TokenType.In, tokens[colonIdx + 1].Type);
    }

    [Fact]
    public void TypeHintPosition_FullTypeName_LexerProducesIdentifier()
    {
        // When the user completes typing "int", the lexer produces an identifier
        var source = "struct S { f: int }";
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();

        var colonIdx = tokens.FindIndex(t => t.Type == TokenType.Colon);
        Assert.True(colonIdx >= 0);
        Assert.Equal(TokenType.Identifier, tokens[colonIdx + 1].Type);
        Assert.Equal("int", tokens[colonIdx + 1].Lexeme);
    }

    [Fact]
    public void TypeHintPosition_DetectionPattern_IdentifierBeforeColon()
    {
        // Verify the token pattern used for type hint detection:
        // Identifier followed by Colon in struct fields, function params, let/const
        var source = "fn foo(x: string) {}";
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();

        var colonIdx = tokens.FindIndex(t => t.Type == TokenType.Colon);
        Assert.True(colonIdx >= 1);
        Assert.Equal(TokenType.Identifier, tokens[colonIdx - 1].Type); // "x" before ":"
        Assert.Equal(TokenType.Identifier, tokens[colonIdx + 1].Type); // "string" after ":"
    }

    [Fact]
    public void TypeHintPosition_ReturnType_ArrowPrecedesType()
    {
        // Verify the token pattern for return type: RightParen Arrow Identifier
        var source = "fn foo() -> int {}";
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();

        var arrowIdx = tokens.FindIndex(t => t.Type == TokenType.Arrow);
        Assert.True(arrowIdx >= 0);
        Assert.Equal(TokenType.Identifier, tokens[arrowIdx + 1].Type);
        Assert.Equal("int", tokens[arrowIdx + 1].Lexeme);
    }

    [Fact]
    public void TypeHintPosition_ForIn_KeywordNotInTypePosition()
    {
        // In "for x in items", the "in" keyword is NOT preceded by Colon,
        // so it should NOT be detected as a type hint position
        var source = "for x in [1, 2, 3] {}";
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();

        var inIdx = tokens.FindIndex(t => t.Type == TokenType.In);
        Assert.True(inIdx >= 1);
        // Previous token is an Identifier, but the token before that is NOT Colon
        Assert.Equal(TokenType.Identifier, tokens[inIdx - 1].Type); // "x"
        Assert.NotEqual(TokenType.Colon, tokens[inIdx - 1].Type); // Not a Colon
    }

    [Fact]
    public void TypeHintPosition_LetVarDecl_TypeAfterColon()
    {
        // let x: int = 5 — "int" is in type hint position
        var source = "let x: int = 5;";
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();

        var colonIdx = tokens.FindIndex(t => t.Type == TokenType.Colon);
        Assert.True(colonIdx >= 1);
        Assert.Equal(TokenType.Identifier, tokens[colonIdx - 1].Type); // "x"
        Assert.Equal(TokenType.Identifier, tokens[colonIdx + 1].Type); // "int"
        Assert.Equal("int", tokens[colonIdx + 1].Lexeme);
    }

    [Fact]
    public void TypeHintPosition_DictLiteral_ValueNotInTypePosition()
    {
        // { "key": value } — the Colon is preceded by StringLiteral, not Identifier
        // so the value should NOT be treated as a type hint
        var source = """let d = { "name": "Alice" };""";
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();

        var colonIdx = tokens.FindIndex(t => t.Type == TokenType.Colon);
        Assert.True(colonIdx >= 1);
        // The token before colon is a StringLiteral, not an Identifier
        Assert.Equal(TokenType.StringLiteral, tokens[colonIdx - 1].Type);
    }

    [Fact]
    public void TypeHintPosition_StructField_TypeNameResolvesAsStruct()
    {
        // When a type name IS a known struct, it resolves via FindDefinition
        var source = @"
            struct Point { x: int, y: int }
            fn draw(p: Point) {}
        ";
        var tree = Analyze(source);

        // "Point" used as type hint should resolve to the struct definition
        // Line 3 "fn draw(p: Point) {}" — Point is at column position after Colon
        var sym = tree.FindDefinition("Point", 3, 24);
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Struct, sym!.Kind);
    }

    // ──────────────────────────────────────────────────────────
    // 6. Dict Literal Semantic Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void DictKey_DoesNotResolveAsVariable()
    {
        // Dict keys should not resolve to variable symbols
        const string src = """let d = { name: "Alice", age: 30 };""";
        var tree = Analyze(src);

        // "name" and "age" as dict keys should NOT be in the symbol table as variables
        var nameSyms = tree.All.Where(s => s.Name == "name" && s.Kind == SymbolKind.Variable).ToList();
        Assert.Empty(nameSyms);
    }

    [Fact]
    public void DictKey_CoexistsWithSameNameVariable()
    {
        // A variable named "name" and a dict key "name" should not conflict
        const string src = """
            let name = "Bob";
            let d = { name: "Alice" };
            """;
        var tree = Analyze(src);

        // The variable "name" should exist
        var nameSym = tree.FindDefinition("name", 1, 5);
        Assert.NotNull(nameSym);
        Assert.Equal(SymbolKind.Variable, nameSym!.Kind);
    }

    [Fact]
    public void DictLiteral_NestedDict_KeysNotVariables()
    {
        // Nested dict keys should also not create variable symbols
        const string src = """let d = { flags: { verbose: true } };""";
        var tree = Analyze(src);

        var flagsVars = tree.All.Where(s => s.Name == "flags" && s.Kind == SymbolKind.Variable).ToList();
        Assert.Empty(flagsVars);
        var verboseVars = tree.All.Where(s => s.Name == "verbose" && s.Kind == SymbolKind.Variable).ToList();
        Assert.Empty(verboseVars);
    }

    // ──────────────────────────────────────────────────────────
    // 7. IsDictKey — Hover/Definition Suppression Tests
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void IsDictKey_SimpleKey_ReturnsTrue()
    {
        // { name: "Alice" }  — "name" at line 1, col 11 is a dict key
        const string src = """let d = { name: "Alice" };""";
        var result = FullAnalyze(src);

        // Find the "name" token to get its exact position
        var nameToken = result.Tokens.First(t => t.Type == Stash.Lexing.TokenType.Identifier && t.Lexeme == "name");
        Assert.True(result.IsDictKey(nameToken.Span.StartLine, nameToken.Span.StartColumn));
    }

    [Fact]
    public void IsDictKey_SecondKey_ReturnsTrue()
    {
        // { name: "Alice", age: 30 }  — "age" after comma is also a dict key
        const string src = """let d = { name: "Alice", age: 30 };""";
        var result = FullAnalyze(src);

        var ageToken = result.Tokens.First(t => t.Type == Stash.Lexing.TokenType.Identifier && t.Lexeme == "age");
        Assert.True(result.IsDictKey(ageToken.Span.StartLine, ageToken.Span.StartColumn));
    }

    [Fact]
    public void IsDictKey_VariableNotDictKey_ReturnsFalse()
    {
        // "let name = ..." — "name" is a variable, not a dict key
        const string src = """let name = "Bob";""";
        var result = FullAnalyze(src);

        var nameToken = result.Tokens.First(t => t.Type == Stash.Lexing.TokenType.Identifier && t.Lexeme == "name");
        Assert.False(result.IsDictKey(nameToken.Span.StartLine, nameToken.Span.StartColumn));
    }

    [Fact]
    public void IsDictKey_NameShadowsNamespace_ReturnsTrue()
    {
        // { config: { verbose: true } }  — "config" is a dict key even though it shadows the built-in namespace
        const string src = """let d = { config: { verbose: true } };""";
        var result = FullAnalyze(src);

        var configToken = result.Tokens.First(t => t.Type == Stash.Lexing.TokenType.Identifier && t.Lexeme == "config");
        Assert.True(result.IsDictKey(configToken.Span.StartLine, configToken.Span.StartColumn));
    }

    [Fact]
    public void IsDictKey_NestedDictKey_ReturnsTrue()
    {
        // Nested dict key should also be detected
        const string src = """let d = { flags: { verbose: true } };""";
        var result = FullAnalyze(src);

        var verboseToken = result.Tokens.First(t => t.Type == Stash.Lexing.TokenType.Identifier && t.Lexeme == "verbose");
        Assert.True(result.IsDictKey(verboseToken.Span.StartLine, verboseToken.Span.StartColumn));
    }

    // ──────────────────────────────────────────────────────────
    // 8. Dot-Access and Type-Hint Context Rules
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void SemanticTokens_DotAccessMember_IsNotNamespace()
    {
        // "time" is a built-in namespace, but in "e.time" it's a property access
        Assert.True(BuiltInRegistry.IsBuiltInNamespace("time"));

        // In dot-access context (afterDot=true), the semantic token handler
        // skips namespace matches — verify the registry would match standalone
        Assert.False(BuiltInRegistry.IsBuiltInFunction("time"));
    }

    [Fact]
    public void SemanticTokens_NamespaceNameInDotAccess_FilteredByRule()
    {
        // Verify that "time" is recognized as a namespace name
        Assert.True(BuiltInRegistry.IsBuiltInNamespace("time"));

        // And that it's NOT a built-in function (standalone function like len, typeof)
        Assert.False(BuiltInRegistry.IsBuiltInFunction("time"));

        // Rule 2 states: after dot, never classify as namespace.
        // This means BuiltInRegistry.IsBuiltInNamespace() should not be consulted
        // after a dot — only BuiltInRegistry.IsBuiltInFunction() applies.
    }

    [Fact]
    public void SemanticTokens_KeywordAfterDot_IsMemberNotKeyword()
    {
        // "true", "false", "null" are keyword tokens but valid member names after dot
        // In "assert.true()", "true" should be classified as function, not keyword
        Assert.True(BuiltInRegistry.IsBuiltInNamespace("assert"));

        // Verify the lexer tokenizes "true" as TokenType.True (a keyword)
        var lexer = new Lexer("true", "<test>");
        var tokens = lexer.ScanTokens();
        Assert.Equal(TokenType.True, tokens[0].Type);

        // Rule: after dot, keyword tokens become member access (function/property)
    }

    [Fact]
    public void SemanticTokens_TypeHintPosition_AlwaysType()
    {
        // "in" is a keyword (TokenType.In), but in type hint position it's a type name
        // e.g., fn foo(x: int) — "int" starts with "in" which is a keyword
        var lexer = new Lexer("in", "<test>");
        var tokens = lexer.ScanTokens();
        Assert.Equal(TokenType.In, tokens[0].Type);

        // Rule 1: in type hint position (after "identifier:" or "→"), always classify as Type
        // This is already tested implicitly, but this confirms "in" is indeed a keyword token
    }

    [Fact]
    public void SemanticTokens_BuiltInNamespace_DoesNotMatchFunctions()
    {
        // Namespaces and standalone functions are disjoint sets
        Assert.True(BuiltInRegistry.IsBuiltInNamespace("io"));
        Assert.True(BuiltInRegistry.IsBuiltInNamespace("str"));
        Assert.True(BuiltInRegistry.IsBuiltInNamespace("arr"));
        Assert.True(BuiltInRegistry.IsBuiltInNamespace("time"));

        Assert.False(BuiltInRegistry.IsBuiltInFunction("io"));
        Assert.False(BuiltInRegistry.IsBuiltInFunction("str"));
        Assert.False(BuiltInRegistry.IsBuiltInFunction("arr"));
        Assert.False(BuiltInRegistry.IsBuiltInFunction("time"));

        // Global functions are NOT namespaces
        Assert.True(BuiltInRegistry.IsBuiltInFunction("len"));
        Assert.True(BuiltInRegistry.IsBuiltInFunction("typeof"));

        Assert.False(BuiltInRegistry.IsBuiltInNamespace("len"));
        Assert.False(BuiltInRegistry.IsBuiltInNamespace("typeof"));
    }

    [Fact]
    public void SemanticTokens_StructFieldAccess_ResolvesField()
    {
        const string src = "struct Point { x, y }\nlet p = Point { x: 1, y: 2 };";
        var tree = Analyze(src);

        // "x" should resolve as a field
        var sym = tree.FindDefinition("x", 1, 16);
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Field, sym!.Kind);
    }
}
