using Stash.Analysis;

namespace Stash.Tests.Analysis;

public class BraceRulesTests
{
    private static string Format(string source, FormatConfig? config = null) =>
        new StashFormatter(config ?? FormatConfig.Default).Format(source);

    [Fact]
    public void SingleLineBlock_Disabled_ByDefault()
    {
        var result = Format("if (true) { return 1; }");
        Assert.Equal("if (true) {\n  return 1;\n}\n", result);
    }

    [Fact]
    public void SingleLineBlock_Enabled_SingleStatement()
    {
        var config = new FormatConfig { SingleLineBlocks = true };
        var result = Format("if (true) { return 1; }", config);
        Assert.Equal("if (true) { return 1; }\n", result);
    }

    [Fact]
    public void SingleLineBlock_Enabled_MultiStatement_StaysMultiLine()
    {
        var config = new FormatConfig { SingleLineBlocks = true };
        var result = Format("if (true) { let x = 1; return x; }", config);
        Assert.Equal("if (true) {\n  let x = 1;\n  return x;\n}\n", result);
    }

    [Fact]
    public void BraceSpacing_SpaceBeforeOpenBrace()
    {
        var result = Format("fn foo(){}");
        Assert.Equal("fn foo() {\n}\n", result);
    }

    [Fact]
    public void BraceSpacing_IfCondition_SpaceBeforeBrace()
    {
        var result = Format("if(true){}");
        Assert.Equal("if (true) {\n}\n", result);
    }

    [Fact]
    public void BraceSpacing_WhileLoop_SpaceBeforeBrace()
    {
        var result = Format("while(true){}");
        Assert.Equal("while (true) {\n}\n", result);
    }
}
