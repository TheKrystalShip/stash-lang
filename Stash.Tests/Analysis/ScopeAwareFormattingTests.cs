using Stash.Analysis;

namespace Stash.Tests.Analysis;

public class ScopeAwareFormattingTests
{
    private static string Format(string source) =>
        new StashFormatter(2, useTabs: false).Format(source);

    [Fact]
    public void NestedFunction_InFunctionBody_BlankLineAround()
    {
        var result = Format("fn outer() { let x = 1; fn inner() { return x; } let y = 2; }");
        Assert.Equal("fn outer() {\n  let x = 1;\n\n  fn inner() {\n    return x;\n  }\n\n  let y = 2;\n}\n", result);
    }

    [Fact]
    public void StructWithMethodInExtend_ScopesWork()
    {
        var result = Format("struct Foo { x: int }\nextend Foo { fn bar() { return self.x; } }");
        Assert.Equal("struct Foo {\n  x: int\n}\n\nextend Foo {\n  fn bar() {\n    return self.x;\n  }\n}\n", result);
    }

    [Fact]
    public void Lambda_BlockBody_FormatsCorrectly()
    {
        var result = Format("let f = (x: int) => { return x + 1; };");
        Assert.Equal("let f = (x: int) => {\n  return x + 1;\n};\n", result);
    }

    [Fact]
    public void Lambda_ExprBody_FormatsCorrectly()
    {
        var result = Format("let f = (x: int) => x + 1;");
        Assert.Equal("let f = (x: int) => x + 1;\n", result);
    }

    [Fact]
    public void TryCatch_FormatsCorrectly()
    {
        var result = Format("try{let x=1;}catch(e){io.println(e);}finally{io.println(\"done\");}");
        Assert.Equal("try {\n  let x = 1;\n} catch (e) {\n  io.println(e);\n} finally {\n  io.println(\"done\");\n}\n", result);
    }

    [Fact]
    public void Switch_CaseBodies_FormatsCorrectly()
    {
        var result = Format("switch (x) { case 1: { return \"one\"; } case 2: { return \"two\"; } default: { return \"other\"; } }");
        Assert.Equal("switch (x) {\n  case 1 : {\n    return \"one\";\n  }\n  case 2 : {\n    return \"two\";\n  }\n  default : {\n    return \"other\";\n  }\n}\n", result);
    }

    [Fact]
    public void Elevate_FormatsCorrectly()
    {
        var result = Format("elevate{let x=1;}");
        Assert.Equal("elevate {\n  let x = 1;\n}\n", result);
    }

    [Fact]
    public void ForIn_FormatsCorrectly()
    {
        var result = Format("for(let item in items){io.println(item);}");
        Assert.Equal("for (let item in items) {\n  io.println(item);\n}\n", result);
    }
}
