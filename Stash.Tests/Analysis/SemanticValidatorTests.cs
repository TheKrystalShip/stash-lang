using Stash.Lexing;
using Stash.Parsing;
using Stash.Analysis;
using Stash.Stdlib;

namespace Stash.Tests.Analysis;

public class SemanticValidatorTests : AnalysisTestBase
{
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
        // break inside if inside while — loopDepth is still 1, no SA0101 error
        var diagnostics = Validate("while (true) { if (true) { break; } }");

        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
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
        var diagnostics = Validate("const x = 1; const y = x; io.println(y);");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ValidProgram_NoErrors()
    {
        var diagnostics = Validate("const x = 1; fn foo(a) { return a + x; } foo(2);");

        Assert.Empty(diagnostics);
    }

    // ── Type Hints ─────────────────────────────────────────────────────

    [Fact]
    public void TypeHintedVarDecl_ProducesNoErrors()
    {
        var diagnostics = Validate("let name: string = \"Alice\"; name = \"Bob\"; io.println(name);");
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
        var diagnostics = Validate("fn add(a: int, b: int) -> int { return a + b; }");
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
        var diagnostics = Validate("const names = [\"a\"]; for (let _: string in names) { io.println(_); }");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MixedTypeHints_ProducesNoErrors()
    {
        var source = @"
            struct Config { name: string, value }
            let cfg: Config = null;
            const MAX: int = 100;
            fn runProcess(item: string, count) -> bool {
                io.println(item);
                io.println(count);
                return true;
            }
            cfg = null;
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

    [Fact]
    public void Interface_ValidTypeHints_NoDiagnostics()
    {
        var diagnostics = Validate("interface Shape { area() -> float, name: string }");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Interface_InvalidTypeHint_ReportsDiagnostic()
    {
        var diagnostics = Validate("interface Shape { name: NonexistentType }");
        Assert.Contains(diagnostics, d => d.Message.Contains("NonexistentType"));
    }

    [Fact]
    public void Interface_InvalidMethodReturnType_ReportsDiagnostic()
    {
        var diagnostics = Validate("interface Shape { area() -> UnknownType }");
        Assert.Contains(diagnostics, d => d.Message.Contains("UnknownType"));
    }

    [Fact]
    public void Interface_InvalidParamType_ReportsDiagnostic()
    {
        var diagnostics = Validate("interface Calc { add(a: FakeType, b: int) }");
        Assert.Contains(diagnostics, d => d.Message.Contains("FakeType"));
    }

    [Fact]
    public void Interface_UsedAsTypeHint_NoDiagnostic()
    {
        var diagnostics = Validate("interface Printable { toString() }\nfn process(item: Printable) { return item; }");
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("Printable") || d.Message.Contains("Unknown type"));
    }

    [Fact]
    public void Retry_ShellOnlyBodyWithoutUntil_ReportsWarning()
    {
        var diagnostics = Validate("retry (3) { $(echo hello); }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("shell commands") &&
            d.Message.Contains("until") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void Retry_ShellBodyWithUntil_NoShellWarning()
    {
        var diagnostics = Validate("retry (3) until (r) => r.exitCode == 0 { $(echo hello); }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("shell commands"));
    }

    [Fact]
    public void Retry_StrictCommandBody_NoShellWarning()
    {
        var diagnostics = Validate("retry (3) { $!(echo hello); }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("shell commands"));
    }

    [Fact]
    public void Retry_MixedBody_NoShellWarning()
    {
        var diagnostics = Validate(@"retry (3) { $(echo hello); throw ""fail""; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("shell commands"));
    }

    [Fact]
    public void Retry_MaxAttemptsOne_ReportsHint()
    {
        var diagnostics = Validate(@"retry (1) { throw ""fail""; }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("1 attempt") &&
            d.Message.Contains("never retry") &&
            d.Level == DiagnosticLevel.Information);
    }

    [Fact]
    public void Retry_MaxAttemptsThree_NoHint()
    {
        var diagnostics = Validate(@"retry (3) { throw ""fail""; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("never retry"));
    }

    [Fact]
    public void Retry_UntilLambda_NoCallableWarning()
    {
        var diagnostics = Validate("retry (3) until (r) => true { 1; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("callable"));
    }

    [Fact]
    public void Retry_UntilIdentifier_NoCallableWarning()
    {
        var diagnostics = Validate("fn check(r) { return true; } retry (3) until check { 1; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("callable"));
    }

    [Fact]
    public void Retry_UntilNonCallable_ReportsWarning()
    {
        var diagnostics = Validate("retry (3) until 42 { 1; }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("callable") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void Retry_BackoffWithoutDelay_ReportsHint()
    {
        var diagnostics = Validate(@"retry (3, backoff: Backoff.Exponential) { throw ""fail""; }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("backoff") &&
            d.Message.Contains("delay") &&
            d.Level == DiagnosticLevel.Information);
    }

    [Fact]
    public void Retry_BackoffWithDelay_NoHint()
    {
        var diagnostics = Validate(@"retry (3, delay: 1s, backoff: Backoff.Exponential) { throw ""fail""; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("backoff") && d.Message.Contains("delay"));
    }

    [Fact]
    public void Retry_OnWithIdentifierArray_NoWarning()
    {
        var diagnostics = Validate(@"retry (3, on: [NetworkError, TimeoutError]) { throw ""fail""; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("'on'") && d.Message.Contains("error type"));
    }

    [Fact]
    public void Retry_OnWithNonArray_ReportsWarning()
    {
        var diagnostics = Validate(@"retry (3, on: 42) { throw ""fail""; }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("'on'") &&
            d.Message.Contains("array") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void Retry_BackoffWithZeroDelay_ReportsHint()
    {
        var diagnostics = Validate(@"retry (3, delay: 0s, backoff: Backoff.Exponential) { throw ""fail""; }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("backoff") &&
            d.Message.Contains("delay") &&
            d.Level == DiagnosticLevel.Information);
    }

    [Fact]
    public void Retry_UntilDotExpr_NoCallableWarning()
    {
        var diagnostics = Validate("fn check(r) { return true; } let obj = { check: check }; retry (3) until obj.check { 1; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("callable"));
    }

    [Fact]
    public void Retry_UntilCallExpr_NoCallableWarning()
    {
        var diagnostics = Validate("fn getChecker() { return (r) => true; } retry (3) until getChecker() { 1; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("callable"));
    }

    [Fact]
    public void Retry_OnWithStringLiteralArray_NoWarning()
    {
        var diagnostics = Validate(@"retry (3, on: [""NetworkError"", ""TimeoutError""]) { throw ""fail""; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("'on'") && d.Message.Contains("error type"));
    }

    [Fact]
    public void Retry_OnWithVariableReference_NoWarning()
    {
        var diagnostics = Validate(@"let errors = [""NetworkError""]; retry (3, on: errors) { throw ""fail""; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("'on'") && d.Message.Contains("error type"));
    }

    [Fact]
    public void Retry_PipedCommandBodyWithoutUntil_ReportsWarning()
    {
        var diagnostics = Validate("retry (3) { $(echo hello) | $(grep world); }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("shell commands") &&
            d.Message.Contains("until") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void Retry_MaxAttemptsZero_ReportsWarning()
    {
        var diagnostics = Validate(@"retry (0) { throw ""fail""; }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("0 attempts") &&
            d.Message.Contains("never execute") &&
            d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void Retry_MaxAttemptsZero_NoOneAttemptHint()
    {
        var diagnostics = Validate(@"retry (0) { throw ""fail""; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("1 attempt"));
    }

    [Fact]
    public void Retry_NoThrowableBody_ReportsHint()
    {
        var diagnostics = Validate("retry (3) { let x = 5 + 3; x; }");
        Assert.Contains(diagnostics, d =>
            d.Message.Contains("no operations that can throw") &&
            d.Level == DiagnosticLevel.Information);
    }

    [Fact]
    public void Retry_BodyWithFunctionCall_NoThrowableHint()
    {
        var diagnostics = Validate(@"retry (3) { throw ""fail""; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("no operations that can throw"));
    }

    [Fact]
    public void Retry_BodyWithDotAccess_NoThrowableHint()
    {
        var diagnostics = Validate("let obj = {}; retry (3) { obj.field; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("no operations that can throw"));
    }

    [Fact]
    public void Retry_NoThrowableBodyWithUntil_NoHint()
    {
        var diagnostics = Validate("retry (3) until (r) => r == 8 { let x = 5 + 3; x; }");
        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("no operations that can throw"));
    }

    // =========================================================================
    // SA0160–SA0163: typed catch / bare rethrow diagnostics
    // =========================================================================

    [Fact]
    public void SA0160_BareThrow_OutsideCatch_ReportsDiagnostic()
    {
        // throw; is only valid inside a catch block
        var diagnostics = Validate("throw;");
        Assert.Contains(diagnostics, d => d.Code == "SA0160");
    }

    [Fact]
    public void SA0160_BareThrow_InsideCatch_NoError()
    {
        // throw; inside a catch block is valid
        var diagnostics = Validate(@"
            try {
                throw ""x"";
            } catch (e) {
                throw;
            }
        ");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0160");
    }

    [Fact]
    public void SA0161_TypedClauseAfterCatchAll_ReportsDiagnostic()
    {
        // catch (e) followed by catch (TypeError e) — typed clause is unreachable
        var diagnostics = Validate(@"
            try {
                throw ""x"";
            } catch (e) {
                // catch-all
            } catch (TypeError e) {
                // unreachable
            }
        ");
        Assert.Contains(diagnostics, d => d.Code == "SA0161");
    }

    [Fact]
    public void SA0161_ErrorKeywordCatchAll_ThenTyped_ReportsDiagnostic()
    {
        // catch (Error e) followed by catch (ParseError e) — typed clause is unreachable
        var diagnostics = Validate(@"
            try {
                throw ""x"";
            } catch (Error e) {
                // catch-all via Error keyword
            } catch (ParseError e) {
                // unreachable
            }
        ");
        Assert.Contains(diagnostics, d => d.Code == "SA0161");
    }

    [Fact]
    public void SA0161_TypedBeforeCatchAll_NoError()
    {
        // Typed clause before catch-all is correct ordering — no diagnostic
        var diagnostics = Validate(@"
            try {
                throw ""x"";
            } catch (TypeError e) {
                // typed first
            } catch (e) {
                // catch-all last
            }
        ");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0161");
    }

    [Fact]
    public void SA0162_DuplicateCatchAll_ReportsDiagnostic()
    {
        // Two catch-all clauses — second is unreachable
        var diagnostics = Validate(@"
            try {
                throw ""x"";
            } catch (e) {
                // first catch-all
            } catch (e) {
                // second catch-all — unreachable
            }
        ");
        Assert.Contains(diagnostics, d => d.Code == "SA0162");
    }

    [Fact]
    public void SA0162_SingleCatchAll_NoError()
    {
        // A single catch-all is fine
        var diagnostics = Validate(@"
            try {
                throw ""x"";
            } catch (e) {
                // only catch-all
            }
        ");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0162");
    }

    [Fact]
    public void SA0163_CatchingRuntimeErrorByName_ReportsDiagnostic()
    {
        // Explicitly catching the base RuntimeError type is discouraged
        var diagnostics = Validate(@"
            try {
                throw ""x"";
            } catch (RuntimeError e) {
                // catching by base name
            }
        ");
        Assert.Contains(diagnostics, d => d.Code == "SA0163");
    }

    [Fact]
    public void SA0163_CatchingSpecificErrorType_NoWarning()
    {
        // Catching a specific typed error is correct — no SA0163
        var diagnostics = Validate(@"
            try {
                throw ""x"";
            } catch (TypeError e) {
                // specific type — fine
            }
        ");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0163");
    }
}
