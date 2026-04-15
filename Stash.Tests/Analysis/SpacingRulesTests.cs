using Stash.Analysis;

namespace Stash.Tests.Analysis;

public class SpacingRulesTests
{
    private static string Format(string source) =>
        new StashFormatter(2, useTabs: false).Format(source);

    // ── Top-level spacing ─────────────────────────────────

    [Fact]
    public void TopLevel_ConsecutiveImports_NoBlankLine()
    {
        var result = Format("import \"a.stash\" as a;\nimport \"b.stash\" as b;");
        Assert.Equal("import \"a.stash\" as a;\nimport \"b.stash\" as b;\n", result);
    }

    [Fact]
    public void TopLevel_ImportThenFunction_BlankLine()
    {
        var result = Format("import \"a.stash\" as a;\nfn foo() {}");
        Assert.Equal("import \"a.stash\" as a;\n\nfn foo() {\n}\n", result);
    }

    [Fact]
    public void TopLevel_ConsecutiveVars_NoBlankLine()
    {
        var result = Format("let x = 1;\nlet y = 2;");
        Assert.Equal("let x = 1;\nlet y = 2;\n", result);
    }

    [Fact]
    public void TopLevel_FunctionThenFunction_BlankLine()
    {
        var result = Format("fn foo() {}\nfn bar() {}");
        Assert.Equal("fn foo() {\n}\n\nfn bar() {\n}\n", result);
    }

    [Fact]
    public void TopLevel_VarThenFunction_BlankLine()
    {
        var result = Format("let x = 1;\nfn foo() {}");
        Assert.Equal("let x = 1;\n\nfn foo() {\n}\n", result);
    }

    [Fact]
    public void TopLevel_FunctionThenVar_BlankLine()
    {
        var result = Format("fn foo() {}\nlet x = 1;");
        Assert.Equal("fn foo() {\n}\n\nlet x = 1;\n", result);
    }

    [Fact]
    public void TopLevel_StructThenStruct_BlankLine()
    {
        var result = Format("struct A { x: int }\nstruct B { y: int }");
        Assert.Equal("struct A {\n  x: int\n}\n\nstruct B {\n  y: int\n}\n", result);
    }

    // ── Struct body spacing ───────────────────────────────

    [Fact]
    public void StructBody_FieldsThenMethod_BlankLine()
    {
        var result = Format("struct Foo { x: int, y: int fn bar() { return self.x; } }");
        Assert.Equal("struct Foo {\n  x: int,\n  y: int\n\n  fn bar() {\n    return self.x;\n  }\n}\n", result);
    }

    [Fact]
    public void StructBody_MethodThenMethod_BlankLine()
    {
        var result = Format("struct Foo { x: int fn bar() { return self.x; } fn baz() { return self.x + 1; } }");
        Assert.Equal("struct Foo {\n  x: int\n\n  fn bar() {\n    return self.x;\n  }\n\n  fn baz() {\n    return self.x + 1;\n  }\n}\n", result);
    }

    // ── Function body spacing ─────────────────────────────

    [Fact]
    public void FunctionBody_ConsecutiveStatements_NoBlankLine()
    {
        var result = Format("fn foo() { let x = 1; let y = 2; return x + y; }");
        Assert.Equal("fn foo() {\n  let x = 1;\n  let y = 2;\n  return x + y;\n}\n", result);
    }

    // ── Extend body spacing ───────────────────────────────

    [Fact]
    public void ExtendBody_MethodsThenMethod_BlankLine()
    {
        var result = Format("extend Foo { fn bar() { return 1; } fn baz() { return 2; } }");
        Assert.Equal("extend Foo {\n  fn bar() {\n    return 1;\n  }\n\n  fn baz() {\n    return 2;\n  }\n}\n", result);
    }

    // ── Enum body spacing ─────────────────────────────────

    [Fact]
    public void EnumBody_Members_NoBlankLine()
    {
        var result = Format("enum Color { Red, Green, Blue }");
        Assert.Equal("enum Color {\n  Red,\n  Green,\n  Blue\n}\n", result);
    }
}
