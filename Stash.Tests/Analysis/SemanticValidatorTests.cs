using Stash.Lexing;
using Stash.Parsing;
using Stash.Lsp.Analysis;

namespace Stash.Tests.Analysis;

public class SemanticValidatorTests
{
    private static List<SemanticDiagnostic> Validate(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        var validator = new SemanticValidator(scopeTree);
        return validator.Validate(stmts);
    }

    [Fact]
    public void BreakOutsideLoop_ReportsError()
    {
        var diagnostics = Validate("break;");

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticLevel.Error, d.Level);
        Assert.Contains("'break' used outside of a loop.", d.Message);
    }

    [Fact]
    public void BreakInsideLoop_NoError()
    {
        var diagnostics = Validate("while (true) { break; }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ContinueOutsideLoop_ReportsError()
    {
        var diagnostics = Validate("continue;");

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticLevel.Error, d.Level);
        Assert.Contains("'continue' used outside of a loop.", d.Message);
    }

    [Fact]
    public void ContinueInsideLoop_NoError()
    {
        var diagnostics = Validate("for (let i in [1]) { continue; }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ReturnOutsideFunction_ReportsError()
    {
        var diagnostics = Validate("return 1;");

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticLevel.Error, d.Level);
        Assert.Contains("'return' used outside of a function.", d.Message);
    }

    [Fact]
    public void ReturnInsideFunction_NoError()
    {
        var diagnostics = Validate("fn foo() { return 1; }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ConstReassignment_ReportsError()
    {
        var diagnostics = Validate("const X = 1; X = 2;");

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Cannot reassign constant 'X'.") &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void LetReassignment_NoError()
    {
        var diagnostics = Validate("let x = 1; x = 2;");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void WrongArity_ReportsError()
    {
        var diagnostics = Validate("fn add(a, b) { return a + b; } add(1);");

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Expected 2 arguments but got 1.") &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void CorrectArity_NoError()
    {
        var diagnostics = Validate("fn add(a, b) { return a + b; } add(1, 2);");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ZeroArityFunction_WrongCall_ReportsError()
    {
        var diagnostics = Validate("fn greet() {} greet(1);");

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Expected 0 arguments but got 1.") &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void NestedBreak_InLoop_NoError()
    {
        // break inside if inside while — loopDepth is still 1, no error
        var diagnostics = Validate("while (true) { if (true) { break; } }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ReturnInsideNestedFunction_NoError()
    {
        var diagnostics = Validate("fn outer() { fn inner() { return 1; } }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void BreakInsideFunction_InsideLoop_NoError()
    {
        var diagnostics = Validate("fn foo() { while (true) { break; } }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UndefinedVariable_ReportsWarning()
    {
        var diagnostics = Validate("let x = y;");

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("'y' is not defined.") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void DefinedVariable_NoWarning()
    {
        var diagnostics = Validate("let x = 1; let y = x;");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ValidProgram_NoErrors()
    {
        var diagnostics = Validate("let x = 1; fn foo(a) { return a + x; } foo(2);");

        Assert.Empty(diagnostics);
    }

    // ── Type Hints ─────────────────────────────────────────────────────

    [Fact]
    public void TypeHintedVarDecl_ProducesNoErrors()
    {
        var diagnostics = Validate("let name: string = \"Alice\";");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TypeHintedConstDecl_ProducesNoErrors()
    {
        var diagnostics = Validate("const PI: float = 3.14;");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TypeHintedFnDecl_ProducesNoErrors()
    {
        var diagnostics = Validate("fn add(a: int, b: int) -> int { return a; }");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TypeHintedStructDecl_ProducesNoErrors()
    {
        var diagnostics = Validate("struct Server { host: string, port: int }");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TypeHintedForIn_ProducesNoErrors()
    {
        var diagnostics = Validate("let names = [\"a\"]; for (let item: string in names) { }");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MixedTypeHints_ProducesNoErrors()
    {
        var source = @"
            struct Config { name: string, value }
            let cfg: Config = null;
            const MAX: int = 100;
            fn process(item: string, count) -> bool {
                return true;
            }
        ";
        var diagnostics = Validate(source);
        Assert.Empty(diagnostics);
    }

    // ── Type Hint Validation ───────────────────────────────────────────

    [Fact]
    public void UnknownTypeHint_VarDecl_ReportsWarning()
    {
        var diagnostics = Validate("let x: arrary = [];");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Unknown type 'arrary'.") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void UnknownTypeHint_ConstDecl_ReportsWarning()
    {
        var diagnostics = Validate("const X: integr = 1;");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Unknown type 'integr'.") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void UnknownTypeHint_FnParam_ReportsWarning()
    {
        var diagnostics = Validate("fn add(a: intt, b: int) -> int { return a; }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Unknown type 'intt'.") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void UnknownTypeHint_FnReturn_ReportsWarning()
    {
        var diagnostics = Validate("fn greet() -> strng { return \"hi\"; }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Unknown type 'strng'.") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void UnknownTypeHint_StructField_ReportsWarning()
    {
        var diagnostics = Validate("struct Point { x: flot, y: float }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Unknown type 'flot'.") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void UnknownTypeHint_ForIn_ReportsWarning()
    {
        var diagnostics = Validate("let items = [1]; for (let item: strig in items) { }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Unknown type 'strig'.") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void ValidBuiltInTypes_NoWarnings()
    {
        var source = @"
            let a: string = """";
            let b: int = 1;
            let c: float = 1.0;
            let d: bool = true;
            let e: null = null;
            let f: array = [];
        ";
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("Unknown type"));
    }

    [Fact]
    public void UserDefinedStruct_AsTypeHint_NoWarning()
    {
        var diagnostics = Validate("struct Point { x, y }\nlet p: Point = null;");
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("Unknown type"));
    }

    [Fact]
    public void UserDefinedEnum_AsTypeHint_NoWarning()
    {
        var diagnostics = Validate("enum Color { Red, Green, Blue }\nlet c: Color = null;");
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("Unknown type"));
    }

    [Fact]
    public void MultipleUnknownTypes_ReportsAll()
    {
        var diagnostics = Validate("fn process(a: foo, b: bar) -> baz { return null; }");
        Assert.Contains(diagnostics, d => d.Message.Contains("Unknown type 'foo'."));
        Assert.Contains(diagnostics, d => d.Message.Contains("Unknown type 'bar'."));
        Assert.Contains(diagnostics, d => d.Message.Contains("Unknown type 'baz'."));
    }

    [Fact]
    public void NamespaceFunctionCall_WrongArgCount_ReportsError()
    {
        var diagnostics = Validate(@"
            let x = str.upper(""hello"", ""extra"");
        ");
        Assert.Contains(diagnostics, d => d.Message.Contains("arguments") && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void NamespaceFunctionCall_CorrectArgCount_NoError()
    {
        var diagnostics = Validate(@"
            let x = str.upper(""hello"");
        ");
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("arguments") && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void NamespaceFunctionCall_Variadic_NoArgCountError()
    {
        // str.padStart is variadic (arity -1), accepts 2 or 3 args — should not get arg count error
        var diagnostics = Validate(@"
            let x = str.padStart(""hi"", 5);
            let y = str.padStart(""hi"", 5, ""0"");
        ");
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("arguments") && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void DictTypeAnnotation_NoUnknownTypeError()
    {
        var diagnostics = Validate(@"
            let d: dict = dict.new();
        ");
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("Unknown type") && d.Level == DiagnosticLevel.Warning);
    }

    // ── Default parameter value tests ────────────────────────────────────────

    [Fact]
    public void DefaultParam_CorrectArity_MinArgs_NoError()
    {
        var diagnostics = Validate("fn f(a, b = 5) { return a + b; } f(1);");
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("arguments") && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void DefaultParam_CorrectArity_AllArgs_NoError()
    {
        var diagnostics = Validate("fn f(a, b = 5) { return a + b; } f(1, 2);");
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("arguments") && d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void DefaultParam_TooFewArgs_ReportsError()
    {
        var diagnostics = Validate("fn f(a, b = 5) { return a + b; } f();");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("arguments") &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void DefaultParam_TooManyArgs_ReportsError()
    {
        var diagnostics = Validate("fn f(a, b = 5) { return a + b; } f(1, 2, 3);");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("arguments") &&
            d.Level == DiagnosticLevel.Error);
    }

    [Fact]
    public void DefaultParam_AllOptional_ZeroArgs_NoError()
    {
        var diagnostics = Validate("fn f(a = 1, b = 2) {} f();");
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("arguments") && d.Level == DiagnosticLevel.Error);
    }
}
