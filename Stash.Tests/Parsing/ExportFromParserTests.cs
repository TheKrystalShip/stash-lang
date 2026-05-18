using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Parsing;

/// <summary>
/// Parser tests for the re-export forms added in Phase 2A of the export-from-import feature.
/// Covers <c>export expr as alias;</c> (<see cref="ExportModuleAsStmt"/>) and
/// <c>export { names } from expr;</c> (<see cref="ExportFromStmt"/>), together with
/// disambiguation, error cases, and regressions.
/// </summary>
public class ExportFromParserTests
{
    private static List<Stmt> ParseProgram(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        return new Parser(tokens).ParseProgram();
    }

    private static void RunExpectingParseError(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        Assert.NotEmpty(parser.Errors);
    }

    // =========================================================================
    // ExportModuleAsStmt — namespace re-export path form
    // =========================================================================

    [Fact]
    public void Parse_ExportStringPathAsAlias_ProducesExportModuleAsStmt()
    {
        var stmts = ParseProgram("""export "lib/data.stash" as data;""");
        var stmt = Assert.IsType<ExportModuleAsStmt>(Assert.Single(stmts));
        Assert.Equal("data", stmt.Alias.Lexeme);
        Assert.Equal("export", stmt.ExportKeyword.Lexeme);
        Assert.Equal("as", stmt.AsKeyword.Lexeme);
        var lit = Assert.IsType<LiteralExpr>(stmt.Path);
        Assert.Equal("lib/data.stash", lit.Value);
    }

    [Fact]
    public void Parse_ExportDynamicPathAsAlias_ProducesExportModuleAsStmt()
    {
        // D-9: dynamic path expressions are accepted, mirroring import's grammar
        var stmts = ParseProgram("export some_const as ns;");
        var stmt = Assert.IsType<ExportModuleAsStmt>(Assert.Single(stmts));
        Assert.Equal("ns", stmt.Alias.Lexeme);
        var ident = Assert.IsType<IdentifierExpr>(stmt.Path);
        Assert.Equal("some_const", ident.Name.Lexeme);
    }

    [Fact]
    public void Parse_ExportFnCallPathAsAlias_ProducesExportModuleAsStmt()
    {
        var stmts = ParseProgram("export get_path() as lib;");
        var stmt = Assert.IsType<ExportModuleAsStmt>(Assert.Single(stmts));
        Assert.Equal("lib", stmt.Alias.Lexeme);
        Assert.IsType<CallExpr>(stmt.Path);
    }

    [Fact]
    public void Parse_ExportModuleAs_SourceSpanCoversFullStatement()
    {
        var stmts = ParseProgram("""export "p" as x;""");
        var stmt = Assert.IsType<ExportModuleAsStmt>(Assert.Single(stmts));
        // Span starts at 'export' and ends at ';'
        Assert.Equal(1, stmt.Span.StartLine);
        Assert.True(stmt.Span.EndColumn > stmt.Span.StartColumn);
    }

    // =========================================================================
    // ExportFromStmt — selective named re-export form
    // =========================================================================

    [Fact]
    public void Parse_ExportBracesSingleNameFromPath_ProducesExportFromStmt()
    {
        var stmts = ParseProgram("""export { foo } from "lib/x.stash";""");
        var stmt = Assert.IsType<ExportFromStmt>(Assert.Single(stmts));
        var name = Assert.Single(stmt.Names);
        Assert.Equal("foo", name.Lexeme);
        Assert.Equal("from", stmt.FromKeyword.Lexeme);
        var lit = Assert.IsType<LiteralExpr>(stmt.Path);
        Assert.Equal("lib/x.stash", lit.Value);
    }

    [Fact]
    public void Parse_ExportBracesMultipleNamesFromPath_ProducesExportFromStmt()
    {
        var stmts = ParseProgram("""export { Color, Size, Direction } from "lib/types.stash";""");
        var stmt = Assert.IsType<ExportFromStmt>(Assert.Single(stmts));
        Assert.Equal(3, stmt.Names.Count);
        Assert.Equal("Color", stmt.Names[0].Lexeme);
        Assert.Equal("Size", stmt.Names[1].Lexeme);
        Assert.Equal("Direction", stmt.Names[2].Lexeme);
    }

    [Fact]
    public void Parse_ExportBracesTrailingCommaFromPath_ProducesExportFromStmt()
    {
        // Trailing-comma variant must parse
        var stmts = ParseProgram("""export { a, b, } from "p";""");
        var stmt = Assert.IsType<ExportFromStmt>(Assert.Single(stmts));
        Assert.Equal(2, stmt.Names.Count);
        Assert.Equal("a", stmt.Names[0].Lexeme);
        Assert.Equal("b", stmt.Names[1].Lexeme);
    }

    [Fact]
    public void Parse_ExportEmptyBracesFromPath_ProducesExportFromStmtWithZeroNames()
    {
        // Empty list is syntactically valid; SA0812 fires in Phase 2C (not the parser)
        var stmts = ParseProgram("""export {} from "p";""");
        var stmt = Assert.IsType<ExportFromStmt>(Assert.Single(stmts));
        Assert.Empty(stmt.Names);
    }

    [Fact]
    public void Parse_ExportBracesFromDynamicPath_ProducesExportFromStmt()
    {
        // D-9: dynamic path expression in from-form
        var stmts = ParseProgram("export { foo } from path_fn();");
        var stmt = Assert.IsType<ExportFromStmt>(Assert.Single(stmts));
        var name = Assert.Single(stmt.Names);
        Assert.Equal("foo", name.Lexeme);
        Assert.IsType<CallExpr>(stmt.Path);
    }

    [Fact]
    public void Parse_ExportFromStmt_SourceSpanCoversFullStatement()
    {
        var stmts = ParseProgram("""export { a, b } from "p";""");
        var stmt = Assert.IsType<ExportFromStmt>(Assert.Single(stmts));
        Assert.Equal(1, stmt.Span.StartLine);
        Assert.True(stmt.Span.EndColumn > stmt.Span.StartColumn);
    }

    // =========================================================================
    // Back-compat: export {} without 'from' still produces ExportBlockStmt
    // =========================================================================

    [Fact]
    public void Parse_ExportEmptyBracesNoFrom_ProducesExportBlockStmt()
    {
        // Back-compat: export {}; (no 'from') must still parse as ExportBlockStmt
        var stmts = ParseProgram("export {};");
        var stmt = Assert.IsType<ExportBlockStmt>(Assert.Single(stmts));
        Assert.Empty(stmt.Names);
    }

    [Fact]
    public void Parse_ExportBracesNoFrom_ProducesExportBlockStmt()
    {
        // Back-compat: export { a, b }; (no 'from') is unchanged ExportBlockStmt
        var stmts = ParseProgram("export { a, b };");
        var stmt = Assert.IsType<ExportBlockStmt>(Assert.Single(stmts));
        Assert.Equal(2, stmt.Names.Count);
    }

    // =========================================================================
    // Error cases
    // =========================================================================

    [Fact]
    public void Parse_ExportWildcardFrom_RaisesParseErrorReferencingSA0811()
    {
        var tokens = new Lexer("""export * from "p";""", "<test>").ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        Assert.NotEmpty(parser.Errors);
        Assert.Contains("SA0811", parser.Errors[0]);
    }

    [Fact]
    public void Parse_ExportStringPathWithoutAs_RaisesParseError()
    {
        // export "p"; — missing 'as <alias>' must error
        RunExpectingParseError("""export "p";""");
    }

    // =========================================================================
    // Soft-keyword disambiguation regressions
    // =========================================================================

    [Fact]
    public void Parse_ExportCallExpression_ParsesAsExpressionStatement()
    {
        // Regression: export(42); must parse as an expression statement, not an export declaration.
        // The path-form scan sees ') ;' at depth 0 — trailing tokens are not 'as Identifier ;'.
        var stmts = ParseProgram("fn export() {} export(42);");
        Assert.Equal(2, stmts.Count);
        var callStmt = Assert.IsType<ExprStmt>(stmts[1]);
        Assert.IsType<CallExpr>(callStmt.Expression);
    }

    [Fact]
    public void Parse_ExportIdentifier_WithoutAsTrailer_ParsesAsExpressionStatement()
    {
        // Regression (Q4): the path-form scan must NOT fire when the statement does not end
        // in 'as Identifier ;'. 'export(foo)' ends in ') ;' — not 'as Identifier ;'.
        // This specifically guards against over-activation of IsExportPathFormLookahead.
        var stmts = ParseProgram("fn export(x) {} export(foo);");
        Assert.Equal(2, stmts.Count);
        // Second statement is an expression statement, not an ExportModuleAsStmt
        var callStmt = Assert.IsType<ExprStmt>(stmts[1]);
        Assert.IsType<CallExpr>(callStmt.Expression);
    }

    [Fact]
    public void Parse_ExportAsIdentifier_InCallPosition_StillWorks()
    {
        // export used purely as an identifier (call) is unchanged
        var stmts = ParseProgram("fn export(x) {} export(x);");
        Assert.Equal(2, stmts.Count);
        Assert.IsType<ExprStmt>(stmts[1]);
    }
}
