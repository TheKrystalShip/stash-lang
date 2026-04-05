using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class DictLiteralTests
{
    private static Expr ParseExpr(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    private static object? Eval(string source)
    {
        string full = "return " + source + ";";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    // ── Category 1: Parser — AST Node Production ──────────────────────────────

    [Fact]
    public void Parse_EmptyDict_ReturnsDictLiteralExpr()
    {
        var result = ParseExpr("{}");
        var dict = Assert.IsType<DictLiteralExpr>(result);
        Assert.Empty(dict.Entries);
    }

    [Fact]
    public void Parse_SingleEntry_ReturnsDictLiteralExpr()
    {
        var result = ParseExpr("{ name: \"hello\" }");
        var dict = Assert.IsType<DictLiteralExpr>(result);
        Assert.Single(dict.Entries);
        Assert.Equal("name", dict.Entries[0].Key?.Lexeme);
    }

    [Fact]
    public void Parse_MultipleEntries_ReturnsDictLiteralExpr()
    {
        var result = ParseExpr("{ a: 1, b: 2, c: 3 }");
        var dict = Assert.IsType<DictLiteralExpr>(result);
        Assert.Equal(3, dict.Entries.Count);
    }

    // ── Category 2: Basic Dict Literal Evaluation ─────────────────────────────

    [Fact]
    public void EmptyDict_ReturnsDict()
    {
        var result = Eval("{}");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(0, dict.Count);
    }

    [Fact]
    public void SingleEntry_SetsValue()
    {
        Assert.Equal("hello", Run("let d = { name: \"hello\" }; let result = d.name;"));
    }

    [Fact]
    public void MultipleEntries_SetsAllValues()
    {
        Assert.Equal(1L, Run("let d = { x: 1, y: 2 }; let result = d.x;"));
    }

    [Fact]
    public void SecondValue_Accessible()
    {
        Assert.Equal(2L, Run("let d = { x: 1, y: 2 }; let result = d.y;"));
    }

    [Fact]
    public void MixedTypes_AllPreserved()
    {
        Assert.Equal(42L, Run("""let d = { name: "stash", count: 42, active: true }; let result = d.count;"""));
    }

    [Fact]
    public void StringValue_Accessible()
    {
        Assert.Equal("stash", Run("""let d = { name: "stash", count: 42, active: true }; let result = d.name;"""));
    }

    [Fact]
    public void BoolValue_Accessible()
    {
        Assert.Equal(true, Run("""let d = { name: "stash", count: 42, active: true }; let result = d.active;"""));
    }

    // ── Category 3: Operations on Dict Literals ───────────────────────────────

    [Fact]
    public void DictLiteral_TypeOfIsDict()
    {
        Assert.Equal("dict", Run("let d = { a: 1 }; let result = typeof(d);"));
    }

    [Fact]
    public void DictLiteral_LenReturnsEntryCount()
    {
        Assert.Equal(3L, Run("let d = { a: 1, b: 2, c: 3 }; let result = len(d);"));
    }

    [Fact]
    public void DictLiteral_DictHasFindsKey()
    {
        Assert.Equal(true, Run("""let d = { name: "test" }; let result = dict.has(d, "name");"""));
    }

    [Fact]
    public void DictLiteral_DictHasMissingKey()
    {
        Assert.Equal(false, Run("""let d = { name: "test" }; let result = dict.has(d, "missing");"""));
    }

    [Fact]
    public void DictLiteral_DictKeysReturnsAllKeys()
    {
        Assert.Equal(2L, Run("let d = { a: 1, b: 2 }; let result = len(dict.keys(d));"));
    }

    [Fact]
    public void DictLiteral_IndexAccessWorks()
    {
        Assert.Equal("test", Run("""let d = { name: "test" }; let result = d["name"];"""));
    }

    [Fact]
    public void DictLiteral_MutableAfterCreation()
    {
        Assert.Equal(2L, Run("""let d = { a: 1 }; d.b = 2; let result = d.b;"""));
    }

    // ── Category 4: Nested Dict Literals ─────────────────────────────────────

    [Fact]
    public void NestedDict_OuterAccessible()
    {
        Assert.Equal("dict", Run("""let d = { meta: { name: "app" } }; let result = typeof(d.meta);"""));
    }

    [Fact]
    public void NestedDict_InnerValueAccessible()
    {
        Assert.Equal("app", Run("""let d = { meta: { name: "app" } }; let result = d.meta.name;"""));
    }

    [Fact]
    public void DeeplyNested_ThreeLevels()
    {
        Assert.Equal(42L, Run("""let d = { a: { b: { c: 42 } } }; let result = d.a.b.c;"""));
    }

    // ── Category 5: Expressions as Values ────────────────────────────────────

    [Fact]
    public void ValueCanBeExpression()
    {
        Assert.Equal(15L, Run("let x = 10; let d = { val: x + 5 }; let result = d.val;"));
    }

    [Fact]
    public void ValueCanBeArrayLiteral()
    {
        Assert.Equal(3L, Run("let d = { items: [1, 2, 3] }; let result = len(d.items);"));
    }

    [Fact]
    public void ValueCanBeFunctionCall()
    {
        Assert.Equal("HELLO", Run("""let d = { upper: str.upper("hello") }; let result = d.upper;"""));
    }

    [Fact]
    public void ValueCanBeTernary()
    {
        Assert.Equal(1L, Run("let x = true; let d = { val: x ? 1 : 0 }; let result = d.val;"));
    }

    // ── Category 6: Dict Literal Does NOT Break Existing Syntax ──────────────

    [Fact]
    public void StructInit_StillWorks()
    {
        Assert.Equal(1L, Run("""
            struct Point { x, y }
            let p = Point { x: 1, y: 2 };
            let result = p.x;
        """));
    }

    [Fact]
    public void StructShorthand_StillWorks()
    {
        Assert.Equal(20L, Run("""
            struct Point { x, y }
            let x = 10;
            let y = 20;
            let p = Point { x, y };
            let result = p.y;
        """));
    }

    [Fact]
    public void BlockStmt_StillWorks()
    {
        Assert.Equal(42L, Run("""
            let result = 0;
            {
                result = 42;
            }
        """));
    }

    [Fact]
    public void IfBlock_StillWorks()
    {
        Assert.Equal(42L, Run("""
            let result = 0;
            if (true) {
                result = 42;
            }
        """));
    }

    [Fact]
    public void FunctionBlock_StillWorks()
    {
        Assert.Equal(3L, Run("""
            fn add(a, b) {
                return a + b;
            }
            let result = add(1, 2);
        """));
    }

    [Fact]
    public void ForLoop_StillWorks()
    {
        Assert.Equal(6L, Run("""
            let result = 0;
            for (let i in [1, 2, 3]) {
                result = result + i;
            }
        """));
    }

    [Fact]
    public void WhileLoop_StillWorks()
    {
        Assert.Equal(3L, Run("""
            let result = 0;
            let i = 0;
            while (i < 3) {
                result = result + 1;
                i = i + 1;
            }
        """));
    }

    [Fact]
    public void EmptyStructInit_StillWorks()
    {
        Assert.Equal("struct", Run("""
            struct Empty { }
            let e = Empty {};
            let result = typeof(e);
        """));
    }

    // ── Category 7: Dict Literal as Function Argument ─────────────────────────

    [Fact]
    public void DictAsArgument_PassedToFunction()
    {
        Assert.Equal(42L, Run("""fn getVal(d) { return d.x; } let result = getVal({ x: 42 });"""));
    }

    [Fact]
    public void DictInArrayLiteral()
    {
        Assert.Equal(2L, Run("let arr = [{ a: 1 }, { a: 2 }]; let result = arr[1].a;"));
    }

    // ── Category 8: Edge Cases ────────────────────────────────────────────────

    [Fact]
    public void DictLiteral_ForInIteration()
    {
        Assert.Equal(2L, Run("""
            let d = { a: 1, b: 2 };
            let result = 0;
            for (let key in d) {
                result = result + 1;
            }
        """));
    }
}
