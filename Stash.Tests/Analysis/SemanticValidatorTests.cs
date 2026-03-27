using Stash.Lexing;
using Stash.Parsing;
using Stash.Analysis;
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
        var diagnostics = Validate("for (let _ in [1]) { continue; }");

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
        var diagnostics = Validate("let x = 1; let y = x; io.println(y);");

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
        var diagnostics = Validate("let name: string = \"Alice\"; io.println(name);");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TypeHintedConstDecl_ProducesNoErrors()
    {
        var diagnostics = Validate("const PI: float = 3.14; io.println(PI);");
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
        var diagnostics = Validate("let names = [\"a\"]; for (let _: string in names) { }");
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
            io.println(cfg);
            io.println(MAX);
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

    private static List<SemanticDiagnostic> ValidateWithInference(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        TypeInferenceEngine.InferTypes(scopeTree, stmts);
        var validator = new SemanticValidator(scopeTree);
        return validator.Validate(stmts);
    }

    // ── Type Mismatch — Function Arguments ─────────────────────────────

    [Fact]
    public void TypedParam_WrongLiteralType_ReportsWarning()
    {
        var diagnostics = Validate(@"
            fn doSomething(value: int) {}
            doSomething(""hello"");
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("expects type 'int'") &&
            d.Message.Contains("got 'string'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_CorrectLiteralType_NoWarning()
    {
        var diagnostics = Validate(@"
            fn doSomething(value: int) {}
            doSomething(42);
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("expects type") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_TypedVariable_Mismatch_ReportsWarning()
    {
        var diagnostics = Validate(@"
            fn doSomething(value: int) -> bool { return value > 10; }
            let value: string = ""Hello"";
            doSomething(value);
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("expects type 'int'") &&
            d.Message.Contains("got 'string'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_TypedVariable_Match_NoWarning()
    {
        var diagnostics = Validate(@"
            fn doSomething(value: int) {}
            let x: int = 42;
            doSomething(x);
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("expects type") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_UntypedVariable_NoWarning()
    {
        var diagnostics = Validate(@"
            fn doSomething(value: int) {}
            let x = 42;
            doSomething(x);
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("expects type") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_UntypedVariable_WithInference_Mismatch_ReportsWarning()
    {
        var diagnostics = ValidateWithInference(@"
            fn doSomething(value: int) {}
            let x = ""hello"";
            doSomething(x);
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("expects type 'int'") &&
            d.Message.Contains("got 'string'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void UntypedParam_AnyArgType_NoWarning()
    {
        var diagnostics = Validate(@"
            fn doSomething(value) {}
            doSomething(""hello"");
            doSomething(42);
            doSomething(true);
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("expects type") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_NullArgument_NoWarning()
    {
        var diagnostics = Validate(@"
            fn doSomething(value: int) {}
            doSomething(null);
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("expects type") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_MultipleParams_OneMismatch_ReportsWarning()
    {
        var diagnostics = Validate(@"
            fn connect(host: string, port: int) {}
            connect(""localhost"", ""8080"");
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("'port'") &&
            d.Message.Contains("expects type 'int'") &&
            d.Message.Contains("got 'string'") &&
            d.Level == DiagnosticLevel.Warning);
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("'host'") &&
            d.Message.Contains("expects type") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_BoolMismatch_ReportsWarning()
    {
        var diagnostics = Validate(@"
            fn check(flag: bool) {}
            check(42);
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("expects type 'bool'") &&
            d.Message.Contains("got 'int'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_FloatMismatch_ReportsWarning()
    {
        var diagnostics = Validate(@"
            fn calculate(ratio: float) {}
            calculate(42);
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("expects type 'float'") &&
            d.Message.Contains("got 'int'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_StructType_Mismatch_ReportsWarning()
    {
        var diagnostics = Validate(@"
            struct Server { host, port }
            fn deploy(srv: Server) {}
            deploy(""not a server"");
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("expects type 'Server'") &&
            d.Message.Contains("got 'string'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedParam_StructType_Match_NoWarning()
    {
        var diagnostics = Validate(@"
            struct Server { host, port }
            fn deploy(srv: Server) {}
            deploy(Server { host: ""10.0.0.1"", port: 22 });
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("expects type") && d.Level == DiagnosticLevel.Warning);
    }

    // ── Type Mismatch — Variable Reassignment ──────────────────────────

    [Fact]
    public void TypedVar_Reassign_DifferentType_ReportsWarning()
    {
        var diagnostics = Validate(@"
            let value: string = ""Hello"";
            value = 20;
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Cannot assign value of type 'int'") &&
            d.Message.Contains("variable 'value'") &&
            d.Message.Contains("of type 'string'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedVar_Reassign_SameType_NoWarning()
    {
        var diagnostics = Validate(@"
            let value: string = ""Hello"";
            value = ""World"";
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("Cannot assign") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedVar_Reassign_Null_NoWarning()
    {
        var diagnostics = Validate(@"
            let value: string = ""Hello"";
            value = null;
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("Cannot assign") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void UntypedVar_Reassign_DifferentType_NoWarning()
    {
        var diagnostics = Validate(@"
            let value = ""Hello"";
            value = 20;
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("Cannot assign") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void UntypedVar_WithInference_Reassign_DifferentType_NoWarning()
    {
        var diagnostics = ValidateWithInference(@"
            let value = ""Hello"";
            value = 20;
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("Cannot assign") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedVar_IntToString_ReportsWarning()
    {
        var diagnostics = Validate(@"
            let count: int = 0;
            count = ""not a number"";
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Cannot assign value of type 'string'") &&
            d.Message.Contains("variable 'count'") &&
            d.Message.Contains("of type 'int'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedVar_BoolToInt_ReportsWarning()
    {
        var diagnostics = Validate(@"
            let flag: bool = true;
            flag = 0;
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Cannot assign value of type 'int'") &&
            d.Message.Contains("variable 'flag'") &&
            d.Message.Contains("of type 'bool'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    // ── Type Mismatch — Variable Initialization ────────────────────────

    [Fact]
    public void TypedVar_InitWrongType_ReportsWarning()
    {
        var diagnostics = Validate(@"let x: int = ""hello"";");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Variable 'x' is declared as 'int'") &&
            d.Message.Contains("initialized with 'string'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedVar_InitCorrectType_NoWarning()
    {
        var diagnostics = Validate(@"let x: int = 42;");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("declared as") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedVar_InitWithNull_NoWarning()
    {
        var diagnostics = Validate(@"let x: string = null;");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("declared as") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedConst_InitWrongType_ReportsWarning()
    {
        var diagnostics = Validate(@"const MAX: int = ""hello"";");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Constant 'MAX' is declared as 'int'") &&
            d.Message.Contains("initialized with 'string'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void TypedConst_InitCorrectType_NoWarning()
    {
        var diagnostics = Validate(@"const MAX: int = 100;");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("declared as") && d.Level == DiagnosticLevel.Warning);
    }

    // ── Combined Scenario from User Example ────────────────────────────

    [Fact]
    public void UserExample_TypedFnAndTypedVar_BothWarnings()
    {
        var diagnostics = Validate(@"
            fn doSomething(value: int) -> bool {
                return value > 10;
            }
            let value: string = ""Hello, World!"";
            doSomething(value);
            value = 20;
        ");
        // Should warn about function argument type mismatch
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("expects type 'int'") &&
            d.Message.Contains("got 'string'") &&
            d.Level == DiagnosticLevel.Warning);
        // Should warn about variable reassignment type mismatch
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Cannot assign value of type 'int'") &&
            d.Message.Contains("variable 'value'") &&
            d.Message.Contains("of type 'string'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    // ── Struct Field Assignment Type Checking ────────────────────────────

    [Fact]
    public void StructFieldAssign_TypeMismatch_ReportsWarning()
    {
        var diagnostics = ValidateWithInference(@"
            struct Person {
                name: string,
                age: int
            }
            let alice = Person { name: ""Alice"", age: 30 };
            alice.age = ""thirty"";
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Cannot assign value of type 'string'") &&
            d.Message.Contains("field 'age'") &&
            d.Message.Contains("of type 'int'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void StructFieldAssign_CorrectType_NoWarning()
    {
        var diagnostics = ValidateWithInference(@"
            struct Person {
                name: string,
                age: int
            }
            let alice = Person { name: ""Alice"", age: 30 };
            alice.age = 31;
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("Cannot assign") && d.Message.Contains("field 'age'"));
    }

    [Fact]
    public void StructFieldAssign_NullValue_NoWarning()
    {
        var diagnostics = ValidateWithInference(@"
            struct Person {
                name: string,
                age: int
            }
            let alice = Person { name: ""Alice"", age: 30 };
            alice.name = null;
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("Cannot assign") && d.Message.Contains("field 'name'"));
    }

    [Fact]
    public void StructFieldAssign_UntypedField_NoWarning()
    {
        var diagnostics = ValidateWithInference(@"
            struct Config {
                value
            }
            let cfg = Config { value: ""hello"" };
            cfg.value = 42;
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("Cannot assign") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void StructFieldAssign_MultipleFields_OnlyMismatchWarns()
    {
        var diagnostics = ValidateWithInference(@"
            struct Person {
                name: string,
                age: int
            }
            let alice = Person { name: ""Alice"", age: 30 };
            alice.name = ""Bob"";
            alice.age = ""thirty"";
        ");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("field 'name'") && d.Level == DiagnosticLevel.Warning);
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Cannot assign value of type 'string'") &&
            d.Message.Contains("field 'age'") &&
            d.Message.Contains("of type 'int'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void StructFieldAssign_TypedVariable_Mismatch_ReportsWarning()
    {
        var diagnostics = ValidateWithInference(@"
            struct Person {
                name: string,
                age: int
            }
            let alice = Person { name: ""Alice"", age: 30 };
            let newAge = ""thirty"";
            alice.age = newAge;
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Cannot assign value of type 'string'") &&
            d.Message.Contains("field 'age'") &&
            d.Message.Contains("of type 'int'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void StructFieldAssign_ExplicitlyTypedVar_ReportsWarning()
    {
        var diagnostics = Validate(@"
            struct Person {
                name: string,
                age: int
            }
            let alice: Person = Person { name: ""Alice"", age: 30 };
            alice.age = ""thirty"";
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("Cannot assign value of type 'string'") &&
            d.Message.Contains("field 'age'") &&
            d.Message.Contains("of type 'int'") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void DotExpr_TypeInference_ResolveFieldType()
    {
        // This verifies TypeInferenceEngine resolves dot access field types
        var diagnostics = ValidateWithInference(@"
            struct Point {
                x: int,
                y: int
            }
            fn process(value: string) {}
            let p = Point { x: 1, y: 2 };
            process(p.x);
        ");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("expects type 'string'") &&
            d.Message.Contains("got 'int'") &&
            d.Level == DiagnosticLevel.Warning);
    }
}
