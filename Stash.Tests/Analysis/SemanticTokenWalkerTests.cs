using Stash.Analysis;
using Stash.Lexing;
using Stash.Parsing;
using static Stash.Analysis.SemanticTokenConstants;

namespace Stash.Tests.Analysis;

/// <summary>
/// Verifies that <see cref="SemanticTokenWalker"/> emits the precise token types and
/// modifiers required by the syntax-highlighting taxonomy spec.
/// </summary>
public class SemanticTokenWalkerTests : AnalysisTestBase
{
    private static IReadOnlyDictionary<(int Line, int Col), (int Type, int Modifiers)> Classify(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var tree = collector.Collect(stmts);
        var validator = new SemanticValidator(tree);
        var diagnostics = validator.Validate(stmts);
        var result = new AnalysisResult(
            tokens, stmts, new List<string>(), new List<string>(),
            new List<Stash.Common.DiagnosticError>(),
            new List<Stash.Common.DiagnosticError>(),
            tree, diagnostics);
        var walker = new SemanticTokenWalker(result);
        walker.Walk(stmts);
        return walker.ClassifiedTokens;
    }

    private static (int Type, int Modifiers) TokenAt(
        IReadOnlyDictionary<(int Line, int Col), (int Type, int Modifiers)> map,
        int line, int col)
    {
        // Walker stores 0-based line/col.
        Assert.True(map.ContainsKey((line - 1, col - 1)),
            $"No semantic token at line {line} col {col}");
        return map[(line - 1, col - 1)];
    }

    // ── Decision 4: struct/enum distinguished from generic type ──

    [Fact]
    public void StructDeclaration_ClassifiesAsStruct_NotType()
    {
        var map = Classify("struct Point { x, y }");
        var (type, mods) = TokenAt(map, 1, 8);
        Assert.Equal(TokenTypeStruct, type);
        Assert.Equal(ModifierDeclaration, mods);
    }

    [Fact]
    public void EnumDeclaration_ClassifiesAsEnum_NotType()
    {
        var map = Classify("enum Color { Red, Green }");
        var (type, mods) = TokenAt(map, 1, 6);
        Assert.Equal(TokenTypeEnum, type);
        Assert.Equal(ModifierDeclaration, mods);
    }

    [Fact]
    public void StructTypeHint_ClassifiesAsStruct()
    {
        // line 2 col 8 = "p: Point" — Point starts after "fn f(p: ".
        var map = Classify("struct Point { x }\nfn f(p: Point) {}");
        var (type, _) = TokenAt(map, 2, 9);
        Assert.Equal(TokenTypeStruct, type);
    }

    [Fact]
    public void EnumTypeHint_ClassifiesAsEnum()
    {
        var map = Classify("enum Color { Red }\nfn f(c: Color) {}");
        var (type, _) = TokenAt(map, 2, 9);
        Assert.Equal(TokenTypeEnum, type);
    }

    // ── Decision 3: built-ins use defaultLibrary ──

    [Fact]
    public void BuiltInPrimitive_TypeHint_CarriesDefaultLibrary()
    {
        var map = Classify("let x: int = 1;");
        var (type, mods) = TokenAt(map, 1, 8);
        Assert.Equal(TokenTypeType, type);
        Assert.Equal(ModifierDefaultLibrary, mods);
    }

    [Fact]
    public void BuiltInFunction_CarriesDefaultLibrary()
    {
        var map = Classify("len([]);");
        var (type, mods) = TokenAt(map, 1, 1);
        Assert.Equal(TokenTypeFunction, type);
        Assert.Equal(ModifierDefaultLibrary, mods);
    }

    [Fact]
    public void BuiltInNamespace_CarriesDefaultLibrary()
    {
        // "io.println(1);" — io is a built-in namespace at col 1.
        var map = Classify("io.println(1);");
        var (type, mods) = TokenAt(map, 1, 1);
        Assert.Equal(TokenTypeNamespace, type);
        Assert.Equal(ModifierDefaultLibrary, mods);
    }

    [Fact]
    public void BuiltInNamespaceFunction_CarriesDefaultLibrary()
    {
        var map = Classify("io.println(1);");
        var (type, mods) = TokenAt(map, 1, 4);
        Assert.Equal(TokenTypeFunction, type);
        Assert.Equal(ModifierDefaultLibrary, mods);
    }

    [Fact]
    public void BuiltInErrorStruct_TypedCatch_ClassifiesAsStructDefaultLibrary()
    {
        var map = Classify("try {} catch (ValueError e) {}");
        // "ValueError" sits after "try {} catch (" — col 15.
        var (type, mods) = TokenAt(map, 1, 15);
        Assert.Equal(TokenTypeStruct, type);
        Assert.Equal(ModifierDefaultLibrary, mods);
    }

    // ── Decision 4: methods distinguished from functions ──

    [Fact]
    public void StructMethod_ClassifiesAsMethod_NotFunction()
    {
        var map = Classify("struct S { x\n  fn greet() {}\n}");
        // Line 2, col 6 → "fn greet" — greet at col 6.
        var (type, mods) = TokenAt(map, 2, 6);
        Assert.Equal(TokenTypeMethod, type);
        Assert.Equal(ModifierDeclaration, mods);
    }

    [Fact]
    public void UfcsCallOnString_ClassifiesAsMethodDefaultLibrary()
    {
        // "str.upper" exists; UFCS dispatch on a string value.
        var map = Classify("\"abc\".upper();");
        var (type, mods) = TokenAt(map, 1, 7);
        Assert.Equal(TokenTypeMethod, type);
        Assert.Equal(ModifierDefaultLibrary, mods);
    }

    // ── Decision 2: async modifier ──

    [Fact]
    public void AsyncFunction_CarriesAsyncModifier()
    {
        var map = Classify("async fn work() {}");
        // "async fn " = 9 chars; "work" at col 10.
        var (type, mods) = TokenAt(map, 1, 10);
        Assert.Equal(TokenTypeFunction, type);
        Assert.Equal(ModifierDeclaration | ModifierAsync, mods);
    }

    // ── Decision 2: readonly for constants and enum members ──

    [Fact]
    public void ConstDeclaration_CarriesReadonly()
    {
        var map = Classify("const PI = 3.14;");
        var (type, mods) = TokenAt(map, 1, 7);
        Assert.Equal(TokenTypeVariable, type);
        Assert.Equal(ModifierDeclaration | ModifierReadonly, mods);
    }

    [Fact]
    public void EnumMemberDeclaration_CarriesReadonly()
    {
        var map = Classify("enum Color { Red }");
        // "enum Color { " = 13, "Red" at col 14.
        var (type, mods) = TokenAt(map, 1, 14);
        Assert.Equal(TokenTypeEnumMember, type);
        Assert.Equal(ModifierDeclaration | ModifierReadonly, mods);
    }

    // ── Decision 5: self/attempt explicit policy ──

    [Fact]
    public void SelfIdentifier_ClassifiesAsReadonlyVariable()
    {
        var map = Classify("struct S { x\n  fn get() { self.x; }\n}");
        // Line 2, col 14: "self" inside "self.x" after "  fn get() { ".
        var (type, mods) = TokenAt(map, 2, 14);
        Assert.Equal(TokenTypeVariable, type);
        Assert.Equal(ModifierReadonly, mods);
    }

    // ── Decision 1: lexical material is NOT semantically emitted ──

    [Fact]
    public void StringLiteral_NotSemanticallyEmitted()
    {
        var map = Classify("let s = \"hello\";");
        // String starts at col 9. Should not be in classified map.
        Assert.False(map.ContainsKey((0, 8)),
            "String literals should be left to the grammar.");
    }

    [Fact]
    public void NumericLiteral_NotSemanticallyEmitted()
    {
        var map = Classify("let n = 42;");
        Assert.False(map.ContainsKey((0, 8)),
            "Numeric literals should be left to the grammar.");
    }
}
