using Stash.Analysis;

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
    public void Format_StructWithMethod_BlankLineBeforeMethod()
    {
        var result = Format("struct Foo{x:int,y:int fn bar(){return self.x;}}");
        Assert.Equal("struct Foo {\n  x: int,\n  y: int\n\n  fn bar() {\n    return self.x;\n  }\n}\n", result);
    }

    [Fact]
    public void Format_StructWithMethod_TrailingComma_BlankLineBeforeMethod()
    {
        var result = Format("struct Foo{x:int,y:int, fn bar(){return self.x;}}");
        Assert.Equal("struct Foo {\n  x: int,\n  y: int,\n\n  fn bar() {\n    return self.x;\n  }\n}\n", result);
    }

    [Fact]
    public void Format_StructWithMultipleMethods_BlankLineBetween()
    {
        var result = Format("struct Foo{x:int fn bar(){return self.x;}fn baz(){return self.x+1;}}");
        Assert.Equal("struct Foo {\n  x: int\n\n  fn bar() {\n    return self.x;\n  }\n\n  fn baz() {\n    return self.x + 1;\n  }\n}\n", result);
    }

    [Fact]
    public void Format_StructWithSingleInterface_FormatsCorrectly()
    {
        var result = Format("struct  Foo :  IBar   {\n name:  string\n}");
        Assert.Equal("struct Foo : IBar {\n  name: string\n}\n", result);
    }

    [Fact]
    public void Format_StructWithMultipleInterfaces_FormatsCorrectly()
    {
        var result = Format("struct  Foo :  IBar ,  IBaz   {\n name:  string\n}");
        Assert.Equal("struct Foo : IBar, IBaz {\n  name: string\n}\n", result);
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
        Assert.Equal("import { foo, bar } from \"module.stash\";\n", result);
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
        Assert.Equal("let s = Target { host: \"a\", user: \"b\" };\n", result);
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
        var result = Format("let result=$(ping -c 1 ${host});");
        Assert.Equal("let result = $(ping -c 1 ${host});\n", result);
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
        Assert.Contains("struct Target {\n  host: string,\n  user: string,\n  role: string,\n  env: Environment", formatted);
        // Function without return type annotation
        Assert.Contains("fn check_host(host: string) {", formatted);
        // Uses 2-space indent
        Assert.Contains("  let result", formatted);
        // Another function present in the file
        Assert.Contains("fn deploy_to(target: Target, package: string) {", formatted);
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

    [Fact]
    public void Format_InlineComment_Preserved()
    {
        var result = Format("let x = 5; // important note\nlet y = 10;");
        Assert.Equal("let x = 5; // important note\n\nlet y = 10;\n", result);
    }

    [Fact]
    public void Format_CallbackLambda_ClosingBrace()
    {
        var result = Format("arr.map(items, (item) => {\nreturn item * 2;\n});");
        Assert.Equal("arr.map(items, (item) => {\n  return item * 2;\n});\n", result);
    }

    [Fact]
    public void Format_MultiLineArray_Preserved()
    {
        var result = Format("let arr = [\n  1,\n  2,\n  3\n];");
        Assert.Equal("let arr = [\n  1,\n  2,\n  3\n];\n", result);
    }

    [Fact]
    public void Format_MultiLineNestedArray_Preserved()
    {
        var result = Format("let users = [\n  [1, \"Alice\"],\n  [2, \"Bob\"]\n];");
        Assert.Equal("let users = [\n  [1, \"Alice\"],\n  [2, \"Bob\"]\n];\n", result);
    }

    [Fact]
    public void Format_SingleLineArray_StaysSingleLine()
    {
        var result = Format("let arr = [1, 2, 3];");
        Assert.Equal("let arr = [1, 2, 3];\n", result);
    }

    [Fact]
    public void Format_InterfaceDeclaration_FieldsOnly()
    {
        var result = Format("interface HasName{name,age:int}");
        Assert.Equal("interface HasName {\n  name,\n  age: int\n}\n", result);
    }

    [Fact]
    public void Format_InterfaceDeclaration_MethodsOnly()
    {
        var result = Format("interface Printable{toString()->string}");
        Assert.Equal("interface Printable {\n  toString() -> string\n}\n", result);
    }

    [Fact]
    public void Format_InterfaceDeclaration_FieldsAndMethods()
    {
        var result = Format("interface Shape{name:string,area()->float}");
        Assert.Equal("interface Shape {\n  name: string,\n  area() -> float\n}\n", result);
    }

    [Fact]
    public void Format_InterfaceDeclaration_MethodWithParams()
    {
        var result = Format("interface Calc{add(a:int,b:int)->int}");
        Assert.Equal("interface Calc {\n  add(a: int, b: int) -> int\n}\n", result);
    }

    [Fact]
    public void Format_InterfaceDeclaration_InterleavedFieldsAndMethods()
    {
        var result = Format("interface Mixed{x:int,toString()->string,y:float}");
        Assert.Equal("interface Mixed {\n  x: int,\n  toString() -> string,\n  y: float\n}\n", result);
    }

    [Fact]
    public void Format_IsExpr_BareIdentifier_FormatsCorrectly()
    {
        var result = Format("let result=x is int;");
        Assert.Equal("let result = x is int;\n", result);
    }

    [Fact]
    public void Format_IsExpr_BareStructName_FormatsCorrectly()
    {
        var result = Format("let result=p is Point;");
        Assert.Equal("let result = p is Point;\n", result);
    }

    [Fact]
    public void Format_IsExpr_ArrayIndex_FormatsCorrectly()
    {
        var result = Format("let result=item is types[0];");
        Assert.Equal("let result = item is types[0];\n", result);
    }

    [Fact]
    public void Format_IsExpr_FunctionCall_FormatsCorrectly()
    {
        var result = Format("let result=item is getType();");
        Assert.Equal("let result = item is getType();\n", result);
    }

    [Fact]
    public void Format_IsExpr_DotAccess_FormatsCorrectly()
    {
        var result = Format("let result=item is holder.myType;");
        Assert.Equal("let result = item is holder.myType;\n", result);
    }

    [Fact]
    public void Format_IsExpr_InCondition_FormatsCorrectly()
    {
        var result = Format("if(item is types[0]){io.println(\"yes\");}");
        Assert.Equal("if (item is types[0]) {\n  io.println(\"yes\");\n}\n", result);
    }

    [Fact]
    public void Format_ConsecutiveExpressionStatements_NoBlankLines()
    {
        var result = Format("io.println(\"a\");\nio.println(\"b\");\nio.println(\"c\");");
        Assert.Equal("io.println(\"a\");\nio.println(\"b\");\nio.println(\"c\");\n", result);
    }

    [Fact]
    public void Format_StructNoFields_MultipleMethods_NoBlankLineAfterBrace()
    {
        var result = Format("struct Foo{fn bar(){return 1;}fn baz(){return 2;}}");
        Assert.Equal("struct Foo {\n  fn bar() {\n    return 1;\n  }\n\n  fn baz() {\n    return 2;\n  }\n}\n", result);
    }

    [Fact]
    public void Format_ExtendBlock_NoBlankLineAfterBrace()
    {
        var result = Format("extend string{fn upper(){return self;}fn lower(){return self;}}");
        Assert.Equal("extend string {\n  fn upper() {\n    return self;\n  }\n\n  fn lower() {\n    return self;\n  }\n}\n", result);
    }

    [Fact]
    public void Format_DeclarationBetweenStatements_BlankLinesAroundDeclaration()
    {
        var result = Format("let x=1;\nfn foo(){return x;}\nlet y=2;");
        Assert.Equal("let x = 1;\n\nfn foo() {\n  return x;\n}\n\nlet y = 2;\n", result);
    }
}
