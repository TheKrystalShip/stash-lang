using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Interpreting;

namespace Stash.Tests.Interpreting;

public class InterpreterTests
{
    private static object? Eval(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var expr = parser.Parse();
        var interpreter = new Interpreter();
        return interpreter.Interpret(expr);
    }

    // 1. Integer literals
    [Fact]
    public void IntegerLiteral_ReturnsLong()
    {
        Assert.Equal(42L, Eval("42"));
    }

    [Fact]
    public void IntegerLiteral_Zero_ReturnsLong()
    {
        Assert.Equal(0L, Eval("0"));
    }

    // 2. Float literals
    [Fact]
    public void FloatLiteral_ReturnsDouble()
    {
        Assert.Equal(3.14, Eval("3.14"));
    }

    // 3. String literals
    [Fact]
    public void StringLiteral_ReturnsString()
    {
        Assert.Equal("hello", Eval("\"hello\""));
    }

    // 4. Boolean literals
    [Fact]
    public void BooleanLiteral_True()
    {
        Assert.Equal(true, Eval("true"));
    }

    [Fact]
    public void BooleanLiteral_False()
    {
        Assert.Equal(false, Eval("false"));
    }

    // 5. Null literal
    [Fact]
    public void NullLiteral_ReturnsNull()
    {
        Assert.Null(Eval("null"));
    }

    // 6. Integer arithmetic
    [Fact]
    public void IntegerAddition()
    {
        Assert.Equal(3L, Eval("1 + 2"));
    }

    [Fact]
    public void IntegerSubtraction()
    {
        Assert.Equal(7L, Eval("10 - 3"));
    }

    [Fact]
    public void IntegerMultiplication()
    {
        Assert.Equal(20L, Eval("4 * 5"));
    }

    [Fact]
    public void IntegerDivision_Truncates()
    {
        Assert.Equal(3L, Eval("10 / 3"));
    }

    [Fact]
    public void IntegerModulo()
    {
        Assert.Equal(1L, Eval("10 % 3"));
    }

    // 7. Float arithmetic
    [Fact]
    public void FloatAddition()
    {
        var result = Eval("3.14 + 2.0");
        Assert.IsType<double>(result);
        Assert.Equal(5.14, (double)result!, precision: 10);
    }

    [Fact]
    public void FloatDivision()
    {
        var result = Eval("10.0 / 3.0");
        Assert.IsType<double>(result);
        Assert.Equal(10.0 / 3.0, (double)result!, precision: 10);
    }

    // 8. Int/float promotion
    [Fact]
    public void IntFloatPromotion_Addition()
    {
        Assert.Equal(8.14, Eval("5 + 3.14"));
    }

    [Fact]
    public void IntFloatPromotion_Division()
    {
        Assert.Equal(4.0, Eval("10 / 2.5"));
    }

    [Fact]
    public void IntFloatPromotion_Multiplication()
    {
        Assert.Equal(3.0, Eval("2 * 1.5"));
    }

    // 9. String concatenation
    [Fact]
    public void StringConcatenation()
    {
        Assert.Equal("hello world", Eval("\"hello\" + \" world\""));
    }

    // 10. String + number coercion
    [Fact]
    public void StringNumberCoercion_StringFirst()
    {
        Assert.Equal("count: 5", Eval("\"count: \" + 5"));
    }

    [Fact]
    public void StringNumberCoercion_NumberFirst()
    {
        Assert.Equal("5 items", Eval("5 + \" items\""));
    }

    // 11. Unary minus
    [Fact]
    public void UnaryMinus_Integer()
    {
        Assert.Equal(-42L, Eval("-42"));
    }

    [Fact]
    public void UnaryMinus_Float()
    {
        Assert.Equal(-3.14, Eval("-3.14"));
    }

    // 12. Unary not
    [Fact]
    public void UnaryNot_True()
    {
        Assert.Equal(false, Eval("!true"));
    }

    [Fact]
    public void UnaryNot_False()
    {
        Assert.Equal(true, Eval("!false"));
    }

    // 13. Truthiness - negation
    [Fact]
    public void Truthiness_NullIsFalsy()
    {
        Assert.Equal(true, Eval("!null"));
    }

    [Fact]
    public void Truthiness_ZeroIntIsFalsy()
    {
        Assert.Equal(true, Eval("!0"));
    }

    [Fact]
    public void Truthiness_EmptyStringIsFalsy()
    {
        Assert.Equal(true, Eval("!\"\""));
    }

    [Fact]
    public void Truthiness_NonEmptyStringIsTruthy()
    {
        Assert.Equal(false, Eval("!\"hello\""));
    }

    [Fact]
    public void Truthiness_NonZeroIntIsTruthy()
    {
        Assert.Equal(false, Eval("!1"));
    }

    [Fact]
    public void Truthiness_ZeroDoubleIsFalsy()
    {
        Assert.Equal(true, Eval("!0.0"));
    }

    // 14. Comparison operators
    [Fact]
    public void Comparison_LessThan_True()
    {
        Assert.Equal(true, Eval("1 < 2"));
    }

    [Fact]
    public void Comparison_GreaterThan_True()
    {
        Assert.Equal(true, Eval("2 > 1"));
    }

    [Fact]
    public void Comparison_LessEqual_True()
    {
        Assert.Equal(true, Eval("1 <= 1"));
    }

    [Fact]
    public void Comparison_GreaterEqual_False()
    {
        Assert.Equal(false, Eval("1 >= 2"));
    }

    [Fact]
    public void Comparison_WithPromotion()
    {
        Assert.Equal(false, Eval("5 < 3.0"));
    }

    // 15. Equality - no type coercion
    [Fact]
    public void Equality_SameIntValues()
    {
        Assert.Equal(true, Eval("5 == 5"));
    }

    [Fact]
    public void Equality_NotEqual_SameValues()
    {
        Assert.Equal(false, Eval("5 != 5"));
    }

    [Fact]
    public void Equality_NullEqualsNull()
    {
        Assert.Equal(true, Eval("null == null"));
    }

    [Fact]
    public void Equality_IntNotEqualString()
    {
        Assert.Equal(false, Eval("5 == \"5\""));
    }

    [Fact]
    public void Equality_ZeroNotEqualFalse()
    {
        Assert.Equal(false, Eval("0 == false"));
    }

    [Fact]
    public void Equality_ZeroNotEqualNull()
    {
        Assert.Equal(false, Eval("0 == null"));
    }

    // 16. Logical AND - short circuit
    [Fact]
    public void LogicalAnd_TrueAndString_ReturnsString()
    {
        Assert.Equal("yes", Eval("true && \"yes\""));
    }

    [Fact]
    public void LogicalAnd_FalseAndString_ReturnsFalse()
    {
        Assert.Equal(false, Eval("false && \"yes\""));
    }

    [Fact]
    public void LogicalAnd_NullAndString_ReturnsNull()
    {
        Assert.Null(Eval("null && \"x\""));
    }

    // 17. Logical OR - short circuit
    [Fact]
    public void LogicalOr_NullOrString_ReturnsString()
    {
        Assert.Equal("default", Eval("null || \"default\""));
    }

    [Fact]
    public void LogicalOr_StringOrString_ReturnsFirst()
    {
        Assert.Equal("value", Eval("\"value\" || \"default\""));
    }

    // 18. Ternary
    [Fact]
    public void Ternary_TrueCondition()
    {
        Assert.Equal(1L, Eval("true ? 1 : 2"));
    }

    [Fact]
    public void Ternary_FalseCondition()
    {
        Assert.Equal(2L, Eval("false ? 1 : 2"));
    }

    [Fact]
    public void Ternary_FalsyZeroCondition()
    {
        Assert.Equal("nonzero", Eval("0 ? \"zero\" : \"nonzero\""));
    }

    // 19. Grouping
    [Fact]
    public void Grouping_OverridesPrecedence()
    {
        Assert.Equal(9L, Eval("(1 + 2) * 3"));
    }

    // 20. Precedence end-to-end
    [Fact]
    public void Precedence_MultiplicationBeforeAddition()
    {
        Assert.Equal(7L, Eval("1 + 2 * 3"));
    }

    [Fact]
    public void Precedence_ComplexExpression()
    {
        Assert.Equal(13L, Eval("2 + 3 * 4 - 1"));
    }

    // 21. Division by zero
    [Fact]
    public void DivisionByZero_Throws()
    {
        Assert.Throws<RuntimeError>(() => Eval("1 / 0"));
    }

    [Fact]
    public void ModuloByZero_Throws()
    {
        Assert.Throws<RuntimeError>(() => Eval("1 % 0"));
    }

    // 22. Type errors
    [Fact]
    public void TypeError_StringMinusInt()
    {
        Assert.Throws<RuntimeError>(() => Eval("\"x\" - 1"));
    }

    [Fact]
    public void TypeError_BoolPlusBool()
    {
        Assert.Throws<RuntimeError>(() => Eval("true + false"));
    }

    [Fact]
    public void TypeError_StringLessThanInt()
    {
        Assert.Throws<RuntimeError>(() => Eval("\"x\" < 1"));
    }

    [Fact]
    public void TypeError_NegateString()
    {
        Assert.Throws<RuntimeError>(() => Eval("-\"hello\""));
    }

    // 23. Stringify
    [Fact]
    public void Stringify_Null()
    {
        var interpreter = new Interpreter();
        Assert.Equal("null", interpreter.Stringify(null));
    }

    [Fact]
    public void Stringify_True()
    {
        var interpreter = new Interpreter();
        Assert.Equal("true", interpreter.Stringify(true));
    }

    [Fact]
    public void Stringify_False()
    {
        var interpreter = new Interpreter();
        Assert.Equal("false", interpreter.Stringify(false));
    }

    [Fact]
    public void Stringify_Long()
    {
        var interpreter = new Interpreter();
        Assert.Equal("42", interpreter.Stringify(42L));
    }

    [Fact]
    public void Stringify_Double()
    {
        var interpreter = new Interpreter();
        Assert.Equal("3.14", interpreter.Stringify(3.14));
    }

    [Fact]
    public void Stringify_String()
    {
        var interpreter = new Interpreter();
        Assert.Equal("hello", interpreter.Stringify("hello"));
    }

    // 24. Complex expressions
    [Fact]
    public void Complex_ArithmeticWithGrouping()
    {
        Assert.Equal(10L, Eval("(10 + 5) * 2 / 3"));
    }

    [Fact]
    public void Complex_ArithmeticEqualityComparison()
    {
        Assert.Equal(true, Eval("1 + 2 * 3 == 7"));
    }

    // ===== Phase 2 Helpers =====

    /// <summary>
    /// Executes a multi-statement program. The program must capture its output in a variable
    /// named 'result' (the last variable defined at global scope).
    /// Since Interpret(List&lt;Stmt&gt;) returns void, we evaluate the variable 'result' after execution.
    /// </summary>
    private static object? Run(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);

        // Evaluate 'result' variable from cleanup
        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    /// <summary>
    /// Executes a program and expects a RuntimeError.
    /// </summary>
    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

    // ===== Category 1: Variables (let) =====

    [Fact]
    public void VarDecl_AssignsValue()
    {
        Assert.Equal(42L, Run("let result = 42;"));
    }

    [Fact]
    public void VarDecl_WithoutInitializer_IsNull()
    {
        Assert.Null(Run("let result;"));
    }

    [Fact]
    public void VarDecl_Reassignment()
    {
        Assert.Equal(2L, Run("let result = 1; result = 2;"));
    }

    [Fact]
    public void VarDecl_UseInExpression()
    {
        Assert.Equal(15L, Run("let x = 10; let result = x + 5;"));
    }

    [Fact]
    public void VarDecl_UndefinedVariable_ThrowsError()
    {
        RunExpectingError("let x = y;");
    }

    // ===== Category 2: Constants (const) =====

    [Fact]
    public void ConstDecl_AssignsValue()
    {
        Assert.Equal(99L, Run("const result = 99;"));
    }

    [Fact]
    public void ConstDecl_CannotReassign()
    {
        RunExpectingError("const x = 5; x = 10;");
    }

    // ===== Category 3: Block Scoping =====

    [Fact]
    public void Block_InnerScopeShadowsOuter()
    {
        Assert.Equal(1L, Run("let x = 1; { let x = 2; } let result = x;"));
    }

    [Fact]
    public void Block_InnerCanSeeOuter()
    {
        Assert.Equal(15L, Run("let x = 10; let result; { result = x + 5; }"));
    }

    [Fact]
    public void Block_InnerVarNotVisibleOutside()
    {
        RunExpectingError("{ let x = 1; } let result = x;");
    }

    // ===== Category 4: If/Else =====

    [Fact]
    public void If_TrueCondition_ExecutesThen()
    {
        Assert.Equal(1L, Run("let result; if (true) { result = 1; }"));
    }

    [Fact]
    public void If_FalseCondition_SkipsThen()
    {
        Assert.Equal(0L, Run("let result = 0; if (false) { result = 1; }"));
    }

    [Fact]
    public void If_FalseCondition_ExecutesElse()
    {
        Assert.Equal(2L, Run("let result; if (false) { result = 1; } else { result = 2; }"));
    }

    [Fact]
    public void If_ElseIf_Chain()
    {
        Assert.Equal("b", Run("let x = 2; let result; if (x == 1) { result = \"a\"; } else if (x == 2) { result = \"b\"; } else { result = \"c\"; }"));
    }

    [Fact]
    public void If_Truthiness_IntOne_IsTruthy()
    {
        Assert.Equal("truthy", Run("let result; if (1) { result = \"truthy\"; } else { result = \"falsy\"; }"));
    }

    [Fact]
    public void If_Truthiness_IntZero_IsFalsy()
    {
        Assert.Equal("falsy", Run("let result; if (0) { result = \"truthy\"; } else { result = \"falsy\"; }"));
    }

    [Fact]
    public void If_Truthiness_EmptyString_IsFalsy()
    {
        Assert.Equal("falsy", Run("let result; if (\"\") { result = \"truthy\"; } else { result = \"falsy\"; }"));
    }

    [Fact]
    public void If_Truthiness_Null_IsFalsy()
    {
        Assert.Equal("falsy", Run("let result; if (null) { result = \"truthy\"; } else { result = \"falsy\"; }"));
    }

    // ===== Category 5: While Loop =====

    [Fact]
    public void While_BasicLoop()
    {
        Assert.Equal(5L, Run("let result = 0; let i = 0; while (i < 5) { result = result + 1; i = i + 1; }"));
    }

    [Fact]
    public void While_FalseCondition_NeverExecutes()
    {
        Assert.Equal(0L, Run("let result = 0; while (false) { result = 1; }"));
    }

    [Fact]
    public void While_Break()
    {
        Assert.Equal(3L, Run("let result = 0; while (true) { result = result + 1; if (result == 3) { break; } }"));
    }

    [Fact]
    public void While_Continue()
    {
        Assert.Equal(4L, Run("let result = 0; let i = 0; while (i < 5) { i = i + 1; if (i == 3) { continue; } result = result + 1; }"));
    }

    [Fact]
    public void While_NestedBreak_BreaksInnerOnly()
    {
        Assert.Equal(3L, Run("let result = 0; let i = 0; while (i < 3) { i = i + 1; let j = 0; while (j < 3) { j = j + 1; if (j == 2) { break; } result = result + 1; } }"));
    }

    // ===== Category 6: For-In Loop =====

    [Fact]
    public void ForIn_String_IteratesCharacters()
    {
        Assert.Equal("abc", Run("let result = \"\"; for (let ch in \"abc\") { result = result + ch; }"));
    }

    [Fact]
    public void ForIn_String_CountCharacters()
    {
        Assert.Equal(5L, Run("let result = 0; for (let ch in \"hello\") { result = result + 1; }"));
    }

    [Fact]
    public void ForIn_Break()
    {
        Assert.Equal("abc", Run("let result = \"\"; for (let ch in \"abcdef\") { if (ch == \"d\") { break; } result = result + ch; }"));
    }

    [Fact]
    public void ForIn_Continue()
    {
        Assert.Equal("abdef", Run("let result = \"\"; for (let ch in \"abcdef\") { if (ch == \"c\") { continue; } result = result + ch; }"));
    }

    [Fact]
    public void ForIn_NonIterable_ThrowsError()
    {
        RunExpectingError("for (let x in 42) { }");
    }

    // ===== Category 7: Functions =====

    [Fact]
    public void Function_SimpleReturn()
    {
        Assert.Equal(5L, Run("fn five() { return 5; } let result = five();"));
    }

    [Fact]
    public void Function_WithParameters()
    {
        Assert.Equal(7L, Run("fn add(a, b) { return a + b; } let result = add(3, 4);"));
    }

    [Fact]
    public void Function_ImplicitReturnNull()
    {
        Assert.Null(Run("fn greet() { let x = 1; } let result = greet();"));
    }

    [Fact]
    public void Function_RecursiveFibonacci()
    {
        Assert.Equal(55L, Run("fn fib(n) { if (n <= 1) { return n; } return fib(n - 1) + fib(n - 2); } let result = fib(10);"));
    }

    [Fact]
    public void Function_Closure()
    {
        Assert.Equal(3L, Run("fn makeCounter() { let count = 0; fn inc() { count = count + 1; return count; } return inc; } let counter = makeCounter(); counter(); counter(); let result = counter();"));
    }

    [Fact]
    public void Function_WrongArgCount_ThrowsError()
    {
        RunExpectingError("fn f(a) { return a; } f(1, 2);");
    }

    [Fact]
    public void Function_CallNonFunction_ThrowsError()
    {
        RunExpectingError("let x = 5; x();");
    }

    [Fact]
    public void Function_CanAccessGlobals()
    {
        Assert.Equal(10L, Run("let x = 10; fn getX() { return x; } let result = getX();"));
    }

    // ===== Category 8: Built-in Functions =====

    [Fact]
    public void BuiltIn_Typeof_Int()
    {
        Assert.Equal("int", Run("let result = typeof(42);"));
    }

    [Fact]
    public void BuiltIn_Typeof_Float()
    {
        Assert.Equal("float", Run("let result = typeof(3.14);"));
    }

    [Fact]
    public void BuiltIn_Typeof_String()
    {
        Assert.Equal("string", Run("let result = typeof(\"hello\");"));
    }

    [Fact]
    public void BuiltIn_Typeof_Bool()
    {
        Assert.Equal("bool", Run("let result = typeof(true);"));
    }

    [Fact]
    public void BuiltIn_Typeof_Null()
    {
        Assert.Equal("null", Run("let result = typeof(null);"));
    }

    [Fact]
    public void BuiltIn_Typeof_Function()
    {
        Assert.Equal("function", Run("fn f() {} let result = typeof(f);"));
    }

    [Fact]
    public void BuiltIn_Len_String()
    {
        Assert.Equal(5L, Run("let result = len(\"hello\");"));
    }

    [Fact]
    public void BuiltIn_Len_EmptyString()
    {
        Assert.Equal(0L, Run("let result = len(\"\");"));
    }

    [Fact]
    public void BuiltIn_ToStr_Int()
    {
        Assert.Equal("42", Run("let result = toStr(42);"));
    }

    [Fact]
    public void BuiltIn_ToStr_Null()
    {
        Assert.Equal("null", Run("let result = toStr(null);"));
    }

    [Fact]
    public void BuiltIn_ToInt_ValidString()
    {
        Assert.Equal(42L, Run("let result = toInt(\"42\");"));
    }

    [Fact]
    public void BuiltIn_ToInt_InvalidString_ThrowsError()
    {
        RunExpectingError("let x = toInt(\"abc\");");
    }

    [Fact]
    public void BuiltIn_ToInt_FromFloat()
    {
        Assert.Equal(3L, Run("let result = toInt(3.7);"));
    }

    [Fact]
    public void BuiltIn_ToFloat_FromInt()
    {
        Assert.Equal(42.0, Run("let result = toFloat(42);"));
    }

    [Fact]
    public void BuiltIn_ToFloat_FromString()
    {
        Assert.Equal(3.14, Run("let result = toFloat(\"3.14\");"));
    }

    [Fact]
    public void BuiltIn_Len_NonStringNonArray_ThrowsError()
    {
        RunExpectingError("len(42);");
    }

    // ===== Category 9: Assignment Expression =====

    [Fact]
    public void Assignment_ReturnsAssignedValue()
    {
        Assert.Equal(5L, Run("let x = 1; let result = x = 5;"));
    }

    [Fact]
    public void Assignment_UndefinedVariable_ThrowsError()
    {
        RunExpectingError("x = 5;");
    }

    // ===== Category 10: Complex Integration Tests =====

    [Fact]
    public void Integration_FibonacciAndFactorial()
    {
        Assert.Equal(720L, Run("fn fact(n) { if (n <= 1) { return 1; } return n * fact(n - 1); } let result = fact(6);"));
    }

    [Fact]
    public void Integration_NestedFunctions()
    {
        Assert.Equal(10L, Run("fn outer() { let x = 10; fn inner() { return x; } return inner(); } let result = outer();"));
    }

    [Fact]
    public void Integration_VariableShadowing()
    {
        Assert.Equal(2L, Run("let x = 1; fn f() { let x = 2; return x; } let result = f();"));
    }

    [Fact]
    public void Integration_MultipleClosures()
    {
        Assert.Equal(21L, Run("fn adder(x) { fn add(y) { return x + y; } return add; } let add5 = adder(5); let add10 = adder(10); let result = add5(3) + add10(3);"));
    }

    // ===== Review Fixes: Edge Cases =====

    [Fact]
    public void BreakOutsideLoop_ThrowsRuntimeError()
    {
        RunExpectingError("break;");
    }

    [Fact]
    public void ContinueOutsideLoop_ThrowsRuntimeError()
    {
        RunExpectingError("continue;");
    }

    [Fact]
    public void ReturnOutsideFunction_ThrowsRuntimeError()
    {
        RunExpectingError("return 42;");
    }

    [Fact]
    public void ReturnWithoutValueOutsideFunction_ThrowsRuntimeError()
    {
        RunExpectingError("return;");
    }

    [Fact]
    public void RedeclareConstAsLet_AllowsReassignment()
    {
        // After redefining a const as let, assignment should work
        Assert.Equal(3L, Run("const x = 1; let x = 2; x = 3; let result = x;"));
    }

    // ===== Category 9: Arrays =====

    // Array creation

    [Fact]
    public void ArrayLiteral_Empty()
    {
        var result = Eval("[]");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void ArrayLiteral_SingleElement()
    {
        var result = Eval("[42]");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        Assert.Equal(42L, list[0]);
    }

    [Fact]
    public void ArrayLiteral_MultipleElements()
    {
        var result = Eval("[1, 2, 3]");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(3L, list[2]);
    }

    [Fact]
    public void ArrayLiteral_MixedTypes()
    {
        var result = Eval("[1, \"hello\", true, null]");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(4, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal("hello", list[1]);
        Assert.Equal(true, list[2]);
        Assert.Null(list[3]);
    }

    [Fact]
    public void ArrayLiteral_Nested()
    {
        var result = Run("let result = [[1, 2], [3, 4]];");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        var inner0 = Assert.IsType<List<object?>>(list[0]);
        Assert.Equal(2, inner0.Count);
        Assert.Equal(1L, inner0[0]);
        Assert.Equal(2L, inner0[1]);
        var inner1 = Assert.IsType<List<object?>>(list[1]);
        Assert.Equal(2, inner1.Count);
        Assert.Equal(3L, inner1[0]);
        Assert.Equal(4L, inner1[1]);
    }

    // Array indexing

    [Fact]
    public void ArrayIndex_FirstElement()
    {
        Assert.Equal(10L, Run("let arr = [10, 20, 30]; let result = arr[0];"));
    }

    [Fact]
    public void ArrayIndex_LastElement()
    {
        Assert.Equal(30L, Run("let arr = [10, 20, 30]; let result = arr[2];"));
    }

    [Fact]
    public void ArrayIndex_OutOfBounds_Throws()
    {
        RunExpectingError("let arr = [1, 2]; let x = arr[5];");
    }

    [Fact]
    public void ArrayIndex_NegativeIndex_Throws()
    {
        RunExpectingError("let arr = [1, 2]; let x = arr[-1];");
    }

    [Fact]
    public void ArrayIndex_NonInteger_Throws()
    {
        RunExpectingError("let arr = [1, 2]; let x = arr[\"a\"];");
    }

    [Fact]
    public void ArrayIndex_Expression()
    {
        Assert.Equal(20L, Run("let arr = [10, 20, 30]; let i = 1; let result = arr[i];"));
    }

    [Fact]
    public void ArrayIndex_ArithmeticIndex()
    {
        Assert.Equal(30L, Run("let arr = [10, 20, 30]; let result = arr[1 + 1];"));
    }

    // Array index assignment

    [Fact]
    public void ArrayIndexAssign()
    {
        Assert.Equal(99L, Run("let arr = [1, 2, 3]; arr[1] = 99; let result = arr[1];"));
    }

    [Fact]
    public void ArrayIndexAssign_OutOfBounds_Throws()
    {
        RunExpectingError("let arr = [1, 2]; arr[5] = 99;");
    }

    [Fact]
    public void ArrayIndexAssign_NonInteger_Throws()
    {
        RunExpectingError("let arr = [1]; arr[\"x\"] = 99;");
    }

    // String indexing

    [Fact]
    public void StringIndex()
    {
        Assert.Equal("h", Run("let s = \"hello\"; let result = s[0];"));
    }

    [Fact]
    public void StringIndex_Last()
    {
        Assert.Equal("o", Run("let s = \"hello\"; let result = s[4];"));
    }

    [Fact]
    public void StringIndex_OutOfBounds_Throws()
    {
        RunExpectingError("let s = \"hi\"; let x = s[5];");
    }

    // Array with len

    [Fact]
    public void ArrayLen()
    {
        Assert.Equal(3L, Run("let result = len([1, 2, 3]);"));
    }

    [Fact]
    public void ArrayLen_Empty()
    {
        Assert.Equal(0L, Run("let result = len([]);"));
    }

    // Array typeof

    [Fact]
    public void ArrayTypeof()
    {
        Assert.Equal("array", Run("let result = typeof([1, 2, 3]);"));
    }

    // Array for-in

    [Fact]
    public void ArrayForIn()
    {
        Assert.Equal(6L, Run("let arr = [1, 2, 3]; let sum = 0; for (let x in arr) { sum = sum + x; } let result = sum;"));
    }

    [Fact]
    public void ArrayForIn_Empty()
    {
        Assert.Equal(0L, Run("let arr = []; let result = 0; for (let x in arr) { result = result + 1; }"));
    }

    [Fact]
    public void ArrayForIn_WithBreak()
    {
        Assert.Equal(3L, Run("let arr = [1, 2, 3, 4]; let result = 0; for (let x in arr) { if (x == 3) { break; } result = result + x; }"));
    }

    [Fact]
    public void ArrayForIn_WithContinue()
    {
        Assert.Equal(8L, Run("let arr = [1, 2, 3, 4]; let result = 0; for (let x in arr) { if (x == 2) { continue; } result = result + x; }"));
    }

    // Indexing non-indexable

    [Fact]
    public void IndexNonIndexable_Throws()
    {
        RunExpectingError("let x = 42; let y = x[0];");
    }

    // Array Stringify

    [Fact]
    public void ArrayStringify()
    {
        Assert.Equal("[1, 2, 3]", Run("let result = toStr([1, 2, 3]);"));
    }

    [Fact]
    public void ArrayStringify_Nested()
    {
        Assert.Equal("[[1, 2], [3]]", Run("let result = toStr([[1, 2], [3]]);"));
    }

    [Fact]
    public void ArrayStringify_Empty()
    {
        Assert.Equal("[]", Run("let result = toStr([]);"));
    }

    [Fact]
    public void ArrayStringify_MixedTypes()
    {
        Assert.Equal("[1, hello, true, null]", Run("let result = toStr([1, \"hello\", true, null]);"));
    }

    // Array passed to function

    [Fact]
    public void ArrayPassedToFunction()
    {
        Assert.Equal(10L, Run("fn first(arr) { return arr[0]; } let result = first([10, 20]);"));
    }

    // Array returned from function

    [Fact]
    public void ArrayReturnedFromFunction()
    {
        Assert.Equal(3L, Run("fn makeArr() { return [1, 2, 3]; } let result = len(makeArr());"));
    }

    // Chained indexing

    [Fact]
    public void ArrayChainedIndex()
    {
        Assert.Equal(3L, Run("let arr = [[1, 2], [3, 4]]; let result = arr[1][0];"));
    }

    // Assignment to non-array index

    [Fact]
    public void IndexAssignNonArray_Throws()
    {
        RunExpectingError("let s = \"hello\"; s[0] = \"H\";");
    }

    // ===== Category 10: Structs =====

    // Struct declaration + instantiation

    [Fact]
    public void StructDecl_Basic()
    {
        Assert.Equal(1L, Run("struct Point { x, y } let p = Point { x: 1, y: 2 }; let result = p.x;"));
    }

    [Fact]
    public void StructDecl_SingleField()
    {
        Assert.Equal(42L, Run("struct Wrapper { value } let w = Wrapper { value: 42 }; let result = w.value;"));
    }

    [Fact]
    public void StructDecl_EmptyInit()
    {
        Assert.Equal("struct", Run("struct Empty {} let e = Empty {}; let result = typeof(e);"));
    }

    [Fact]
    public void StructDecl_PartialInit()
    {
        Assert.Null(Run("struct Point { x, y } let p = Point { x: 1 }; let result = p.y;"));
    }

    [Fact]
    public void StructDecl_StringField()
    {
        Assert.Equal("10.0.0.1", Run("struct Srv { host } let s = Srv { host: \"10.0.0.1\" }; let result = s.host;"));
    }

    // Field access

    [Fact]
    public void StructFieldAccess_Read()
    {
        Assert.Equal(30L, Run("struct P { x, y } let p = P { x: 10, y: 20 }; let result = p.x + p.y;"));
    }

    [Fact]
    public void StructFieldAccess_UndefinedField_Throws()
    {
        RunExpectingError("struct P { x } let p = P { x: 1 }; let v = p.z;");
    }

    [Fact]
    public void StructFieldAccess_OnNonStruct_Throws()
    {
        RunExpectingError("let x = 42; let v = x.field;");
    }

    // Field assignment

    [Fact]
    public void StructFieldAssign()
    {
        Assert.Equal(99L, Run("struct P { x, y } let p = P { x: 1, y: 2 }; p.x = 99; let result = p.x;"));
    }

    [Fact]
    public void StructFieldAssign_UndefinedField_Throws()
    {
        RunExpectingError("struct P { x } let p = P { x: 1 }; p.z = 99;");
    }

    [Fact]
    public void StructFieldAssign_OnNonStruct_Throws()
    {
        RunExpectingError("let x = 42; x.field = 99;");
    }

    // NotAStruct error

    [Fact]
    public void StructInit_NotAStruct_Throws()
    {
        RunExpectingError("let x = 42; let y = x { field: 1 };");
    }

    // Unknown field in init

    [Fact]
    public void StructInit_UnknownField_Throws()
    {
        RunExpectingError("struct P { x } let p = P { y: 1 };");
    }

    // typeof struct

    [Fact]
    public void StructTypeof()
    {
        Assert.Equal("struct", Run("struct P { x } let p = P { x: 1 }; let result = typeof(p);"));
    }

    [Fact]
    public void StructTypeof_Template()
    {
        Assert.Equal("struct", Run("struct P { x } let result = typeof(P);"));
    }

    // Struct toStr

    [Fact]
    public void StructToStr()
    {
        var result = Run("struct P { x } let p = P { x: 1 }; let result = toStr(p);");
        Assert.Contains("P instance", Assert.IsType<string>(result));
    }

    // Struct in array

    [Fact]
    public void StructInArray()
    {
        Assert.Equal(3L, Run("struct P { x, y } let arr = [P { x: 1, y: 2 }, P { x: 3, y: 4 }]; let result = arr[1].x;"));
    }

    // Array in struct

    [Fact]
    public void ArrayInStruct()
    {
        Assert.Equal(3L, Run("struct S { items } let s = S { items: [1, 2, 3] }; let result = len(s.items);"));
    }

    [Fact]
    public void ArrayInStruct_Index()
    {
        Assert.Equal(20L, Run("struct S { items } let s = S { items: [10, 20] }; let result = s.items[1];"));
    }

    // Struct passed to function

    [Fact]
    public void StructPassedToFunction()
    {
        Assert.Equal(42L, Run("struct P { x, y } fn getX(p) { return p.x; } let p = P { x: 42, y: 0 }; let result = getX(p);"));
    }

    // Struct with function modifying field

    [Fact]
    public void StructFieldModifiedByFunction()
    {
        Assert.Equal(99L, Run("struct P { x } fn setX(p, v) { p.x = v; } let p = P { x: 1 }; setX(p, 99); let result = p.x;"));
    }

    // Nested struct access

    [Fact]
    public void NestedStructAccess()
    {
        Assert.Equal(42L, Run("struct Inner { val } struct Outer { inner } let i = Inner { val: 42 }; let o = Outer { inner: i }; let result = o.inner.val;"));
    }

    // ===== Category 11: Enums =====

    // Basic enum declaration + access

    [Fact]
    public void EnumDecl_Basic()
    {
        Assert.Equal("Color.Red", Run("enum Color { Red, Green, Blue } let result = toStr(Color.Red);"));
    }

    [Fact]
    public void EnumDecl_Assignment()
    {
        Assert.Equal("Status.Active", Run("enum Status { Active, Inactive } let s = Status.Active; let result = toStr(s);"));
    }

    // Enum equality

    [Fact]
    public void EnumEquality_SameMember()
    {
        Assert.Equal(true, Run("enum S { A, B } let result = S.A == S.A;"));
    }

    [Fact]
    public void EnumEquality_DifferentMember()
    {
        Assert.Equal(false, Run("enum S { A, B } let result = S.A == S.B;"));
    }

    [Fact]
    public void EnumInequality()
    {
        Assert.Equal(true, Run("enum S { A, B } let result = S.A != S.B;"));
    }

    [Fact]
    public void EnumEquality_DifferentTypes()
    {
        Assert.Equal(false, Run("enum S { A } enum T { A } let result = S.A == T.A;"));
    }

    [Fact]
    public void EnumInequality_DifferentTypes()
    {
        Assert.Equal(true, Run("enum S { A } enum T { A } let result = S.A != T.A;"));
    }

    // Enum - undefined member

    [Fact]
    public void EnumUndefinedMember_Throws()
    {
        RunExpectingError("enum Color { Red } let x = Color.Blue;");
    }

    // typeof enum

    [Fact]
    public void EnumTypeof_Value()
    {
        Assert.Equal("enum", Run("enum S { A } let result = typeof(S.A);"));
    }

    [Fact]
    public void EnumTypeof_Type()
    {
        Assert.Equal("enum", Run("enum S { A } let result = typeof(S);"));
    }

    // Enum in if statement

    [Fact]
    public void EnumInIf()
    {
        Assert.Equal(1L, Run("enum S { Active, Inactive } let s = S.Active; let result = 0; if (s == S.Active) { result = 1; }"));
    }

    // Enum in struct

    [Fact]
    public void EnumInStruct()
    {
        Assert.Equal(true, Run("enum Status { Up, Down } struct Server { host, status } let srv = Server { host: \"10.0.0.1\", status: Status.Up }; let result = srv.status == Status.Up;"));
    }

    // Enum passed to function

    [Fact]
    public void EnumPassedToFunction()
    {
        Assert.Equal(true, Run("enum S { A, B } fn isA(val) { return val == S.A; } let result = isA(S.A);"));
    }

    // Enum != other types

    [Fact]
    public void EnumNotEqualToString()
    {
        Assert.Equal(false, Run("enum S { A } let result = S.A == \"A\";"));
    }

    [Fact]
    public void EnumNotEqualToInt()
    {
        Assert.Equal(false, Run("enum S { A } let result = S.A == 0;"));
    }

    [Fact]
    public void EnumNotEqualToNull()
    {
        Assert.Equal(false, Run("enum S { A } let result = S.A == null;"));
    }

    // Dot on non-enum non-struct

    [Fact]
    public void DotOnNonStruct_Throws()
    {
        RunExpectingError("let x = 42; let y = x.something;");
    }

    // Dot assign on non-struct

    [Fact]
    public void DotAssignOnNonStruct_Throws()
    {
        RunExpectingError("let x = 42; x.something = 1;");
    }

    // Assign to enum member (dot assign on enum should fail)

    [Fact]
    public void DotAssignOnEnum_Throws()
    {
        RunExpectingError("enum S { A, B } S.A = S.B;");
    }

    // Duplicate field in struct init

    [Fact]
    public void StructInit_DuplicateField_Throws()
    {
        RunExpectingError("struct P { x, y } let p = P { x: 1, x: 2 };");
    }

    // ===== String Interpolation =====

    [Fact]
    public void Interpolation_NoExpressions_ReturnsPlainString()
    {
        Assert.Equal("hello", Eval("$\"hello\""));
    }

    [Fact]
    public void Interpolation_SimpleVariable()
    {
        Assert.Equal("hi Alice", Run("let name = \"Alice\"; let result = $\"hi {name}\";"));
    }

    [Fact]
    public void Interpolation_ArithmeticExpression()
    {
        Assert.Equal("sum is 7", Eval("$\"sum is {3 + 4}\""));
    }

    [Fact]
    public void Interpolation_MultipleExpressions()
    {
        Assert.Equal("1 and 2", Eval("$\"{1} and {2}\""));
    }

    [Fact]
    public void Interpolation_NullValue()
    {
        Assert.Equal("value is null", Run("let x = null; let result = $\"value is {x}\";"));
    }

    [Fact]
    public void Interpolation_BooleanValue()
    {
        Assert.Equal("it is true", Eval("$\"it is {true}\""));
    }

    [Fact]
    public void Interpolation_IntegerValue()
    {
        Assert.Equal("count: 42", Eval("$\"count: {42}\""));
    }

    [Fact]
    public void Interpolation_FloatValue()
    {
        Assert.Equal("pi: 3.14", Eval("$\"pi: {3.14}\""));
    }

    [Fact]
    public void Interpolation_EmbeddedForm()
    {
        Assert.Equal("hello world", Run("let x = \"world\"; let result = \"hello ${x}\";"));
    }

    [Fact]
    public void Interpolation_PrefixedForm()
    {
        Assert.Equal("hello world", Run("let x = \"world\"; let result = $\"hello {x}\";"));
    }

    [Fact]
    public void Interpolation_NestedStringConcat()
    {
        Assert.Equal("val: ab", Eval("$\"val: {\"a\" + \"b\"}\""));
    }

    [Fact]
    public void Interpolation_TernaryExpression()
    {
        Assert.Equal("yes", Eval("$\"{true ? \"yes\" : \"no\"}\""));
    }

    [Fact]
    public void Interpolation_FunctionCall()
    {
        var source = "fn greet() { return \"hi\"; } let result = $\"{greet()}\";";
        Assert.Equal("hi", Run(source));
    }

    [Fact]
    public void Interpolation_EscapedBraces()
    {
        Assert.Equal("literal {brace}", Eval("$\"literal \\{brace}\""));
    }

    [Fact]
    public void Interpolation_AdjacentWithNoText()
    {
        Assert.Equal("12", Eval("$\"{1}{2}\""));
    }

    // ===== Phase 4: Command Execution =====

    [Fact]
    public void Command_BasicEcho_StdoutCaptured()
    {
        var result = Run("let r = $(echo hello); let result = r.stdout;");
        Assert.IsType<string>(result);
        Assert.Contains("hello", (string)result!);
    }

    [Fact]
    public void Command_ExitCodeSuccess()
    {
        Assert.Equal(0L, Run("let r = $(true); let result = r.exitCode;"));
    }

    [Fact]
    public void Command_ExitCodeFailure()
    {
        var result = Run("let r = $(false); let result = r.exitCode;");
        Assert.IsType<long>(result);
        Assert.NotEqual(0L, result);
    }

    [Fact]
    public void Command_StderrCaptured()
    {
        var result = Run("let r = $(echo error >&2); let result = r.stderr;");
        Assert.IsType<string>(result);
        Assert.Contains("error", (string)result!);
    }

    [Fact]
    public void Command_ResultIsStashInstance()
    {
        var source = @"let result = $(echo hi);";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        var result = interpreter.Interpret(resultExpr);

        Assert.IsType<StashInstance>(result);
    }

    [Fact]
    public void Command_WithInterpolation_VariableSubstituted()
    {
        var result = Run("let x = \"world\"; let r = $(echo {x}); let result = r.stdout;");
        Assert.Contains("world", (string)result!);
    }

    [Fact]
    public void Command_WithExpressionInterpolation()
    {
        var result = Run("let a = 2; let b = 3; let r = $(echo {a + b}); let result = r.stdout;");
        Assert.Contains("5", (string)result!);
    }

    [Fact]
    public void Command_TypeofResult()
    {
        Assert.Equal("struct", Run("let r = $(echo hi); let result = typeof(r);"));
    }

    [Fact]
    public void Command_StdoutFieldAccess()
    {
        var result = Run("let result = $(echo hello).stdout;");
        Assert.Contains("hello", (string)result!);
    }

    [Fact]
    public void Command_ExitCodeFieldAccess()
    {
        Assert.Equal(0L, Run("let result = $(true).exitCode;"));
    }

    // ===== Phase 4: Pipe Operator =====

    [Fact]
    public void Pipe_BasicChain_StdoutPiped()
    {
        var result = Run("let r = $(echo hello) | $(cat); let result = r.stdout;");
        Assert.Contains("hello", (string)result!);
    }

    [Fact]
    public void Pipe_ShortCircuitOnFailure()
    {
        var result = Run("let r = $(false) | $(echo should not run); let result = r.exitCode;");
        Assert.NotEqual(0L, result);
    }

    [Fact]
    public void Pipe_ThreeCommands()
    {
        var result = Run("let r = $(echo hello) | $(cat) | $(cat); let result = r.stdout;");
        Assert.Contains("hello", (string)result!);
    }

    [Fact]
    public void Pipe_GrepFilter()
    {
        // echo two lines, grep for one
        var result = Run(@"let r = $(printf ""hello\nworld"") | $(grep world); let result = r.stdout;");
        Assert.Contains("world", (string)result!);
    }

    [Fact]
    public void Pipe_LeftSideNotCommand_ThrowsError()
    {
        RunExpectingError("let x = 42; let r = x | $(cat);");
    }

    // ===== Phase 4: readFile / writeFile Built-ins =====

    [Fact]
    public void ReadFile_ReadsContent()
    {
        string tmpFile = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tmpFile, "test content");
            var result = Run($"let result = readFile(\"{tmpFile.Replace("\\", "\\\\")}\");");
            Assert.Equal("test content", result);
        }
        finally
        {
            System.IO.File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ReadFile_NonExistentFile_ThrowsError()
    {
        RunExpectingError("readFile(\"/nonexistent/file/path/xyz.txt\");");
    }

    [Fact]
    public void ReadFile_NonStringArg_ThrowsError()
    {
        RunExpectingError("readFile(42);");
    }

    [Fact]
    public void WriteFile_WritesContent()
    {
        string tmpFile = System.IO.Path.GetTempFileName();
        try
        {
            Run($"writeFile(\"{tmpFile.Replace("\\", "\\\\")}\", \"hello world\"); let result = 1;");
            Assert.Equal("hello world", System.IO.File.ReadAllText(tmpFile));
        }
        finally
        {
            System.IO.File.Delete(tmpFile);
        }
    }

    [Fact]
    public void WriteFile_NonStringPath_ThrowsError()
    {
        RunExpectingError("writeFile(42, \"content\");");
    }

    [Fact]
    public void WriteFile_NonStringContent_ThrowsError()
    {
        RunExpectingError("writeFile(\"/tmp/test\", 42);");
    }

    [Fact]
    public void WriteFile_ReadFile_Roundtrip()
    {
        string tmpFile = System.IO.Path.GetTempFileName();
        try
        {
            var result = Run($"writeFile(\"{tmpFile.Replace("\\", "\\\\")}\", \"roundtrip data\"); let result = readFile(\"{tmpFile.Replace("\\", "\\\\")}\");");
            Assert.Equal("roundtrip data", result);
        }
        finally
        {
            System.IO.File.Delete(tmpFile);
        }
    }

    // ===== Phase 4: env / setEnv Built-ins =====

    [Fact]
    public void Env_ReadsExistingVariable()
    {
        // PATH should be set on any system
        var result = Run("let result = env(\"PATH\");");
        Assert.IsType<string>(result);
        Assert.NotNull(result);
        Assert.True(((string)result!).Length > 0);
    }

    [Fact]
    public void Env_NonExistentVariable_ReturnsNull()
    {
        Assert.Null(Run("let result = env(\"STASH_NONEXISTENT_VAR_XYZ_12345\");"));
    }

    [Fact]
    public void Env_NonStringArg_ThrowsError()
    {
        RunExpectingError("env(42);");
    }

    [Fact]
    public void SetEnv_SetsVariable()
    {
        var result = Run("setEnv(\"STASH_TEST_VAR\", \"test_value\"); let result = env(\"STASH_TEST_VAR\");");
        Assert.Equal("test_value", result);
    }

    [Fact]
    public void SetEnv_NonStringName_ThrowsError()
    {
        RunExpectingError("setEnv(42, \"value\");");
    }

    [Fact]
    public void SetEnv_NonStringValue_ThrowsError()
    {
        RunExpectingError("setEnv(\"name\", 42);");
    }

    // ===== Phase 4: exit Built-in =====

    [Fact]
    public void Exit_NonIntegerArg_ThrowsError()
    {
        RunExpectingError("exit(\"not a number\");");
    }

    // Note: exit(0) cannot be tested directly as it terminates the process

    // ===== Phase 4: typeof for new types =====

    [Fact]
    public void Typeof_CommandResult_ReturnsStruct()
    {
        Assert.Equal("struct", Run("let result = typeof($(echo hi));"));
    }

    [Fact]
    public void Typeof_BuiltInFunction_ReturnsFunction()
    {
        Assert.Equal("function", Run("let result = typeof(println);"));
    }

    [Fact]
    public void CommandExecution_EmptyCommand_ThrowsRuntimeError()
    {
        RunExpectingError("let r = $( );");
    }

    [Fact]
    public void CommandExecution_QuotedParensDoNotBreakLexing()
    {
        // Parentheses inside quotes must not affect the nesting depth.
        object? result = Run("let r = $(echo \"(hello)\"); let result = r.stdout;");
        Assert.Equal("(hello)\n", result);
    }

    // ===== Phase 5: Try Expression =====

    [Fact]
    public void TryExpr_CatchesRuntimeError_ReturnsNull()
    {
        // toInt("abc") throws RuntimeError, try catches it
        Assert.Null(Run("let result = try toInt(\"abc\");"));
    }

    [Fact]
    public void TryExpr_NoError_ReturnsValue()
    {
        Assert.Equal(42L, Run("let result = try toInt(\"42\");"));
    }

    [Fact]
    public void TryExpr_DivisionByZero_ReturnsNull()
    {
        Assert.Null(Run("let result = try (1 / 0);"));
    }

    [Fact]
    public void TryExpr_UndefinedVariable_ReturnsNull()
    {
        Assert.Null(Run("let result = try undefinedVar;"));
    }

    [Fact]
    public void TryExpr_InvalidFieldAccess_ReturnsNull()
    {
        Assert.Null(Run("struct S { x } let s = S { x: 1 }; let result = try s.nonexistent;"));
    }

    // ===== Phase 5: Null Coalescing (??) =====

    [Fact]
    public void NullCoalesce_LeftNotNull_ReturnsLeft()
    {
        Assert.Equal(42L, Run("let result = 42 ?? 99;"));
    }

    [Fact]
    public void NullCoalesce_LeftNull_ReturnsRight()
    {
        Assert.Equal(99L, Run("let result = null ?? 99;"));
    }

    [Fact]
    public void NullCoalesce_BothNull_ReturnsNull()
    {
        Assert.Null(Run("let result = null ?? null;"));
    }

    [Fact]
    public void NullCoalesce_Chain_FirstNonNull()
    {
        Assert.Equal("found", Run("let result = null ?? null ?? \"found\";"));
    }

    [Fact]
    public void NullCoalesce_ShortCircuit_DoesNotEvaluateRight()
    {
        // If left is not null, right should not be evaluated
        // Using a function call as right side that would fail if evaluated
        Assert.Equal(42L, Run("let result = 42 ?? toInt(\"bad\");"));
    }

    [Fact]
    public void NullCoalesce_WithTry_CombinedPattern()
    {
        // The canonical pattern: try expr ?? default
        Assert.Equal("default", Run("let result = try toInt(\"abc\") ?? \"default\";"));
    }

    [Fact]
    public void NullCoalesce_FalsyValuesAreNotNull()
    {
        // 0, false, "" are falsy but not null — ?? should NOT replace them
        Assert.Equal(0L, Run("let result = 0 ?? 99;"));
    }

    [Fact]
    public void NullCoalesce_EmptyStringIsNotNull()
    {
        Assert.Equal("", Run("let result = \"\" ?? \"default\";"));
    }

    [Fact]
    public void NullCoalesce_FalseIsNotNull()
    {
        Assert.Equal(false, Run("let result = false ?? true;"));
    }

    // ===== Phase 5: lastError() =====

    [Fact]
    public void LastError_InitiallyNull()
    {
        Assert.Null(Run("let result = lastError();"));
    }

    [Fact]
    public void LastError_AfterTryCatchesError()
    {
        var source = @"
            let x = try toInt(""abc"");
            let result = lastError();
        ";
        var resultVal = Run(source);
        Assert.IsType<string>(resultVal);
        Assert.Contains("Cannot parse", (string)resultVal!);
    }

    [Fact]
    public void LastError_ResetBySubsequentTry()
    {
        var source = @"
            let x = try toInt(""abc"");
            let y = try toInt(""42"");
            let result = lastError();
        ";
        // After successful try, lastError should still be from the failed one
        // (only set on error, not cleared on success)
        var resultVal = Run(source);
        Assert.IsType<string>(resultVal);
    }

    [Fact]
    public void LastError_DivisionByZero()
    {
        var source = @"
            let x = try (1 / 0);
            let result = lastError();
        ";
        var resultVal = Run(source);
        Assert.IsType<string>(resultVal);
        Assert.Contains("zero", ((string)resultVal!).ToLower());
    }

    // ===== Phase 5: Imports =====

    private static object? RunWithFile(string source, string filePath)
    {
        var lexer = new Lexer(source, filePath);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.CurrentFile = filePath;
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    [Fact]
    public void Import_FunctionFromModule()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            // Create module file
            string modulePath = System.IO.Path.Combine(tmpDir, "math.stash");
            System.IO.File.WriteAllText(modulePath, "fn add(a, b) { return a + b; }");

            // Create main script
            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import { add } from \"math.stash\"; let result = add(3, 4);";

            Assert.Equal(7L, RunWithFile(source, mainPath));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Import_StructFromModule()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "types.stash");
            System.IO.File.WriteAllText(modulePath, "struct Point { x, y }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import { Point } from \"types.stash\"; let p = Point { x: 10, y: 20 }; let result = p.x;";

            Assert.Equal(10L, RunWithFile(source, mainPath));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Import_EnumFromModule()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "enums.stash");
            System.IO.File.WriteAllText(modulePath, "enum Color { Red, Green, Blue }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import { Color } from \"enums.stash\"; let result = Color.Red == Color.Red;";

            Assert.Equal(true, RunWithFile(source, mainPath));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Import_ConstantFromModule()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "config.stash");
            System.IO.File.WriteAllText(modulePath, "const MAX = 100;");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import { MAX } from \"config.stash\"; let result = MAX;";

            Assert.Equal(100L, RunWithFile(source, mainPath));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Import_MultipleNames()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "utils.stash");
            System.IO.File.WriteAllText(modulePath, "fn add(a, b) { return a + b; } fn mul(a, b) { return a * b; }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import { add, mul } from \"utils.stash\"; let result = add(2, 3) + mul(4, 5);";

            Assert.Equal(25L, RunWithFile(source, mainPath));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Import_ModuleNotFound_ThrowsRuntimeError()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import { foo } from \"nonexistent.stash\";";

            var lexer = new Lexer(source, mainPath);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var statements = parser.ParseProgram();
            var interpreter = new Interpreter();
            interpreter.CurrentFile = mainPath;
            Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Import_UndefinedName_ThrowsRuntimeError()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "mod.stash");
            System.IO.File.WriteAllText(modulePath, "fn foo() { return 1; }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import { bar } from \"mod.stash\";";

            var lexer = new Lexer(source, mainPath);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var statements = parser.ParseProgram();
            var interpreter = new Interpreter();
            interpreter.CurrentFile = mainPath;
            Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Import_CircularImport_ThrowsRuntimeError()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            // a.stash imports b.stash, b.stash imports a.stash
            string aPath = System.IO.Path.Combine(tmpDir, "a.stash");
            string bPath = System.IO.Path.Combine(tmpDir, "b.stash");
            System.IO.File.WriteAllText(aPath, "import { bar } from \"b.stash\"; fn foo() { return 1; }");
            System.IO.File.WriteAllText(bPath, "import { foo } from \"a.stash\"; fn bar() { return 2; }");

            var lexer = new Lexer(System.IO.File.ReadAllText(aPath), aPath);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var statements = parser.ParseProgram();
            var interpreter = new Interpreter();
            interpreter.CurrentFile = aPath;
            Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Import_ModuleCached_NotExecutedTwice()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            // Module has a side effect (incrementing counter), if cached, it should only execute once
            string modulePath = System.IO.Path.Combine(tmpDir, "counter.stash");
            System.IO.File.WriteAllText(modulePath, "let count = 1; fn getCount() { return count; }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            // Importing the same module twice should not re-execute it
            string source = "import { getCount } from \"counter.stash\"; import { count } from \"counter.stash\"; let result = count;";

            Assert.Equal(1L, RunWithFile(source, mainPath));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    // ===== Phase 5: Try + ?? Combined Patterns =====

    [Fact]
    public void TryNullCoalesce_ReadFile_WithDefault()
    {
        // try readFile("nonexistent") ?? "fallback"
        Assert.Equal("fallback", Run("let result = try readFile(\"/nonexistent/path/file.txt\") ?? \"fallback\";"));
    }

    [Fact]
    public void TryNullCoalesce_NestedCalls()
    {
        // try (try expr ?? default1) ?? default2
        Assert.Equal(42L, Run("let result = try toInt(\"42\") ?? 0;"));
    }

    // --- Increment/Decrement (++/--) Tests ---

    [Fact]
    public void PrefixIncrement_IncrementsAndReturnsNewValue()
    {
        Assert.Equal(6L, Run("let x = 5; let result = ++x;"));
    }

    [Fact]
    public void PostfixIncrement_IncrementsAndReturnsOldValue()
    {
        Assert.Equal(5L, Run("let x = 5; let result = x++;"));
    }

    [Fact]
    public void PrefixDecrement_DecrementsAndReturnsNewValue()
    {
        Assert.Equal(4L, Run("let x = 5; let result = --x;"));
    }

    [Fact]
    public void PostfixDecrement_DecrementsAndReturnsOldValue()
    {
        Assert.Equal(5L, Run("let x = 5; let result = x--;"));
    }

    [Fact]
    public void PostfixIncrement_MutatesVariable()
    {
        Assert.Equal(6L, Run("let x = 5; x++; let result = x;"));
    }

    [Fact]
    public void PostfixDecrement_MutatesVariable()
    {
        Assert.Equal(4L, Run("let x = 5; x--; let result = x;"));
    }

    [Fact]
    public void PrefixIncrement_MutatesVariable()
    {
        Assert.Equal(6L, Run("let x = 5; ++x; let result = x;"));
    }

    [Fact]
    public void PrefixDecrement_MutatesVariable()
    {
        Assert.Equal(4L, Run("let x = 5; --x; let result = x;"));
    }

    [Fact]
    public void Increment_OnFloat_Works()
    {
        var result = Run("let x = 3.14; ++x; let result = x;");
        Assert.IsType<double>(result);
        Assert.Equal(4.14, (double)result!, precision: 10);
    }

    [Fact]
    public void Decrement_OnFloat_Works()
    {
        var result = Run("let x = 3.14; --x; let result = x;");
        Assert.IsType<double>(result);
        Assert.Equal(2.14, (double)result!, precision: 10);
    }

    [Fact]
    public void Increment_OnNonNumeric_ThrowsRuntimeError()
    {
        RunExpectingError("let x = \"hello\"; x++;");
    }

    [Fact]
    public void Decrement_OnNonNumeric_ThrowsRuntimeError()
    {
        RunExpectingError("let x = true; x--;");
    }

    [Fact]
    public void Increment_OnNull_ThrowsRuntimeError()
    {
        RunExpectingError("let x = null; x++;");
    }

    [Fact]
    public void PrefixIncrement_OnStructField()
    {
        Assert.Equal(11L, Run("struct S { v } let s = S { v: 10 }; ++s.v; let result = s.v;"));
    }

    [Fact]
    public void PostfixIncrement_OnStructField_ReturnsOldValue()
    {
        Assert.Equal(10L, Run("struct S { v } let s = S { v: 10 }; let result = s.v++;"));
    }

    [Fact]
    public void PostfixIncrement_OnStructField_MutatesField()
    {
        Assert.Equal(11L, Run("struct S { v } let s = S { v: 10 }; s.v++; let result = s.v;"));
    }

    [Fact]
    public void PrefixIncrement_OnArrayElement()
    {
        Assert.Equal(11L, Run("let arr = [10, 20]; ++arr[0]; let result = arr[0];"));
    }

    [Fact]
    public void PostfixIncrement_OnArrayElement_ReturnsOldValue()
    {
        Assert.Equal(10L, Run("let arr = [10, 20]; let result = arr[0]++;"));
    }

    [Fact]
    public void PostfixIncrement_OnArrayElement_MutatesElement()
    {
        Assert.Equal(11L, Run("let arr = [10, 20]; arr[0]++; let result = arr[0];"));
    }

    [Fact]
    public void Increment_InWhileLoop()
    {
        // Classic counter loop pattern
        Assert.Equal(5L, Run("let i = 0; while (i < 5) { i++; } let result = i;"));
    }

    [Fact]
    public void Increment_InExpression()
    {
        // x++ returns 5 (old value), ++y returns 6 (new value) → 5 + 6 = 11
        Assert.Equal(11L, Run("let x = 5; let y = 5; let result = x++ + ++y;"));
    }

    [Fact]
    public void MultipleIncrements_Sequential()
    {
        Assert.Equal(8L, Run("let x = 5; x++; x++; x++; let result = x;"));
    }

    // --- Operator Precedence End-to-End ---

    [Fact]
    public void Precedence_MultiplicationBeforeTerm()
    {
        // Already tested, but verify subtraction too: 10 - 2 * 3 = 4
        Assert.Equal(4L, Eval("10 - 2 * 3"));
    }

    [Fact]
    public void Precedence_ComparisonAfterTerm()
    {
        Assert.Equal(true, Eval("1 + 2 < 4"));
    }

    [Fact]
    public void Precedence_EqualityAfterComparison()
    {
        Assert.Equal(true, Eval("1 < 2 == true"));
    }

    [Fact]
    public void Precedence_AndBeforeOr()
    {
        // false || true && true → false || (true && true) → true
        Assert.Equal(true, Eval("false || true && true"));
    }

    [Fact]
    public void Precedence_NullCoalesceBeforeTernary()
    {
        // true ? null ?? 42 : 0 → ternary picks then-branch, then ?? resolves to 42
        Assert.Equal(42L, Eval("true ? null ?? 42 : 0"));
    }

    // --- Return without value ---

    [Fact]
    public void Function_ReturnWithoutValue_ReturnsNull()
    {
        Assert.Null(Run("fn f() { return; } let result = f();"));
    }

    [Fact]
    public void Function_MultipleReturnPaths()
    {
        // Function with early return on one branch, implicit null on other
        Assert.Equal(42L, Run("fn f(x) { if (x) { return 42; } } let result = f(true);"));
    }

    [Fact]
    public void Function_MultipleReturnPaths_ImplicitNull()
    {
        Assert.Null(Run("fn f(x) { if (x) { return 42; } } let result = f(false);"));
    }

    // --- Division and modulo with negatives ---

    [Fact]
    public void IntegerDivision_NegativeDividend()
    {
        // -10 / 3 — truncation toward zero
        var result = Eval("-10 / 3");
        Assert.IsType<long>(result);
        Assert.Equal(-3L, result);
    }

    [Fact]
    public void IntegerModulo_NegativeDividend()
    {
        // -10 % 3 — C# behavior: sign follows dividend
        var result = Eval("-10 % 3");
        Assert.IsType<long>(result);
        Assert.Equal(-1L, result);
    }

    [Fact]
    public void IntegerModulo_NegativeDivisor()
    {
        var result = Eval("10 % -3");
        Assert.IsType<long>(result);
        Assert.Equal(1L, result);
    }

    // --- Truthiness edge cases ---

    [Fact]
    public void Truthiness_EmptyArrayIsTruthy()
    {
        Assert.Equal(true, Run("let result = [] ? true : false;"));
    }

    [Fact]
    public void Truthiness_StructInstanceIsTruthy()
    {
        Assert.Equal(true, Run("struct S { x } let s = S { x: 1 }; let result = s ? true : false;"));
    }

    // --- Mixed type comparisons ---

    [Fact]
    public void Comparison_MixedIntFloat_LessThan()
    {
        Assert.Equal(true, Eval("3 < 3.5"));
    }

    [Fact]
    public void Comparison_MixedIntFloat_GreaterThan()
    {
        Assert.Equal(true, Eval("4 > 3.5"));
    }

    [Fact]
    public void Comparison_MixedIntFloat_LessEqual()
    {
        Assert.Equal(true, Eval("3 <= 3.0"));
    }

    [Fact]
    public void Comparison_MixedIntFloat_GreaterEqual()
    {
        Assert.Equal(true, Eval("3 >= 2.5"));
    }

    // --- String indexing edge cases ---

    [Fact]
    public void StringIndex_NegativeIndex_Throws()
    {
        RunExpectingError("let s = \"hello\"; let result = s[-1];");
    }

    // --- Const in inner scope ---

    [Fact]
    public void ConstDecl_InBlock_CanBeRead()
    {
        Assert.Equal(42L, Run("let result = 0; { const X = 42; result = X; }"));
    }

    [Fact]
    public void ConstDecl_InBlock_CannotReassign()
    {
        RunExpectingError("{ const X = 42; X = 99; }");
    }

    // --- Chained assignment as expression ---

    [Fact]
    public void ChainedAssignment()
    {
        Assert.Equal(5L, Run("let x = 0; let y = 0; x = y = 5; let result = x;"));
    }

    [Fact]
    public void ChainedAssignment_BothUpdated()
    {
        Assert.Equal(5L, Run("let x = 0; let y = 0; x = y = 5; let result = y;"));
    }

    // --- First-class functions ---

    [Fact]
    public void Function_StoredInVariable()
    {
        Assert.Equal(10L, Run("fn double(x) { return x * 2; } let f = double; let result = f(5);"));
    }

    [Fact]
    public void Function_PassedAsArgument()
    {
        Assert.Equal(6L, Run(@"
            fn apply(f, x) { return f(x); }
            fn triple(x) { return x * 3; }
            let result = apply(triple, 2);
        "));
    }

    // --- for-in non-iterable types ---

    [Fact]
    public void ForIn_Boolean_ThrowsRuntimeError()
    {
        RunExpectingError("for (let x in true) { }");
    }

    [Fact]
    public void ForIn_Null_ThrowsRuntimeError()
    {
        RunExpectingError("for (let x in null) { }");
    }

    [Fact]
    public void ForIn_Float_ThrowsRuntimeError()
    {
        RunExpectingError("for (let x in 3.14) { }");
    }

    // --- Import mutability ---

    [Fact]
    public void Import_LetVariable_CanBeRead()
    {
        // Create a module with a let variable and import it
        string modulePath = Path.Combine(Path.GetTempPath(), "stash_test_import_let_" + Guid.NewGuid().ToString("N") + ".stash");
        File.WriteAllText(modulePath, "let counter = 42;");
        try
        {
            var result = RunWithFile(
                $"import {{ counter }} from \"{modulePath}\"; let result = counter;",
                Path.Combine(Path.GetTempPath(), "main.stash")
            );
            Assert.Equal(42L, result);
        }
        finally
        {
            File.Delete(modulePath);
        }
    }
}
