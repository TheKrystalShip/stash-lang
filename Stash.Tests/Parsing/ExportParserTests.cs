using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Parsing;

/// <summary>
/// Parser tests for the soft-keyword <c>export</c> statement.
/// Covers spec §3.2.2 (Parser / AST) — all cases from Section 6 of the design spec.
/// </summary>
public class ExportParserTests
{
    private static List<Stmt> ParseProgram(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        return new Parser(tokens).ParseProgram();
    }

    /// <summary>
    /// Asserts that the parser records at least one error for the given source.
    /// </summary>
    private static void RunExpectingParseError(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        Assert.NotEmpty(parser.Errors);
    }

    // =========================================================================
    // Declaration-site form — ExportDeclStmt
    // =========================================================================

    [Fact]
    public void Parse_ExportFn_ProducesExportDeclStmt()
    {
        var stmts = ParseProgram("export fn greet(name) { }");
        var exportStmt = Assert.IsType<ExportDeclStmt>(Assert.Single(stmts));
        var fn = Assert.IsType<FnDeclStmt>(exportStmt.Inner);
        Assert.Equal("greet", fn.Name.Lexeme);
        Assert.False(fn.IsAsync);
    }

    [Fact]
    public void Parse_ExportConst_ProducesExportDeclStmt()
    {
        var stmts = ParseProgram("""export const VERSION = "1.0.0";""");
        var exportStmt = Assert.IsType<ExportDeclStmt>(Assert.Single(stmts));
        var constDecl = Assert.IsType<ConstDeclStmt>(exportStmt.Inner);
        Assert.Equal("VERSION", constDecl.Name.Lexeme);
    }

    [Fact]
    public void Parse_ExportStruct_ProducesExportDeclStmt()
    {
        var stmts = ParseProgram("export struct Point { x: int, y: int }");
        var exportStmt = Assert.IsType<ExportDeclStmt>(Assert.Single(stmts));
        var structDecl = Assert.IsType<StructDeclStmt>(exportStmt.Inner);
        Assert.Equal("Point", structDecl.Name.Lexeme);
    }

    [Fact]
    public void Parse_ExportEnum_ProducesExportDeclStmt()
    {
        var stmts = ParseProgram("export enum Status { Ok, Err }");
        var exportStmt = Assert.IsType<ExportDeclStmt>(Assert.Single(stmts));
        var enumDecl = Assert.IsType<EnumDeclStmt>(exportStmt.Inner);
        Assert.Equal("Status", enumDecl.Name.Lexeme);
    }

    [Fact]
    public void Parse_ExportInterface_ProducesExportDeclStmt()
    {
        var stmts = ParseProgram("export interface Closer { fn close() }");
        var exportStmt = Assert.IsType<ExportDeclStmt>(Assert.Single(stmts));
        var iface = Assert.IsType<InterfaceDeclStmt>(exportStmt.Inner);
        Assert.Equal("Closer", iface.Name.Lexeme);
    }

    [Fact]
    public void Parse_ExportAsyncFn_ProducesExportDeclStmtWithAsyncInner()
    {
        var stmts = ParseProgram("export async fn fetch(url) { }");
        var exportStmt = Assert.IsType<ExportDeclStmt>(Assert.Single(stmts));
        var fn = Assert.IsType<FnDeclStmt>(exportStmt.Inner);
        Assert.Equal("fetch", fn.Name.Lexeme);
        Assert.True(fn.IsAsync);
    }

    // =========================================================================
    // Block form — ExportBlockStmt
    // =========================================================================

    [Fact]
    public void Parse_ExportBlock_Single_ProducesExportBlockStmt()
    {
        var stmts = ParseProgram("export { diff };");
        var exportStmt = Assert.IsType<ExportBlockStmt>(Assert.Single(stmts));
        var name = Assert.Single(exportStmt.Names);
        Assert.Equal("diff", name.Lexeme);
    }

    [Fact]
    public void Parse_ExportBlock_Multi_TrailingComma_OK()
    {
        var stmts = ParseProgram("export { diff, VERSION, };");
        var exportStmt = Assert.IsType<ExportBlockStmt>(Assert.Single(stmts));
        Assert.Equal(2, exportStmt.Names.Count);
        Assert.Equal("diff", exportStmt.Names[0].Lexeme);
        Assert.Equal("VERSION", exportStmt.Names[1].Lexeme);
    }

    [Fact]
    public void Parse_ExportBlock_Empty_ProducesExportBlockStmtWithZeroNames()
    {
        var stmts = ParseProgram("export { };");
        var exportStmt = Assert.IsType<ExportBlockStmt>(Assert.Single(stmts));
        Assert.Empty(exportStmt.Names);
    }

    // =========================================================================
    // Error cases — disallowed export targets
    // =========================================================================

    [Fact]
    public void Parse_ExportLet_RaisesParseError()
    {
        RunExpectingParseError("export let counter = 0;");
    }

    [Fact]
    public void Parse_ExportExtend_RaisesParseError()
    {
        RunExpectingParseError("export extend string { fn shout() { } }");
    }

    [Fact]
    public void Parse_ExportImport_RaisesParseError()
    {
        RunExpectingParseError("""export import { foo } from "other.stash";""");
    }

    // =========================================================================
    // Soft-keyword disambiguation — export must NOT consume identifier uses
    // =========================================================================

    [Fact]
    public void Parse_ExportCall_AtCallPosition_StillCalls()
    {
        // export(foo); — 'export' followed by '(' is an expression statement, not an export declaration
        var stmts = ParseProgram("fn export() {} export();");
        Assert.Equal(2, stmts.Count);
        var callStmt = Assert.IsType<ExprStmt>(stmts[1]);
        Assert.IsType<CallExpr>(callStmt.Expression);
    }

    [Fact]
    public void Parse_FnExport_AsName_StillWorks()
    {
        // fn export(...) {} — 'export' used as a function name must still work (containers.stash pattern)
        var stmts = ParseProgram("fn export(container, output_path) { }");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal("export", fn.Name.Lexeme);
    }

    [Fact]
    public void Parse_LetExportEquals_StillWorks()
    {
        // let export = 5; — 'export' as a variable name
        var stmts = ParseProgram("let export = 5;");
        var decl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.Equal("export", decl.Name.Lexeme);
    }

    // =========================================================================
    // Source span coverage
    // =========================================================================

    [Fact]
    public void Parse_ExportFn_ExportKeywordSpanIsCorrect()
    {
        var stmts = ParseProgram("export fn greet() { }");
        var exportStmt = Assert.IsType<ExportDeclStmt>(Assert.Single(stmts));
        Assert.Equal("export", exportStmt.ExportKeyword.Lexeme);
    }

    [Fact]
    public void Parse_ExportBlock_ExportKeywordSpanIsCorrect()
    {
        var stmts = ParseProgram("export { foo };");
        var exportStmt = Assert.IsType<ExportBlockStmt>(Assert.Single(stmts));
        Assert.Equal("export", exportStmt.ExportKeyword.Lexeme);
    }
}
