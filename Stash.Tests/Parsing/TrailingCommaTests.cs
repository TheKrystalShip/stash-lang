using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Parsing;

/// <summary>
/// Verifies that every grammar production marked with an optional trailing comma (<c>","?</c>
/// in Appendix A) accepts a trailing comma, and that lone / double commas are still parse errors.
///
/// Grammar reference: Appendix A of "Stash — Language Specification.md"
/// </summary>
public class TrailingCommaTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static List<Stmt> ParseProgram(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        return new Parser(tokens).ParseProgram();
    }

    private static void ParseExpectingError(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        Assert.NotEmpty(parser.Errors);
    }

    private static Expr ParseExpr(string source)
    {
        var tokens = new Lexer(source, "<test>").ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // POSITIVE — trailing comma now accepted
    // ═════════════════════════════════════════════════════════════════════════

    // ── argumentList: array literals ────────────────────────────────────────

    [Fact]
    public void ArrayLiteral_TrailingComma_ParsesSuccessfully()
    {
        var expr = ParseExpr("[1, 2, ]");
        var arr = Assert.IsType<ArrayExpr>(expr);
        Assert.Equal(2, arr.Elements.Count);
    }

    [Fact]
    public void ArrayLiteral_SingleElementTrailingComma_ParsesSuccessfully()
    {
        var expr = ParseExpr("[42,]");
        var arr = Assert.IsType<ArrayExpr>(expr);
        Assert.Single(arr.Elements);
    }

    [Fact]
    public void ArrayLiteral_EmptyNoComma_StillParsesSuccessfully()
    {
        var expr = ParseExpr("[]");
        var arr = Assert.IsType<ArrayExpr>(expr);
        Assert.Empty(arr.Elements);
    }

    // ── argumentList: call arguments ─────────────────────────────────────────

    [Fact]
    public void CallArguments_TrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("f(1, 2, 3,);");
        var callExpr = Assert.IsType<CallExpr>(Assert.IsType<ExprStmt>(Assert.Single(stmts)).Expression);
        Assert.Equal(3, callExpr.Arguments.Count);
    }

    [Fact]
    public void CallArguments_SingleArgTrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("f(x,);");
        var callExpr = Assert.IsType<CallExpr>(Assert.IsType<ExprStmt>(Assert.Single(stmts)).Expression);
        Assert.Single(callExpr.Arguments);
    }

    // ── parameterList: fn and lambda ────────────────────────────────────────

    [Fact]
    public void FnDecl_TrailingCommaInParams_ParsesSuccessfully()
    {
        var stmts = ParseProgram("fn f(a, b,) {}");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal(2, fn.Parameters.Count);
    }

    [Fact]
    public void FnDecl_SingleParamTrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("fn f(a,) {}");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Single(fn.Parameters);
    }

    [Fact]
    public void Lambda_TrailingCommaInParams_ParsesSuccessfully()
    {
        var expr = ParseExpr("(a, b,) => a + b");
        var lambda = Assert.IsType<LambdaExpr>(expr);
        Assert.Equal(2, lambda.Parameters.Count);
    }

    [Fact]
    public void Lambda_SingleParamTrailingComma_ParsesSuccessfully()
    {
        var expr = ParseExpr("(x,) => x");
        var lambda = Assert.IsType<LambdaExpr>(expr);
        Assert.Single(lambda.Parameters);
    }

    // ── dictEntryList: dict literals ─────────────────────────────────────────

    [Fact]
    public void DictLiteral_TrailingComma_ParsesSuccessfully()
    {
        var expr = ParseExpr("{ a: 1, b: 2, }");
        var dict = Assert.IsType<DictLiteralExpr>(expr);
        Assert.Equal(2, dict.Entries.Count);
    }

    [Fact]
    public void DictLiteral_SingleEntryTrailingComma_ParsesSuccessfully()
    {
        var expr = ParseExpr("{ x: 42, }");
        var dict = Assert.IsType<DictLiteralExpr>(expr);
        Assert.Single(dict.Entries);
    }

    [Fact]
    public void DictLiteral_ComputedKeyTrailingComma_ParsesSuccessfully()
    {
        var expr = ParseExpr("{ [\"a\"]: 1, }");
        var dict = Assert.IsType<DictLiteralExpr>(expr);
        Assert.Single(dict.Entries);
    }

    [Fact]
    public void DictLiteral_StringKeyTrailingComma_ParsesSuccessfully()
    {
        var expr = ParseExpr("{ \"hello\": 1, }");
        var dict = Assert.IsType<DictLiteralExpr>(expr);
        Assert.Single(dict.Entries);
    }

    // ── structFieldList ──────────────────────────────────────────────────────

    [Fact]
    public void StructDecl_TrailingCommaInFields_ParsesSuccessfully()
    {
        var stmts = ParseProgram("struct Point { x, y, }");
        var s = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Equal(2, s.Fields.Count);
    }

    [Fact]
    public void StructDecl_SingleFieldTrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("struct Wrapper { value, }");
        var s = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Single(s.Fields);
    }

    [Fact]
    public void StructDecl_FieldsAndMethodsTrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("struct S { x, y, fn get() { return 0; } }");
        var s = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Equal(2, s.Fields.Count);
        Assert.Single(s.Methods);
    }

    // ── struct literal entries (structLiteral uses dictEntryList) ────────────

    [Fact]
    public void StructLiteral_TrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("struct S { x, y } let p = S { x: 1, y: 2, };");
        Assert.Equal(2, stmts.Count);
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[1]);
        var init = Assert.IsType<StructInitExpr>(varDecl.Initializer);
        Assert.Equal(2, init.FieldValues.Count);
    }

    [Fact]
    public void StructLiteral_SingleFieldTrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("struct S { x } let p = S { x: 42, };");
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[1]);
        var init = Assert.IsType<StructInitExpr>(varDecl.Initializer);
        Assert.Single(init.FieldValues);
    }

    // ── enumDecl ─────────────────────────────────────────────────────────────

    [Fact]
    public void EnumDecl_TrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("enum Color { Red, Green, Blue, }");
        var e = Assert.IsType<EnumDeclStmt>(Assert.Single(stmts));
        Assert.Equal(3, e.Members.Count);
    }

    [Fact]
    public void EnumDecl_SingleMemberTrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("enum Solo { Only, }");
        var e = Assert.IsType<EnumDeclStmt>(Assert.Single(stmts));
        Assert.Single(e.Members);
    }

    // ── interfaceMemberList ──────────────────────────────────────────────────

    [Fact]
    public void InterfaceDecl_TrailingCommaAfterField_ParsesSuccessfully()
    {
        var stmts = ParseProgram("interface Sized { len: int, }");
        var iface = Assert.IsType<InterfaceDeclStmt>(Assert.Single(stmts));
        Assert.Single(iface.Fields);
    }

    [Fact]
    public void InterfaceDecl_TrailingCommaAfterMethod_ParsesSuccessfully()
    {
        var stmts = ParseProgram("interface Closeable { fn close(), }");
        var iface = Assert.IsType<InterfaceDeclStmt>(Assert.Single(stmts));
        Assert.Single(iface.Methods);
    }

    [Fact]
    public void InterfaceDecl_MultiMemberTrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("interface Reader { fn read() -> int, fn close(), }");
        var iface = Assert.IsType<InterfaceDeclStmt>(Assert.Single(stmts));
        Assert.Equal(2, iface.Methods.Count);
    }

    [Fact]
    public void InterfaceMethodSig_TrailingCommaInParams_ParsesSuccessfully()
    {
        var stmts = ParseProgram("interface Adder { fn add(a, b,) -> int, }");
        var iface = Assert.IsType<InterfaceDeclStmt>(Assert.Single(stmts));
        Assert.Single(iface.Methods);
        Assert.Equal(2, iface.Methods[0].Parameters.Count);
    }

    // ── patternItemList: array and dict destructuring ────────────────────────

    [Fact]
    public void ArrayDestructuring_TrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("let [x, y,] = [1, 2];");
        Assert.IsType<DestructureStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void ArrayDestructuring_SingleVarTrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("let [x,] = [1];");
        Assert.IsType<DestructureStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void DictDestructuring_TrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("let { x, y, } = { x: 1, y: 2 };");
        Assert.IsType<DestructureStmt>(Assert.Single(stmts));
    }

    [Fact]
    public void DictDestructuring_SingleVarTrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("let { x, } = { x: 1 };");
        Assert.IsType<DestructureStmt>(Assert.Single(stmts));
    }

    // ── switchExprArm list (already worked before; confirm still works) ───────

    [Fact]
    public void SwitchExpr_TrailingComma_ParsesSuccessfully()
    {
        // Grammar: switchExprTail = "switch" "{" switchExprArm ("," switchExprArm)* ","? "}"
        var stmts = ParseProgram("let result = x switch { 1 => \"a\", _ => \"b\", };");
        Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
    }

    // ── importDecl name list ──────────────────────────────────────────────────

    [Fact]
    public void Import_TrailingCommaInNames_ParsesSuccessfully()
    {
        var stmts = ParseProgram("import { foo, bar, } from \"./mod\";");
        var imp = Assert.IsType<ImportStmt>(Assert.Single(stmts));
        Assert.Equal(2, imp.Names.Count);
    }

    [Fact]
    public void Import_SingleNameTrailingComma_ParsesSuccessfully()
    {
        var stmts = ParseProgram("import { foo, } from \"./mod\";");
        var imp = Assert.IsType<ImportStmt>(Assert.Single(stmts));
        Assert.Single(imp.Names);
    }

    // ── exportBlock name list (already had the guard at line 788; confirm) ────

    [Fact]
    public void ExportBlock_TrailingCommaInNames_ParsesSuccessfully()
    {
        var stmts = ParseProgram("const a = 1; const b = 2; export { a, b, };");
        Assert.Equal(3, stmts.Count);
        Assert.IsType<ExportBlockStmt>(stmts[2]);
    }

    // ── exportFrom name list ─────────────────────────────────────────────────

    [Fact]
    public void ExportFrom_TrailingCommaInNames_ParsesSuccessfully()
    {
        var stmts = ParseProgram("export { a, b, } from \"./mod\";");
        Assert.IsType<ExportFromStmt>(Assert.Single(stmts));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // NEGATIVE — lone comma and double trailing comma must still error
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ArrayLiteral_LoneComma_IsParseError()
    {
        ParseExpectingError("let x = [,];");
    }

    [Fact]
    public void ArrayLiteral_DoubleTrailingComma_IsParseError()
    {
        ParseExpectingError("let x = [1,,];");
    }

    [Fact]
    public void DictLiteral_LoneComma_IsParseError()
    {
        ParseExpectingError("let x = {,};");
    }

    [Fact]
    public void DictLiteral_DoubleTrailingComma_IsParseError()
    {
        ParseExpectingError("let x = { a: 1,, };");
    }

    [Fact]
    public void CallArgs_LoneComma_IsParseError()
    {
        ParseExpectingError("f(,);");
    }

    [Fact]
    public void CallArgs_DoubleTrailingComma_IsParseError()
    {
        ParseExpectingError("f(1,,);");
    }

    [Fact]
    public void FnDecl_LoneComma_IsParseError()
    {
        ParseExpectingError("fn f(,) {}");
    }

    [Fact]
    public void FnDecl_DoubleTrailingComma_IsParseError()
    {
        ParseExpectingError("fn f(a,,) {}");
    }
}
