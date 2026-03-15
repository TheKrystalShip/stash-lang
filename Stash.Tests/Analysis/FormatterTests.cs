using System.IO;
using Stash.Lsp.Analysis;

namespace Stash.Tests.Analysis;

public class FormatterTests
{
    private static string Format(string source, int indentSize = 2) =>
        new StashFormatter(indentSize, useTabs: false).Format(source);

    [Fact]
    public void Format_VarDeclaration_CorrectSpacing()
    {
        var result = Format("let   x=5;");
        Assert.Equal("let x = 5;\n", result);
    }

    [Fact]
    public void Format_ConstWithTypeHint()
    {
        var result = Format("const   MAX_RETRIES:int=3;");
        Assert.Equal("const MAX_RETRIES: int = 3;\n", result);
    }

    [Fact]
    public void Format_FunctionDeclaration_TwoSpaceIndent()
    {
        var result = Format("fn foo(a:int,b:int)->int{\nreturn a+b;\n}");
        Assert.Equal("fn foo(a: int, b: int) -> int {\n  return a + b;\n}\n", result);
    }

    [Fact]
    public void Format_EnumDeclaration()
    {
        var result = Format("enum Color{Red,Green,Blue}");
        Assert.Equal("enum Color {\n  Red,\n  Green,\n  Blue\n}\n", result);
    }

    [Fact]
    public void Format_StructDeclaration()
    {
        var result = Format("struct Point{x:float,y:float}");
        Assert.Equal("struct Point {\n  x: float,\n  y: float\n}\n", result);
    }

    [Fact]
    public void Format_IfElse()
    {
        var result = Format("if(x>0){return true;}else{return false;}");
        Assert.Equal("if (x > 0) {\n  return true;\n} else {\n  return false;\n}\n", result);
    }

    [Fact]
    public void Format_IfElseIfElse()
    {
        var result = Format("if(a==1){x=1;}else if(a==2){x=2;}else{x=3;}");
        Assert.Equal("if (a == 1) {\n  x = 1;\n} else if (a == 2) {\n  x = 2;\n} else {\n  x = 3;\n}\n", result);
    }

    [Fact]
    public void Format_WhileLoop()
    {
        var result = Format("while(x<10){x++;}");
        Assert.Equal("while (x < 10) {\n  x++;\n}\n", result);
    }

    [Fact]
    public void Format_ForInLoop()
    {
        var result = Format("for(let item in arr){io.println(item);}");
        Assert.Equal("for (let item in arr) {\n  io.println(item);\n}\n", result);
    }

    [Fact]
    public void Format_SingleLineCommentPreserved()
    {
        var result = Format("// this is a comment\nlet x = 5;");
        Assert.Equal("// this is a comment\n\nlet x = 5;\n", result);
    }

    [Fact]
    public void Format_BlockCommentPreserved()
    {
        var result = Format("/* block comment */\nlet x = 5;");
        Assert.Equal("/* block comment */\nlet x = 5;\n", result);
    }

    [Fact]
    public void Format_ShebangPreserved()
    {
        var result = Format("#!/usr/bin/env stash\nlet x = 5;");
        Assert.Equal("#!/usr/bin/env stash\n\nlet x = 5;\n", result);
    }

    [Fact]
    public void Format_ImportAs()
    {
        var result = Format("import  \"lib/utils.stash\"  as  utils;");
        Assert.Equal("import \"lib/utils.stash\" as utils;\n", result);
    }

    [Fact]
    public void Format_ImportDestructuring()
    {
        var result = Format("import{foo,bar}from \"module.stash\";");
        Assert.Equal("import {foo, bar} from \"module.stash\";\n", result);
    }

    [Fact]
    public void Format_DotAccess_NoSpacing()
    {
        var result = Format("io.println(target.host);");
        Assert.Equal("io.println(target.host);\n", result);
    }

    [Fact]
    public void Format_BinaryOperators_Spacing()
    {
        var result = Format("let x=a+b*c-d/e%f;");
        Assert.Equal("let x = a + b * c - d / e % f;\n", result);
    }

    [Fact]
    public void Format_ComparisonAndLogical()
    {
        var result = Format("if(a==b&&c!=d||e<f){x=1;}");
        Assert.Equal("if (a == b && c != d || e < f) {\n  x = 1;\n}\n", result);
    }

    [Fact]
    public void Format_UnaryNot()
    {
        var result = Format("if(!valid){return;}");
        Assert.Equal("if (!valid) {\n  return;\n}\n", result);
    }

    [Fact]
    public void Format_UnaryMinus()
    {
        var result = Format("let x = -5;");
        Assert.Equal("let x = -5;\n", result);
    }

    [Fact]
    public void Format_PostfixIncrement()
    {
        var result = Format("x++;");
        Assert.Equal("x++;\n", result);
    }

    [Fact]
    public void Format_PrefixIncrement()
    {
        var result = Format("++x;");
        Assert.Equal("++x;\n", result);
    }

    [Fact]
    public void Format_StructInit_Inline()
    {
        var result = Format("let s = Target{host:\"a\",user:\"b\"};");
        Assert.Equal("let s = Target {host: \"a\", user: \"b\"};\n", result);
    }

    [Fact]
    public void Format_LambdaExpression()
    {
        var result = Format("let double=(x)=>x*2;");
        Assert.Equal("let double = (x) => x * 2;\n", result);
    }

    [Fact]
    public void Format_LambdaBlock()
    {
        var result = Format("let abs=(x)=>{if(x<0){return -x;}return x;};");
        Assert.Equal("let abs = (x) => {\n  if (x < 0) {\n    return -x;\n  }\n  return x;\n};\n", result);
    }

    [Fact]
    public void Format_ArrayLiteral()
    {
        var result = Format("let arr=[1,2,3];");
        Assert.Equal("let arr = [1, 2, 3];\n", result);
    }

    [Fact]
    public void Format_NestedBlocks_CorrectIndent()
    {
        var result = Format("fn outer(){if(true){if(true){x=1;}}}");
        Assert.Equal("fn outer() {\n  if (true) {\n    if (true) {\n      x = 1;\n    }\n  }\n}\n", result);
    }

    [Fact]
    public void Format_BlankLinesBetweenTopLevel()
    {
        var result = Format("const X:int=1;\nfn foo(){}\nfn bar(){}");
        Assert.Equal("const X: int = 1;\n\nfn foo() {\n}\n\nfn bar() {\n}\n", result);
    }

    [Fact]
    public void Format_ConsecutiveComments()
    {
        var result = Format("// line 1\n// line 2\nlet x = 5;");
        Assert.Equal("// line 1\n// line 2\n\nlet x = 5;\n", result);
    }

    [Fact]
    public void Format_TernaryOperator()
    {
        var result = Format("let x=a?b:c;");
        Assert.Equal("let x = a ? b : c;\n", result);
    }

    [Fact]
    public void Format_NullCoalescing()
    {
        var result = Format("let x=a??b;");
        Assert.Equal("let x = a ?? b;\n", result);
    }

    [Fact]
    public void Format_CustomIndentSize()
    {
        var result = Format("fn foo(){let x=1;}", indentSize: 4);
        Assert.Equal("fn foo() {\n    let x = 1;\n}\n", result);
    }

    [Fact]
    public void Format_ReturnTypeArrow()
    {
        var result = Format("fn add(a:int,b:int)->int{return a+b;}");
        Assert.Equal("fn add(a: int, b: int) -> int {\n  return a + b;\n}\n", result);
    }

    [Fact]
    public void Format_EmptyInput()
    {
        var result = Format("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Format_InterpolatedString()
    {
        var result = Format("let msg=$\"hello {name}\";");
        Assert.Equal("let msg = $\"hello {name}\";\n", result);
    }

    [Fact]
    public void Format_CommandLiteral()
    {
        var result = Format("let result=$(ping -c 1 {host});");
        Assert.Equal("let result = $(ping -c 1 {host});\n", result);
    }

    [Fact]
    public void Format_BreakContinue()
    {
        var result = Format("while(true){if(done){break;}continue;}");
        Assert.Equal("while (true) {\n  if (done) {\n    break;\n  }\n  continue;\n}\n", result);
    }

    [Fact]
    public void Format_IndexAccess()
    {
        var result = Format("let x=arr[0];");
        Assert.Equal("let x = arr[0];\n", result);
    }

    [Fact]
    public void Format_MultipleStatementsInFunction()
    {
        var result = Format("fn test(){let a=1;let b=2;return a+b;}");
        Assert.Equal("fn test() {\n  let a = 1;\n  let b = 2;\n  return a + b;\n}\n", result);
    }

    [Fact]
    public void Format_DeployScript_Idempotent()
    {
        // The deploy.stash example should format idempotently (formatting twice = same output)
        var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(repoRoot, "examples", "deploy.stash"));
        var formatter = new StashFormatter(indentSize: 2, useTabs: false);
        var formatted = formatter.Format(source);
        var reformatted = formatter.Format(formatted);
        Assert.Equal(formatted, reformatted);
    }

    [Fact]
    public void Format_DeployScript_PreservesStructure()
    {
        // The formatted deploy.stash should contain key structural elements
        var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(repoRoot, "examples", "deploy.stash"));
        var formatter = new StashFormatter(indentSize: 2, useTabs: false);
        var formatted = formatter.Format(source);

        // Shebang preserved
        Assert.StartsWith("#!/usr/bin/env stash", formatted);
        // Import preserved
        Assert.Contains("import \"lib/utils.stash\" as utils;", formatted);
        // Enum formatted with members on separate lines
        Assert.Contains("enum DeployResult {\n  Success,\n  Failed,\n  Skipped\n}", formatted);
        // Struct formatted with fields on separate lines
        Assert.Contains("struct Target {\n  host: string,\n  user: string,\n  path: string\n}", formatted);
        // Function with return type
        Assert.Contains("fn check_host(host: string) -> bool {", formatted);
        // Uses 2-space indent
        Assert.Contains("  let result", formatted);
        // If-else formatting
        Assert.Contains("} else if", formatted);
    }

    [Fact]
    public void Format_FunctionDeclaration_WithDefaultValue()
    {
        var result = Format("fn greet(name,greeting=\"Hello\") {}");
        Assert.Equal("fn greet(name, greeting = \"Hello\") {\n}\n", result);
    }

    [Fact]
    public void Format_FunctionDeclaration_TypedWithDefaultValue()
    {
        var result = Format("fn connect(host:string,port:int=8080) {}");
        Assert.Equal("fn connect(host: string, port: int = 8080) {\n}\n", result);
    }
}
