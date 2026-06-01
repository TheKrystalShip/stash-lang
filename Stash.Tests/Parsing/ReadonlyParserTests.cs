using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Parsing;

/// <summary>
/// Parser-level tests for the <c>readonly</c> soft keyword modifier.
/// Covers: VarDeclStmt/ConstDeclStmt flags, 3-way dispatch in Declaration(),
/// for-init rejection, export readonly const, soft-keyword identifier fall-through.
/// </summary>
public class ReadonlyParserTests
{
    private static List<Stmt> ParseProgram(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseProgram();
    }

    private static Parser ParseProgramWithErrors(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        return parser;
    }

    // ── readonly let ──────────────────────────────────────────────────────────

    [Fact]
    public void ReadonlyLet_ProducesVarDeclStmtWithIsReadonlyTrue()
    {
        var stmts = ParseProgram("readonly let x = 1;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.Equal("x", varDecl.Name.Lexeme);
        Assert.True(varDecl.IsReadonly);
        Assert.NotNull(varDecl.ReadonlyKeyword);
        Assert.Equal("readonly", varDecl.ReadonlyKeyword!.Lexeme);
    }

    [Fact]
    public void ReadonlyLet_ReadonlyKeywordToken_LexemeIsReadonly()
    {
        var stmts = ParseProgram("readonly let counter = 0;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.NotNull(varDecl.ReadonlyKeyword);
        Assert.Equal("readonly", varDecl.ReadonlyKeyword!.Lexeme);
    }

    [Fact]
    public void PlainLet_IsReadonlyFalse_ReadonlyKeywordNull()
    {
        var stmts = ParseProgram("let x = 1;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.False(varDecl.IsReadonly);
        Assert.Null(varDecl.ReadonlyKeyword);
    }

    // ── readonly const ────────────────────────────────────────────────────────

    [Fact]
    public void ReadonlyConst_ProducesConstDeclStmtWithIsReadonlyTrue()
    {
        var stmts = ParseProgram("readonly const Config = 42;");
        var constDecl = Assert.IsType<ConstDeclStmt>(Assert.Single(stmts));
        Assert.Equal("Config", constDecl.Name.Lexeme);
        Assert.True(constDecl.IsReadonly);
        Assert.NotNull(constDecl.ReadonlyKeyword);
        Assert.Equal("readonly", constDecl.ReadonlyKeyword!.Lexeme);
    }

    [Fact]
    public void PlainConst_IsReadonlyFalse_ReadonlyKeywordNull()
    {
        var stmts = ParseProgram("const VERSION = \"1.0\";");
        var constDecl = Assert.IsType<ConstDeclStmt>(Assert.Single(stmts));
        Assert.False(constDecl.IsReadonly);
        Assert.Null(constDecl.ReadonlyKeyword);
    }

    // ── Soft keyword — 'readonly' as a plain identifier ──────────────────────

    [Fact]
    public void ReadonlyAsIdentifier_LetReadonly_ParsesCorrectly()
    {
        // 'readonly' must remain a legal variable name (soft keyword)
        var stmts = ParseProgram("let readonly = 1;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.Equal("readonly", varDecl.Name.Lexeme);
        Assert.False(varDecl.IsReadonly);
    }

    [Fact]
    public void ReadonlyAsIdentifier_AssignmentExpression_ParsesCorrectly()
    {
        // Using 'readonly' as an expression (identifier) must not cause a parse error
        var parser = ParseProgramWithErrors("readonly = true;");
        Assert.Empty(parser.Errors);
    }

    // ── 3-way branch: readonly before fn/struct/enum/interface → diagnostic ──

    [Fact]
    public void ReadonlyBeforeFn_ProducesParseError()
    {
        var parser = ParseProgramWithErrors("readonly fn greet() { }");
        Assert.NotEmpty(parser.Errors);
        Assert.Contains(parser.Errors, e => e.Contains("readonly"));
    }

    [Fact]
    public void ReadonlyBeforeStruct_ProducesParseError()
    {
        var parser = ParseProgramWithErrors("readonly struct Point { }");
        Assert.NotEmpty(parser.Errors);
        Assert.Contains(parser.Errors, e => e.Contains("readonly"));
    }

    [Fact]
    public void ReadonlyBeforeEnum_ProducesParseError()
    {
        var parser = ParseProgramWithErrors("readonly enum Color { Red }");
        Assert.NotEmpty(parser.Errors);
        Assert.Contains(parser.Errors, e => e.Contains("readonly"));
    }

    [Fact]
    public void ReadonlyBeforeInterface_ProducesParseError()
    {
        var parser = ParseProgramWithErrors("readonly interface Serializable { }");
        Assert.NotEmpty(parser.Errors);
        Assert.Contains(parser.Errors, e => e.Contains("readonly"));
    }

    // ── for-init clause rejection ─────────────────────────────────────────────

    [Fact]
    public void ReadonlyInForInit_CStyle_ProducesParseError()
    {
        var parser = ParseProgramWithErrors("for (readonly let i = 0; i < 10; i++) { }");
        Assert.NotEmpty(parser.Errors);
        Assert.Contains(parser.Errors, e => e.Contains("'readonly'") || e.Contains("readonly"));
    }

    [Fact]
    public void ReadonlyInForIn_ProducesParseError()
    {
        var parser = ParseProgramWithErrors("for (readonly let x in items) { }");
        Assert.NotEmpty(parser.Errors);
        Assert.Contains(parser.Errors, e => e.Contains("'readonly'") || e.Contains("readonly"));
    }

    // ── export readonly const ─────────────────────────────────────────────────

    [Fact]
    public void ExportReadonlyConst_ProducesExportDeclStmtWithReadonlyInner()
    {
        var stmts = ParseProgram("""export readonly const Config = 42;""");
        var exportDecl = Assert.IsType<ExportDeclStmt>(Assert.Single(stmts));
        var constDecl = Assert.IsType<ConstDeclStmt>(exportDecl.Inner);
        Assert.True(constDecl.IsReadonly);
        Assert.Equal("Config", constDecl.Name.Lexeme);
    }

    [Fact]
    public void ExportReadonlyLet_ProducesParseError()
    {
        // 'export readonly let' must be rejected (exporting mutable bindings is disallowed)
        var parser = ParseProgramWithErrors("export readonly let x = 1;");
        Assert.NotEmpty(parser.Errors);
    }

    // ── Span coverage: readonly is included in node span ─────────────────────

    [Fact]
    public void ReadonlyLet_NodeSpanStartsAtReadonlyToken()
    {
        var stmts = ParseProgram("readonly let x = 1;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        // Span should start at column 1 (the 'readonly' keyword), not at 'let'
        Assert.Equal(1, varDecl.Span.StartColumn);
    }

    [Fact]
    public void ReadonlyConst_NodeSpanStartsAtReadonlyToken()
    {
        var stmts = ParseProgram("readonly const X = 1;");
        var constDecl = Assert.IsType<ConstDeclStmt>(Assert.Single(stmts));
        Assert.Equal(1, constDecl.Span.StartColumn);
    }

    // ── Multiple readonly declarations in a block ─────────────────────────────

    [Fact]
    public void ReadonlyDeclarations_InsideBlock_ParseCorrectly()
    {
        var stmts = ParseProgram("""
            {
                readonly let a = 1;
                readonly const B = 2;
            }
            """);
        var block = Assert.IsType<BlockStmt>(Assert.Single(stmts));
        Assert.Equal(2, block.Statements.Count);
        var varDecl = Assert.IsType<VarDeclStmt>(block.Statements[0]);
        var constDecl = Assert.IsType<ConstDeclStmt>(block.Statements[1]);
        Assert.True(varDecl.IsReadonly);
        Assert.True(constDecl.IsReadonly);
    }
}
