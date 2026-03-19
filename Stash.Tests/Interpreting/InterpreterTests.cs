using System;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Interpreting.Types;

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

    // 3a. Triple-quoted strings
    [Fact]
    public void TripleQuotedString_ReturnsCorrectValue()
    {
        Assert.Equal("hello world", Eval("\"\"\"hello world\"\"\""));
    }

    [Fact]
    public void TripleQuotedString_InterpolationEndToEnd()
    {
        var source = "let name = \"world\"; let result = \"\"\"\nhello ${name}\n\"\"\";";
        Assert.Equal("hello world", Run(source));
    }

    [Fact]
    public void TripleQuotedString_MultiLineDedent()
    {
        var source = "\"\"\"\n    line1\n    line2\n\"\"\"";
        Assert.Equal("line1\nline2", Eval(source));
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
    public void StringRepetition_Basic()
    {
        Assert.Equal("hahaha", Eval("\"ha\" * 3"));
    }

    [Fact]
    public void StringRepetition_Commutative()
    {
        Assert.Equal("hahaha", Eval("3 * \"ha\""));
    }

    [Fact]
    public void StringRepetition_Zero()
    {
        Assert.Equal("", Eval("\"x\" * 0"));
    }

    [Fact]
    public void StringRepetition_EmptyString()
    {
        Assert.Equal("", Eval("\"\" * 5"));
    }

    [Fact]
    public void StringRepetition_Once()
    {
        Assert.Equal("ab", Eval("\"ab\" * 1"));
    }

    [Fact]
    public void StringRepetition_NegativeCount_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Eval("\"x\" * -1"));
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

    // ── and / or keyword aliases ─────────────────────────────────────────────

    [Fact]
    public void AndKeyword_BehavesLikeAmpersandAmpersand()
    {
        Assert.Equal(true, Eval("true and true"));
        Assert.Equal(false, Eval("true and false"));
        Assert.Equal(false, Eval("false and true"));
    }

    [Fact]
    public void OrKeyword_BehavesLikePipePipe()
    {
        Assert.Equal(true, Eval("true or false"));
        Assert.Equal(true, Eval("false or true"));
        Assert.Equal(false, Eval("false or false"));
    }

    [Fact]
    public void AndKeyword_ShortCircuits()
    {
        // false and <anything> should short-circuit and not evaluate right side
        Assert.Equal(false, Eval("false and (1 / 0)"));
    }

    [Fact]
    public void OrKeyword_ShortCircuits()
    {
        // true or <anything> should short-circuit and not evaluate right side
        Assert.Equal(true, Eval("true or (1 / 0)"));
    }

    [Fact]
    public void AndOrKeywords_MixedWithSymbols()
    {
        Assert.Equal(true, Eval("true && true and true"));
        Assert.Equal(true, Eval("false || true or true"));
        Assert.Equal(false, Eval("true and false || false"));
    }

    [Fact]
    public void AndOrKeywords_InIfCondition()
    {
        Assert.Equal("yes", Run("let result = \"no\"; if (true and true) { result = \"yes\"; }"));
        Assert.Equal("yes", Run("let result = \"no\"; if (false or true) { result = \"yes\"; }"));
    }

    [Fact]
    public void AndOrKeywords_InWhileCondition()
    {
        Assert.Equal(1L, Run("let x = 0; let result = 0; while (x < 1 and result == 0) { result = 1; x = x + 1; }"));
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

    // ===== Category X: Do-While Loop =====

    [Fact]
    public void DoWhile_BasicLoop()
    {
        Assert.Equal(5L, Run("let result = 0; let i = 0; do { result = result + 1; i = i + 1; } while (i < 5);"));
    }

    [Fact]
    public void DoWhile_FalseCondition_ExecutesOnce()
    {
        Assert.Equal(1L, Run("let result = 0; do { result = result + 1; } while (false);"));
    }

    [Fact]
    public void DoWhile_Break()
    {
        Assert.Equal(3L, Run("let result = 0; do { result = result + 1; if (result == 3) { break; } } while (true);"));
    }

    [Fact]
    public void DoWhile_Continue()
    {
        Assert.Equal(4L, Run("let result = 0; let i = 0; do { i = i + 1; if (i == 3) { continue; } result = result + 1; } while (i < 5);"));
    }

    [Fact]
    public void DoWhile_NestedBreak_BreaksInnerOnly()
    {
        Assert.Equal(3L, Run("let result = 0; let i = 0; do { i = i + 1; let j = 0; do { j = j + 1; if (j == 2) { break; } result = result + 1; } while (j < 3); } while (i < 3);"));
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
        Assert.Equal("42", Run("let result = conv.toStr(42);"));
    }

    [Fact]
    public void BuiltIn_ToStr_Null()
    {
        Assert.Equal("null", Run("let result = conv.toStr(null);"));
    }

    [Fact]
    public void BuiltIn_ToInt_ValidString()
    {
        Assert.Equal(42L, Run("let result = conv.toInt(\"42\");"));
    }

    [Fact]
    public void BuiltIn_ToInt_InvalidString_ThrowsError()
    {
        RunExpectingError("let x = conv.toInt(\"abc\");");
    }

    [Fact]
    public void BuiltIn_ToInt_FromFloat()
    {
        Assert.Equal(3L, Run("let result = conv.toInt(3.7);"));
    }

    [Fact]
    public void BuiltIn_ToFloat_FromInt()
    {
        Assert.Equal(42.0, Run("let result = conv.toFloat(42);"));
    }

    [Fact]
    public void BuiltIn_ToFloat_FromString()
    {
        Assert.Equal(3.14, Run("let result = conv.toFloat(\"3.14\");"));
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
        Assert.Equal("[1, 2, 3]", Run("let result = conv.toStr([1, 2, 3]);"));
    }

    [Fact]
    public void ArrayStringify_Nested()
    {
        Assert.Equal("[[1, 2], [3]]", Run("let result = conv.toStr([[1, 2], [3]]);"));
    }

    [Fact]
    public void ArrayStringify_Empty()
    {
        Assert.Equal("[]", Run("let result = conv.toStr([]);"));
    }

    [Fact]
    public void ArrayStringify_MixedTypes()
    {
        Assert.Equal("[1, hello, true, null]", Run("let result = conv.toStr([1, \"hello\", true, null]);"));
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

    // ===== Struct Methods =====

    [Fact]
    public void StructMethod_BasicCall()
    {
        Assert.Equal(42L, Run("struct S { x, fn getX() { return self.x; } } let s = S { x: 42 }; let result = s.getX();"));
    }

    [Fact]
    public void StructMethod_WithParams()
    {
        Assert.Equal(15L, Run("struct S { x, fn add(y) { return self.x + y; } } let s = S { x: 10 }; let result = s.add(5);"));
    }

    [Fact]
    public void StructMethod_AccessMultipleFields()
    {
        Assert.Equal(30L, Run("struct P { x, y, fn sum() { return self.x + self.y; } } let p = P { x: 10, y: 20 }; let result = p.sum();"));
    }

    [Fact]
    public void StructMethod_ReturnsInstance()
    {
        Assert.Equal(65L, Run(@"
            struct P { x, y,
                fn add(other) {
                    return P { x: self.x + other.x, y: self.y + other.y };
                }
            }
            let a = P { x: 20, y: 30 };
            let b = P { x: 5, y: 10 };
            let c = a.add(b);
            let result = c.x + c.y;
        "));
    }

    [Fact]
    public void StructMethod_MultipleMethods()
    {
        Assert.Equal(16L, Run(@"
            struct Rect { w, h,
                fn area() { return self.w * self.h; }
                fn perimeter() { return 2 * (self.w + self.h); }
            }
            let r = Rect { w: 2, h: 3 };
            let result = r.area() + r.perimeter();
        "));
    }

    [Fact]
    public void StructMethod_NoParams()
    {
        Assert.Equal("hello", Run("struct G { fn greet() { return \"hello\"; } } let g = G {}; let result = g.greet();"));
    }

    [Fact]
    public void StructMethod_DefaultParam()
    {
        Assert.Equal(11L, Run("struct C { val, fn inc(n = 1) { return self.val + n; } } let c = C { val: 10 }; let result = c.inc();"));
    }

    [Fact]
    public void StructMethod_DefaultParam_Overridden()
    {
        Assert.Equal(15L, Run("struct C { val, fn inc(n = 1) { return self.val + n; } } let c = C { val: 10 }; let result = c.inc(5);"));
    }

    [Fact]
    public void StructMethod_ModifySelf()
    {
        Assert.Equal(99L, Run(@"
            struct Box { value,
                fn set(v) { self.value = v; }
            }
            let b = Box { value: 0 };
            b.set(99);
            let result = b.value;
        "));
    }

    [Fact]
    public void StructMethod_ChainingCalls()
    {
        Assert.Equal(42L, Run(@"
            struct Builder { val,
                fn build() { return self.val; }
            }
            let b = Builder { val: 42 };
            let result = b.build();
        "));
    }

    [Fact]
    public void StructMethod_CallOnNewInstance()
    {
        Assert.Equal(5L, Run("struct S { x, fn getX() { return self.x; } } let result = S { x: 5 }.getX();"));
    }

    [Fact]
    public void StructMethod_UndefinedMethod_Throws()
    {
        RunExpectingError("struct S { x } let s = S { x: 1 }; s.noSuchMethod();");
    }

    [Fact]
    public void StructMethod_WrongArity_Throws()
    {
        RunExpectingError("struct S { fn f(a) { return a; } } let s = S {}; s.f(1, 2);");
    }

    [Fact]
    public void StructMethod_SelfNotLeaked()
    {
        // self should not be accessible outside of methods
        RunExpectingError("struct S { x, fn getX() { return self.x; } } let s = S { x: 1 }; let r = self;");
    }

    [Fact]
    public void StructMethod_TypeofBoundMethod()
    {
        Assert.Equal("function", Run("struct S { fn f() { return 1; } } let s = S {}; let result = typeof(s.f);"));
    }

    [Fact]
    public void StructMethod_MethodCallsMethod()
    {
        Assert.Equal(10L, Run(@"
            struct S { x,
                fn double() { return self.x * 2; }
                fn quadruple() { return self.double() * 2; }
            }
            let s = S { x: 5 };
            let result = s.double();
        "));
    }

    [Fact]
    public void StructMethod_MethodCallsOtherMethod()
    {
        Assert.Equal(20L, Run(@"
            struct S { x,
                fn double() { return self.x * 2; }
                fn quadruple() { return self.double() * 2; }
            }
            let s = S { x: 5 };
            let result = s.quadruple();
        "));
    }

    [Fact]
    public void StructMethod_SharedAcrossInstances()
    {
        Assert.Equal(30L, Run(@"
            struct P { x,
                fn getX() { return self.x; }
            }
            let a = P { x: 10 };
            let b = P { x: 20 };
            let result = a.getX() + b.getX();
        "));
    }

    [Fact]
    public void StructMethod_WithClosure()
    {
        Assert.Equal(15L, Run(@"
            let factor = 3;
            struct S { x,
                fn scaled() { return self.x * factor; }
            }
            let s = S { x: 5 };
            let result = s.scaled();
        "));
    }

    [Fact]
    public void StructMethod_FieldsAndMethodsSameName_FieldWins()
    {
        // If a field has the same name as a method, field access should win
        Assert.Equal(42L, Run(@"
            struct S { x,
                fn x() { return 99; }
            }
            let s = S { x: 42 };
            let result = s.x;
        "));
    }

    [Fact]
    public void StructMethod_EmptyStruct_WithMethod()
    {
        Assert.Equal(42L, Run("struct S { fn answer() { return 42; } } let s = S {}; let result = s.answer();"));
    }

    [Fact]
    public void StructMethod_MethodAssign_Throws()
    {
        // Cannot assign to a method — methods are looked up on the struct template, not as fields
        // Attempting to set a field that doesn't exist should fail
        RunExpectingError("struct S { fn f() { return 1; } } let s = S {}; s.f = 42;");
    }

    // Struct toStr

    [Fact]
    public void StructToStr()
    {
        var result = Run("struct P { x } let p = P { x: 1 }; let result = conv.toStr(p);");
        Assert.Equal("P { x: 1 }", result);
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
        Assert.Equal("Color.Red", Run("enum Color { Red, Green, Blue } let result = conv.toStr(Color.Red);"));
    }

    [Fact]
    public void EnumDecl_Assignment()
    {
        Assert.Equal("Status.Active", Run("enum Status { Active, Inactive } let s = Status.Active; let result = conv.toStr(s);"));
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
        var result = Run("let r = $(cat /dev/null/nonexistent); let result = r.stderr;");
        Assert.IsType<string>(result);
        Assert.True(((string)result!).Length > 0, "Expected stderr output from cat on a nonexistent path");
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
            var result = Run($"let result = fs.readFile(\"{tmpFile.Replace("\\", "\\\\")}\");");
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
        RunExpectingError("fs.readFile(\"/nonexistent/file/path/xyz.txt\");");
    }

    [Fact]
    public void ReadFile_NonStringArg_ThrowsError()
    {
        RunExpectingError("fs.readFile(42);");
    }

    [Fact]
    public void WriteFile_WritesContent()
    {
        string tmpFile = System.IO.Path.GetTempFileName();
        try
        {
            Run($"fs.writeFile(\"{tmpFile.Replace("\\", "\\\\")}\", \"hello world\"); let result = 1;");
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
        RunExpectingError("fs.writeFile(42, \"content\");");
    }

    [Fact]
    public void WriteFile_NonStringContent_ThrowsError()
    {
        RunExpectingError("fs.writeFile(\"/tmp/test\", 42);");
    }

    [Fact]
    public void WriteFile_ReadFile_Roundtrip()
    {
        string tmpFile = System.IO.Path.GetTempFileName();
        try
        {
            var result = Run($"fs.writeFile(\"{tmpFile.Replace("\\", "\\\\")}\", \"roundtrip data\"); let result = fs.readFile(\"{tmpFile.Replace("\\", "\\\\")}\");");
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
        var result = Run("let result = env.get(\"PATH\");");
        Assert.IsType<string>(result);
        Assert.NotNull(result);
        Assert.True(((string)result!).Length > 0);
    }

    [Fact]
    public void Env_NonExistentVariable_ReturnsNull()
    {
        Assert.Null(Run("let result = env.get(\"STASH_NONEXISTENT_VAR_XYZ_12345\");"));
    }

    [Fact]
    public void Env_NonStringArg_ThrowsError()
    {
        RunExpectingError("env.get(42);");
    }

    [Fact]
    public void SetEnv_SetsVariable()
    {
        var result = Run("env.set(\"STASH_TEST_VAR\", \"test_value\"); let result = env.get(\"STASH_TEST_VAR\");");
        Assert.Equal("test_value", result);
    }

    [Fact]
    public void SetEnv_NonStringName_ThrowsError()
    {
        RunExpectingError("env.set(42, \"value\");");
    }

    [Fact]
    public void SetEnv_NonStringValue_ThrowsError()
    {
        RunExpectingError("env.set(\"name\", 42);");
    }

    // ===== Phase 4: exit Built-in =====

    [Fact]
    public void Exit_NonIntegerArg_ThrowsError()
    {
        RunExpectingError("process.exit(\"not a number\");");
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
        Assert.Equal("function", Run("let result = typeof(io.println);"));
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
        Assert.Null(Run("let result = try conv.toInt(\"abc\");"));
    }

    [Fact]
    public void TryExpr_NoError_ReturnsValue()
    {
        Assert.Equal(42L, Run("let result = try conv.toInt(\"42\");"));
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
        Assert.Equal(42L, Run("let result = 42 ?? conv.toInt(\"bad\");"));
    }

    [Fact]
    public void NullCoalesce_WithTry_CombinedPattern()
    {
        // The canonical pattern: try expr ?? default
        Assert.Equal("default", Run("let result = try conv.toInt(\"abc\") ?? \"default\";"));
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
            let x = try conv.toInt(""abc"");
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
            let x = try conv.toInt(""abc"");
            let y = try conv.toInt(""42"");
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
        Assert.Equal("fallback", Run("let result = try fs.readFile(\"/nonexistent/path/file.txt\") ?? \"fallback\";"));
    }

    [Fact]
    public void TryNullCoalesce_NestedCalls()
    {
        // try (try expr ?? default1) ?? default2
        Assert.Equal(42L, Run("let result = try conv.toInt(\"42\") ?? 0;"));
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

    // --- Compound Assignment Operators (+=, -=, *=, /=, %=, ??=) ---

    [Fact]
    public void CompoundAdd_AddsAndAssigns()
    {
        Assert.Equal(8L, Run("let x = 5; x += 3; let result = x;"));
    }

    [Fact]
    public void CompoundSubtract_SubtractsAndAssigns()
    {
        Assert.Equal(6L, Run("let x = 10; x -= 4; let result = x;"));
    }

    [Fact]
    public void CompoundMultiply_MultipliesAndAssigns()
    {
        Assert.Equal(12L, Run("let x = 3; x *= 4; let result = x;"));
    }

    [Fact]
    public void CompoundDivide_DividesAndAssigns()
    {
        Assert.Equal(4L, Run("let x = 20; x /= 5; let result = x;"));
    }

    [Fact]
    public void CompoundModulo_ModuloAndAssigns()
    {
        Assert.Equal(2L, Run("let x = 17; x %= 5; let result = x;"));
    }

    [Fact]
    public void CompoundNullCoalesce_AssignsWhenNull()
    {
        Assert.Equal("default", Run("let x = null; x ??= \"default\"; let result = x;"));
    }

    [Fact]
    public void CompoundNullCoalesce_DoesNotAssignWhenNonNull()
    {
        Assert.Equal("hello", Run("let x = \"hello\"; x ??= \"default\"; let result = x;"));
    }

    [Fact]
    public void CompoundAdd_StringConcatenation()
    {
        Assert.Equal("hello world", Run("let s = \"hello\"; s += \" world\"; let result = s;"));
    }

    [Fact]
    public void CompoundAdd_OnStructField()
    {
        Assert.Equal(8L, Run("struct Foo { count } let f = Foo { count: 5 }; f.count += 3; let result = f.count;"));
    }

    [Fact]
    public void CompoundAdd_OnArrayIndex()
    {
        Assert.Equal(11L, Run("let arr = [1, 2, 3]; arr[0] += 10; let result = arr[0];"));
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

    [Fact]
    public void Import_ModuleWithTopLevelFunctionCall()
    {
        // Module defines a function and calls it at the top level — this previously
        // failed with "Undefined variable 'greet'" because the Resolver skipped
        // top-level declarations and the Interpreter fell back to _globals instead
        // of _environment.
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "utils.stash");
            System.IO.File.WriteAllText(modulePath,
                "let greeting = \"\";\n" +
                "fn greet(name) { greeting = name; }\n" +
                "greet(\"loaded\");\n");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import { greeting } from \"utils.stash\"; let result = greeting;";

            Assert.Equal("loaded", RunWithFile(source, mainPath));
        }
        finally
        {
            System.IO.Directory.Delete(tmpDir, true);
        }
    }

    // ===== Phase 6: Namespaces =====

    // -- fs namespace --

    [Fact]
    public void Fs_Exists_TrueForExistingFile()
    {
        string tmpFile = System.IO.Path.GetTempFileName();
        try
        {
            string source = $"let result = fs.exists(\"{tmpFile.Replace("\\", "\\\\")}\");";
            Assert.Equal(true, Run(source));
        }
        finally { System.IO.File.Delete(tmpFile); }
    }

    [Fact]
    public void Fs_Exists_FalseForNonExistent()
    {
        string source = "let result = fs.exists(\"/tmp/stash_nonexistent_file_xyz_12345\");";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void Fs_DirExists_TrueForExistingDir()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string source = $"let result = fs.dirExists(\"{tmpDir.Replace("\\", "\\\\")}\");";
            Assert.Equal(true, Run(source));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void Fs_DirExists_FalseForNonExistent()
    {
        string source = "let result = fs.dirExists(\"/tmp/stash_nonexistent_dir_xyz_12345\");";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void Fs_PathExists_TrueForFile()
    {
        string tmpFile = System.IO.Path.GetTempFileName();
        try
        {
            string source = $"let result = fs.pathExists(\"{tmpFile.Replace("\\", "\\\\")}\");";
            Assert.Equal(true, Run(source));
        }
        finally { System.IO.File.Delete(tmpFile); }
    }

    [Fact]
    public void Fs_PathExists_TrueForDir()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string source = $"let result = fs.pathExists(\"{tmpDir.Replace("\\", "\\\\")}\");";
            Assert.Equal(true, Run(source));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void Fs_PathExists_FalseForNonExistent()
    {
        string source = "let result = fs.pathExists(\"/tmp/stash_nonexistent_xyz_12345\");";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void Fs_WriteFile_And_ReadFile_Roundtrip()
    {
        string tmpFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            string source = $"fs.writeFile(\"{tmpFile.Replace("\\", "\\\\")}\", \"hello world\"); let result = fs.readFile(\"{tmpFile.Replace("\\", "\\\\")}\");";
            Assert.Equal("hello world", Run(source));
        }
        finally { if (System.IO.File.Exists(tmpFile))
            {
                System.IO.File.Delete(tmpFile);
            }
        }
    }

    [Fact]
    public void Fs_AppendFile()
    {
        string tmpFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            string escaped = tmpFile.Replace("\\", "\\\\");
            string source = $"fs.writeFile(\"{escaped}\", \"hello\"); fs.appendFile(\"{escaped}\", \" world\"); let result = fs.readFile(\"{escaped}\");";
            Assert.Equal("hello world", Run(source));
        }
        finally { if (System.IO.File.Exists(tmpFile))
            {
                System.IO.File.Delete(tmpFile);
            }
        }
    }

    [Fact]
    public void Fs_CreateDir()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            string source = $"fs.createDir(\"{tmpDir.Replace("\\", "\\\\")}\"); let result = fs.dirExists(\"{tmpDir.Replace("\\", "\\\\")}\");";
            Assert.Equal(true, Run(source));
        }
        finally { if (System.IO.Directory.Exists(tmpDir))
            {
                System.IO.Directory.Delete(tmpDir, true);
            }
        }
    }

    [Fact]
    public void Fs_Delete_File()
    {
        string tmpFile = System.IO.Path.GetTempFileName();
        string escaped = tmpFile.Replace("\\", "\\\\");
        string source = $"fs.delete(\"{escaped}\"); let result = fs.exists(\"{escaped}\");";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void Fs_Delete_Directory()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        string escaped = tmpDir.Replace("\\", "\\\\");
        string source = $"fs.delete(\"{escaped}\"); let result = fs.dirExists(\"{escaped}\");";
        Assert.Equal(false, Run(source));
    }

    [Fact]
    public void Fs_Delete_NonExistent_Throws()
    {
        RunExpectingError("fs.delete(\"/tmp/stash_nonexistent_xyz_12345\");");
    }

    [Fact]
    public void Fs_Copy()
    {
        string tmpFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_src_" + System.Guid.NewGuid().ToString("N") + ".txt");
        string copyFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_dst_" + System.Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            System.IO.File.WriteAllText(tmpFile, "copy me");
            string source = $"fs.copy(\"{tmpFile.Replace("\\", "\\\\")}\", \"{copyFile.Replace("\\", "\\\\")}\"); let result = fs.readFile(\"{copyFile.Replace("\\", "\\\\")}\");";
            Assert.Equal("copy me", Run(source));
        }
        finally
        {
            if (System.IO.File.Exists(tmpFile))
            {
                System.IO.File.Delete(tmpFile);
            }

            if (System.IO.File.Exists(copyFile))
            {
                System.IO.File.Delete(copyFile);
            }
        }
    }

    [Fact]
    public void Fs_Move()
    {
        string srcFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_src_" + System.Guid.NewGuid().ToString("N") + ".txt");
        string dstFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_dst_" + System.Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            System.IO.File.WriteAllText(srcFile, "move me");
            string srcEsc = srcFile.Replace("\\", "\\\\");
            string dstEsc = dstFile.Replace("\\", "\\\\");
            string source = $"fs.move(\"{srcEsc}\", \"{dstEsc}\"); let result = fs.readFile(\"{dstEsc}\");";
            Assert.Equal("move me", Run(source));
            Assert.False(System.IO.File.Exists(srcFile));
        }
        finally
        {
            if (System.IO.File.Exists(srcFile))
            {
                System.IO.File.Delete(srcFile);
            }

            if (System.IO.File.Exists(dstFile))
            {
                System.IO.File.Delete(dstFile);
            }
        }
    }

    [Fact]
    public void Fs_Size()
    {
        string tmpFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            System.IO.File.WriteAllText(tmpFile, "12345");
            string source = $"let result = fs.size(\"{tmpFile.Replace("\\", "\\\\")}\");";
            Assert.Equal(5L, Run(source));
        }
        finally { if (System.IO.File.Exists(tmpFile))
            {
                System.IO.File.Delete(tmpFile);
            }
        }
    }

    [Fact]
    public void Fs_ListDir()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(tmpDir, "a.txt"), "");
            System.IO.File.WriteAllText(System.IO.Path.Combine(tmpDir, "b.txt"), "");
            string source = $"let result = len(fs.listDir(\"{tmpDir.Replace("\\", "\\\\")}\"));";
            Assert.Equal(2L, Run(source));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void Fs_ReadFile_NonExistent_Throws()
    {
        RunExpectingError("fs.readFile(\"/tmp/stash_nonexistent_xyz_12345\");");
    }

    [Fact]
    public void Fs_ReadFile_NonStringArg_Throws()
    {
        RunExpectingError("let result = fs.readFile(42);");
    }

    // -- path namespace --

    [Fact]
    public void Path_Dir()
    {
        string source = "let result = path.dir(\"/home/user/file.txt\");";
        Assert.Equal("/home/user", Run(source));
    }

    [Fact]
    public void Path_Base()
    {
        string source = "let result = path.base(\"/home/user/file.txt\");";
        Assert.Equal("file.txt", Run(source));
    }

    [Fact]
    public void Path_Ext()
    {
        string source = "let result = path.ext(\"/home/user/file.txt\");";
        Assert.Equal(".txt", Run(source));
    }

    [Fact]
    public void Path_Name()
    {
        string source = "let result = path.name(\"/home/user/file.txt\");";
        Assert.Equal("file", Run(source));
    }

    [Fact]
    public void Path_Join()
    {
        string source = "let result = path.join(\"/home/user\", \"file.txt\");";
        Assert.Equal(System.IO.Path.Combine("/home/user", "file.txt"), Run(source));
    }

    [Fact]
    public void Path_Abs()
    {
        string source = "let result = path.abs(\".\");";
        var absResult = Run(source);
        Assert.IsType<string>(absResult);
        Assert.True(((string)absResult!).Length > 1);
    }

    // -- Namespace general behavior --

    [Fact]
    public void Namespace_Typeof_ReturnsNamespace()
    {
        string source = "let result = typeof(fs);";
        Assert.Equal("namespace", Run(source));
    }

    [Fact]
    public void Namespace_Typeof_PathNamespace()
    {
        string source = "let result = typeof(path);";
        Assert.Equal("namespace", Run(source));
    }

    [Fact]
    public void Namespace_AssignToMember_Throws()
    {
        RunExpectingError("fs.exists = 42;");
    }

    [Fact]
    public void Namespace_NonExistentMember_Throws()
    {
        RunExpectingError("let result = fs.nonExistentFunction();");
    }

    // -- import-as --

    [Fact]
    public void ImportAs_FunctionAccess()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "math.stash");
            System.IO.File.WriteAllText(modulePath, "fn add(a, b) { return a + b; }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import \"math.stash\" as math; let result = math.add(3, 4);";

            Assert.Equal(7L, RunWithFile(source, mainPath));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ImportAs_ConstantAccess()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "config.stash");
            System.IO.File.WriteAllText(modulePath, "const MAX = 100;");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import \"config.stash\" as cfg; let result = cfg.MAX;";

            Assert.Equal(100L, RunWithFile(source, mainPath));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ImportAs_EnumAccess()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "colors.stash");
            System.IO.File.WriteAllText(modulePath, "enum Color { Red, Green, Blue }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import \"colors.stash\" as colors; let result = colors.Color.Red == colors.Color.Red;";

            Assert.Equal(true, RunWithFile(source, mainPath));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ImportAs_StructAccess()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "types.stash");
            System.IO.File.WriteAllText(modulePath, "struct Point { x, y }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import \"types.stash\" as t; let p = t.Point { x: 10, y: 20 }; let result = p.x;";

            Assert.Equal(10L, RunWithFile(source, mainPath));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ImportAs_MultipleFunctions()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "utils.stash");
            System.IO.File.WriteAllText(modulePath, "fn add(a, b) { return a + b; } fn mul(a, b) { return a * b; }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import \"utils.stash\" as u; let result = u.add(2, 3) + u.mul(4, 5);";

            Assert.Equal(25L, RunWithFile(source, mainPath));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ImportAs_TypeofReturnsNamespace()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "mod.stash");
            System.IO.File.WriteAllText(modulePath, "fn foo() { return 1; }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import \"mod.stash\" as m; let result = typeof(m);";

            Assert.Equal("namespace", RunWithFile(source, mainPath));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ImportAs_AssignToNamespaceMember_Throws()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "mod.stash");
            System.IO.File.WriteAllText(modulePath, "let x = 1;");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import \"mod.stash\" as m; m.x = 42;";

            var lexer = new Lexer(source, mainPath);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var statements = parser.ParseProgram();
            var interpreter = new Interpreter();
            interpreter.CurrentFile = mainPath;
            Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ImportAs_NonExistentModule_Throws()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import \"nonexistent.stash\" as m;";

            var lexer = new Lexer(source, mainPath);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var statements = parser.ParseProgram();
            var interpreter = new Interpreter();
            interpreter.CurrentFile = mainPath;
            Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void ImportAs_NonExistentMemberAccess_Throws()
    {
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash_test_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        try
        {
            string modulePath = System.IO.Path.Combine(tmpDir, "mod.stash");
            System.IO.File.WriteAllText(modulePath, "fn foo() { return 1; }");

            string mainPath = System.IO.Path.Combine(tmpDir, "main.stash");
            string source = "import \"mod.stash\" as m; let result = m.bar();";

            var lexer = new Lexer(source, mainPath);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var statements = parser.ParseProgram();
            var interpreter = new Interpreter();
            interpreter.CurrentFile = mainPath;
            Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
        }
        finally { System.IO.Directory.Delete(tmpDir, true); }
    }

    // -- io, conv, env, process namespaces --

    [Fact]
    public void IoNamespace_Typeof()
    {
        Assert.Equal("namespace", Run("let result = typeof(io);"));
    }

    [Fact]
    public void ConvNamespace_Typeof()
    {
        Assert.Equal("namespace", Run("let result = typeof(conv);"));
    }

    [Fact]
    public void EnvNamespace_Typeof()
    {
        Assert.Equal("namespace", Run("let result = typeof(env);"));
    }

    [Fact]
    public void ProcessNamespace_Typeof()
    {
        Assert.Equal("namespace", Run("let result = typeof(process);"));
    }

    [Fact]
    public void ConvNamespace_ToStr_Int()
    {
        Assert.Equal("42", Run("let result = conv.toStr(42);"));
    }

    [Fact]
    public void ConvNamespace_ToStr_Null()
    {
        Assert.Equal("null", Run("let result = conv.toStr(null);"));
    }

    [Fact]
    public void ConvNamespace_ToInt_String()
    {
        Assert.Equal(42L, Run("let result = conv.toInt(\"42\");"));
    }

    [Fact]
    public void ConvNamespace_ToInt_Float()
    {
        Assert.Equal(3L, Run("let result = conv.toInt(3.7);"));
    }

    [Fact]
    public void ConvNamespace_ToInt_InvalidString_Throws()
    {
        RunExpectingError("let result = conv.toInt(\"abc\");");
    }

    [Fact]
    public void ConvNamespace_ToFloat_Int()
    {
        Assert.Equal(42.0, Run("let result = conv.toFloat(42);"));
    }

    [Fact]
    public void ConvNamespace_ToFloat_String()
    {
        Assert.Equal(3.14, Run("let result = conv.toFloat(\"3.14\");"));
    }

    [Fact]
    public void EnvNamespace_Get_ExistingVar()
    {
        var result = Run("let result = env.get(\"PATH\");");
        Assert.NotNull(result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public void EnvNamespace_Get_NonExistent()
    {
        Assert.Null(Run("let result = env.get(\"STASH_NONEXISTENT_VAR_XYZ_12345\");"));
    }

    [Fact]
    public void EnvNamespace_Get_NonStringArg_Throws()
    {
        RunExpectingError("env.get(42);");
    }

    [Fact]
    public void EnvNamespace_Set_And_Get()
    {
        Assert.Equal("test_value", Run("env.set(\"STASH_TEST_NS_VAR\", \"test_value\"); let result = env.get(\"STASH_TEST_NS_VAR\");"));
    }

    [Fact]
    public void EnvNamespace_Set_NonStringName_Throws()
    {
        RunExpectingError("env.set(42, \"value\");");
    }

    [Fact]
    public void EnvNamespace_Set_NonStringValue_Throws()
    {
        RunExpectingError("env.set(\"name\", 42);");
    }

    [Fact]
    public void ProcessNamespace_Exit_NonInteger_Throws()
    {
        RunExpectingError("process.exit(\"not a number\");");
    }

    // ── process.exists ──────────────────────────────────────────────

    [Fact]
    public void ProcessExists_CurrentProcess_ReturnsTrue()
    {
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        Assert.Equal(true, Run($"let result = process.exists({pid});"));
    }

    [Fact]
    public void ProcessExists_InvalidPid_ReturnsFalse()
    {
        Assert.Equal(false, Run("let result = process.exists(99999999);"));
    }

    [Fact]
    public void ProcessExists_NonInteger_Throws()
    {
        RunExpectingError("process.exists(\"not a pid\");");
    }

    // ── process.find ────────────────────────────────────────────────

    [Fact]
    public void ProcessFind_ReturnsArray()
    {
        Assert.IsType<List<object?>>(Run("let result = process.find(\"__nonexistent_process_xyz__\");"));
    }

    [Fact]
    public void ProcessFind_NonexistentName_ReturnsEmptyArray()
    {
        var result = Run("let result = len(process.find(\"__stash_nonexistent_xyz__\"));");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void ProcessFind_NonString_Throws()
    {
        RunExpectingError("process.find(42);");
    }

    // ── process.daemonize ───────────────────────────────────────────

    [Fact]
    public void ProcessDaemonize_ReturnsProcessHandle()
    {
        var result = Run("let p = process.daemonize(\"sleep 10\"); let result = typeof(p);");
        Assert.Equal("struct", result);
    }

    [Fact]
    public void ProcessDaemonize_NotTracked()
    {
        var result = Run(@"
            let p = process.daemonize(""sleep 10"");
            let tracked = process.list();
            let result = len(tracked);
        ");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void ProcessDaemonize_NonString_Throws()
    {
        RunExpectingError("process.daemonize(42);");
    }

    // ── process.waitAll ─────────────────────────────────────────────

    [Fact]
    public void ProcessWaitAll_ReturnsArrayOfResults()
    {
        var result = Run(@"
            let p1 = process.spawn(""echo hello"");
            let p2 = process.spawn(""echo world"");
            let results = process.waitAll([p1, p2]);
            let result = len(results);
        ");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void ProcessWaitAll_ResultsContainStdout()
    {
        var result = Run(@"
            let p1 = process.spawn(""echo hello"");
            let results = process.waitAll([p1]);
            let result = str.trim(results[0].stdout);
        ");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ProcessWaitAll_EmptyArray_ReturnsEmptyArray()
    {
        var result = Run("let result = len(process.waitAll([]));");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void ProcessWaitAll_NonArray_Throws()
    {
        RunExpectingError("process.waitAll(\"not an array\");");
    }

    [Fact]
    public void ProcessWaitAll_NonProcessInArray_Throws()
    {
        RunExpectingError("process.waitAll([42]);");
    }

    // ── process.waitAny ─────────────────────────────────────────────

    [Fact]
    public void ProcessWaitAny_ReturnsCommandResult()
    {
        var result = Run(@"
            let p1 = process.spawn(""echo fast"");
            let p2 = process.spawn(""sleep 10"");
            let r = process.waitAny([p1, p2]);
            process.kill(p2);
            let result = typeof(r);
        ");
        Assert.Equal("struct", result);
    }

    [Fact]
    public void ProcessWaitAny_EmptyArray_Throws()
    {
        RunExpectingError("process.waitAny([]);");
    }

    [Fact]
    public void ProcessWaitAny_NonArray_Throws()
    {
        RunExpectingError("process.waitAny(\"not an array\");");
    }

    [Fact]
    public void ProcessWaitAny_NonProcessInArray_Throws()
    {
        RunExpectingError("process.waitAny([42]);");
    }

    // ── process.onExit ──────────────────────────────────────────────

    [Fact]
    public void ProcessOnExit_NonProcess_Throws()
    {
        RunExpectingError("process.onExit(42, (r) => r);");
    }

    [Fact]
    public void ProcessOnExit_NonFunction_Throws()
    {
        RunExpectingError(@"
            let p = process.spawn(""echo hi"");
            process.onExit(p, ""not a function"");
        ");
    }

    [Fact]
    public void Namespace_AssignToIoMember_Throws()
    {
        RunExpectingError("io.println = 42;");
    }

    [Fact]
    public void Namespace_AssignToConvMember_Throws()
    {
        RunExpectingError("conv.toStr = 42;");
    }

    [Fact]
    public void Namespace_NonExistentMember_OnIo_Throws()
    {
        RunExpectingError("let result = io.missing();");
    }

    [Fact]
    public void Namespace_NonExistentMember_OnConv_Throws()
    {
        RunExpectingError("let result = conv.missing();");
    }

    // ── Lambda / Arrow function tests ───────────────────────────────

    [Fact]
    public void Lambda_ExpressionBody_ReturnsValue()
    {
        Assert.Equal(6L, Run("let double = (x) => x * 2; let result = double(3);"));
    }

    [Fact]
    public void Lambda_BlockBody_ReturnsValue()
    {
        Assert.Equal(6L, Run("let double = (x) => { return x * 2; }; let result = double(3);"));
    }

    [Fact]
    public void Lambda_NoParams_ReturnsValue()
    {
        Assert.Equal(42L, Run("let f = () => 42; let result = f();"));
    }

    [Fact]
    public void Lambda_MultipleParams_ReturnsValue()
    {
        Assert.Equal(10L, Run("let add = (a, b) => a + b; let result = add(3, 7);"));
    }

    [Fact]
    public void Lambda_Closure_CapturesEnvironment()
    {
        Assert.Equal(15L, Run("let x = 10; let addX = (y) => x + y; let result = addX(5);"));
    }

    [Fact]
    public void Lambda_PassedAsArgument()
    {
        Assert.Equal(9L, Run("fn apply(f, x) { return f(x); } let result = apply((x) => x * x, 3);"));
    }

    [Fact]
    public void Lambda_ReturnedFromFunction()
    {
        Assert.Equal(8L, Run("fn makeMultiplier(m) { return (x) => x * m; } let triple = makeMultiplier(4); let result = triple(2);"));
    }

    [Fact]
    public void Lambda_BlockBody_ImplicitNullReturn()
    {
        Assert.Null(Run("let f = () => { let x = 1; }; let result = f();"));
    }

    [Fact]
    public void Lambda_ExpressionBody_TernaryExpression()
    {
        Assert.Equal("yes", Run("let check = (x) => x > 0 ? \"yes\" : \"no\"; let result = check(5);"));
    }

    [Fact]
    public void Lambda_NestedLambda()
    {
        Assert.Equal(7L, Run("let add = (a) => (b) => a + b; let add3 = add(3); let result = add3(4);"));
    }

    [Fact]
    public void Lambda_InArrayElement()
    {
        Assert.Equal(4L, Run("let ops = [(x) => x + 1, (x) => x * 2]; let result = ops[1](2);"));
    }

    [Fact]
    public void Lambda_MutatesClosure()
    {
        Assert.Equal(3L, Run("fn makeCounter() { let count = 0; return () => { count = count + 1; return count; }; } let counter = makeCounter(); counter(); counter(); let result = counter();"));
    }

    [Fact]
    public void Lambda_GroupingStillWorks()
    {
        Assert.Equal(9L, Run("let result = (1 + 2) * 3;"));
    }

    // Switch expression

    [Fact]
    public void Switch_MatchesFirstArm()
    {
        Assert.Equal("one", Eval("1 switch { 1 => \"one\", 2 => \"two\" }"));
    }

    [Fact]
    public void Switch_MatchesSecondArm()
    {
        Assert.Equal("two", Eval("2 switch { 1 => \"one\", 2 => \"two\" }"));
    }

    [Fact]
    public void Switch_DiscardArm()
    {
        Assert.Equal("other", Eval("99 switch { 1 => \"one\", _ => \"other\" }"));
    }

    [Fact]
    public void Switch_StringSubject()
    {
        Assert.Equal(1L, Eval("\"hello\" switch { \"hello\" => 1, \"world\" => 2 }"));
    }

    [Fact]
    public void Switch_BoolSubject()
    {
        Assert.Equal("yes", Eval("true switch { true => \"yes\", false => \"no\" }"));
    }

    [Fact]
    public void Switch_NullSubject()
    {
        Assert.Equal("nil", Eval("null switch { null => \"nil\", _ => \"other\" }"));
    }

    [Fact]
    public void Switch_NoMatchThrows()
    {
        Assert.Throws<RuntimeError>(() => Eval("3 switch { 1 => \"one\", 2 => \"two\" }"));
    }

    [Fact]
    public void Switch_WithExpression()
    {
        Assert.Equal("five", Run("let x = 5; let result = x switch { 5 => \"five\", _ => \"other\" };"));
    }

    [Fact]
    public void Switch_FirstMatchWins()
    {
        Assert.Equal("first", Eval("1 switch { 1 => \"first\", 1 => \"second\" }"));
    }

    [Fact]
    public void Switch_ExpressionBody()
    {
        Assert.Equal(22L, Eval("2 switch { 1 => 10 + 1, 2 => 20 + 2, _ => 0 }"));
    }

    // arr namespace tests

    // arr.push
    [Fact]
    public void ArrPush_AddsToEnd()
    {
        Assert.Equal(3L, Run("let a = [1, 2]; arr.push(a, 3); let result = a[2];"));
    }

    [Fact]
    public void ArrPush_IncreasesLength()
    {
        Assert.Equal(3L, Run("let a = [1, 2]; arr.push(a, 3); let result = len(a);"));
    }

    [Fact]
    public void ArrPush_NonArray_Throws()
    {
        RunExpectingError("arr.push(\"hello\", 1);");
    }

    // arr.pop
    [Fact]
    public void ArrPop_ReturnsLastElement()
    {
        Assert.Equal(3L, Run("let a = [1, 2, 3]; let result = arr.pop(a);"));
    }

    [Fact]
    public void ArrPop_RemovesLastElement()
    {
        Assert.Equal(2L, Run("let a = [1, 2, 3]; arr.pop(a); let result = len(a);"));
    }

    [Fact]
    public void ArrPop_EmptyArray_Throws()
    {
        RunExpectingError("let a = []; arr.pop(a);");
    }

    // arr.peek
    [Fact]
    public void ArrPeek_ReturnsLastElement()
    {
        Assert.Equal(3L, Run("let a = [1, 2, 3]; let result = arr.peek(a);"));
    }

    [Fact]
    public void ArrPeek_DoesNotRemoveElement()
    {
        Assert.Equal(3L, Run("let a = [1, 2, 3]; arr.peek(a); let result = len(a);"));
    }

    [Fact]
    public void ArrPeek_EmptyArray_Throws()
    {
        RunExpectingError("let a = []; arr.peek(a);");
    }

    // arr.insert
    [Fact]
    public void ArrInsert_AtBeginning()
    {
        Assert.Equal(0L, Run("let a = [1, 2, 3]; arr.insert(a, 0, 0); let result = a[0];"));
    }

    [Fact]
    public void ArrInsert_InMiddle()
    {
        Assert.Equal(10L, Run("let a = [1, 2, 3]; arr.insert(a, 1, 10); let result = a[1];"));
    }

    [Fact]
    public void ArrInsert_AtEnd()
    {
        Assert.Equal(4L, Run("let a = [1, 2, 3]; arr.insert(a, 3, 4); let result = a[3];"));
    }

    [Fact]
    public void ArrInsert_OutOfBounds_Throws()
    {
        RunExpectingError("let a = [1, 2]; arr.insert(a, 5, 99);");
    }

    [Fact]
    public void ArrInsert_NegativeIndex_Throws()
    {
        RunExpectingError("let a = [1, 2]; arr.insert(a, -1, 99);");
    }

    // arr.removeAt
    [Fact]
    public void ArrRemoveAt_ReturnsRemovedElement()
    {
        Assert.Equal(2L, Run("let a = [1, 2, 3]; let result = arr.removeAt(a, 1);"));
    }

    [Fact]
    public void ArrRemoveAt_DecreasesLength()
    {
        Assert.Equal(2L, Run("let a = [1, 2, 3]; arr.removeAt(a, 0); let result = len(a);"));
    }

    [Fact]
    public void ArrRemoveAt_OutOfBounds_Throws()
    {
        RunExpectingError("let a = [1, 2]; arr.removeAt(a, 5);");
    }

    // arr.remove
    [Fact]
    public void ArrRemove_FindsAndRemovesElement()
    {
        Assert.Equal(true, Run("let a = [1, 2, 3]; let result = arr.remove(a, 2);"));
    }

    [Fact]
    public void ArrRemove_DecreasesLength()
    {
        Assert.Equal(2L, Run("let a = [1, 2, 3]; arr.remove(a, 2); let result = len(a);"));
    }

    [Fact]
    public void ArrRemove_NotFound_ReturnsFalse()
    {
        Assert.Equal(false, Run("let a = [1, 2, 3]; let result = arr.remove(a, 99);"));
    }

    [Fact]
    public void ArrRemove_RemovesFirstOccurrence()
    {
        Assert.Equal(2L, Run("let a = [1, 2, 1, 3]; arr.remove(a, 1); let result = a[0];"));
    }

    // arr.clear
    [Fact]
    public void ArrClear_EmptiesArray()
    {
        Assert.Equal(0L, Run("let a = [1, 2, 3]; arr.clear(a); let result = len(a);"));
    }

    // arr.contains
    [Fact]
    public void ArrContains_Found()
    {
        Assert.Equal(true, Run("let a = [1, 2, 3]; let result = arr.contains(a, 2);"));
    }

    [Fact]
    public void ArrContains_NotFound()
    {
        Assert.Equal(false, Run("let a = [1, 2, 3]; let result = arr.contains(a, 99);"));
    }

    [Fact]
    public void ArrContains_StringValues()
    {
        Assert.Equal(true, Run("let a = [\"hello\", \"world\"]; let result = arr.contains(a, \"world\");"));
    }

    // arr.indexOf
    [Fact]
    public void ArrIndexOf_Found()
    {
        Assert.Equal(1L, Run("let a = [10, 20, 30]; let result = arr.indexOf(a, 20);"));
    }

    [Fact]
    public void ArrIndexOf_NotFound()
    {
        Assert.Equal(-1L, Run("let a = [10, 20, 30]; let result = arr.indexOf(a, 99);"));
    }

    // arr.slice
    [Fact]
    public void ArrSlice_Middle()
    {
        var result = Run("let a = [1, 2, 3, 4, 5]; let result = arr.slice(a, 1, 3);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(3L, list[1]);
    }

    [Fact]
    public void ArrSlice_Full()
    {
        var result = Run("let a = [1, 2, 3]; let result = arr.slice(a, 0, 3);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void ArrSlice_ClampsToEnd()
    {
        var result = Run("let a = [1, 2, 3]; let result = arr.slice(a, 1, 100);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(3L, list[1]);
    }

    // arr.concat
    [Fact]
    public void ArrConcat_MergesArrays()
    {
        var result = Run("let a = [1, 2]; let b = [3, 4]; let result = arr.concat(a, b);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(4, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(4L, list[3]);
    }

    [Fact]
    public void ArrConcat_DoesNotMutateOriginals()
    {
        Assert.Equal(2L, Run("let a = [1, 2]; let b = [3, 4]; arr.concat(a, b); let result = len(a);"));
    }

    // arr.join
    [Fact]
    public void ArrJoin_WithSeparator()
    {
        Assert.Equal("1, 2, 3", Run("let a = [1, 2, 3]; let result = arr.join(a, \", \");"));
    }

    [Fact]
    public void ArrJoin_EmptyArray()
    {
        Assert.Equal("", Run("let a = []; let result = arr.join(a, \", \");"));
    }

    [Fact]
    public void ArrJoin_MixedTypes()
    {
        Assert.Equal("1-hello-true", Run("let a = [1, \"hello\", true]; let result = arr.join(a, \"-\");"));
    }

    // arr.reverse
    [Fact]
    public void ArrReverse_ReversesInPlace()
    {
        Assert.Equal(3L, Run("let a = [1, 2, 3]; arr.reverse(a); let result = a[0];"));
    }

    [Fact]
    public void ArrReverse_LastBecomesFirst()
    {
        Assert.Equal(1L, Run("let a = [1, 2, 3]; arr.reverse(a); let result = a[2];"));
    }

    // arr.sort
    [Fact]
    public void ArrSort_SortsIntegers()
    {
        Assert.Equal(1L, Run("let a = [3, 1, 2]; arr.sort(a); let result = a[0];"));
    }

    [Fact]
    public void ArrSort_SortsStrings()
    {
        Assert.Equal("apple", Run("let a = [\"cherry\", \"apple\", \"banana\"]; arr.sort(a); let result = a[0];"));
    }

    [Fact]
    public void ArrSort_MixedTypes_Throws()
    {
        RunExpectingError("let a = [1, \"hello\"]; arr.sort(a);");
    }

    // arr.map
    [Fact]
    public void ArrMap_TransformsElements()
    {
        var result = Run("let a = [1, 2, 3]; let result = arr.map(a, (x) => x * 2);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
        Assert.Equal(6L, list[2]);
    }

    [Fact]
    public void ArrMap_ReturnsNewArray()
    {
        Assert.Equal(1L, Run("let a = [1, 2, 3]; let b = arr.map(a, (x) => x * 2); let result = a[0];"));
    }

    // arr.filter
    [Fact]
    public void ArrFilter_FiltersElements()
    {
        var result = Run("let a = [1, 2, 3, 4, 5]; let result = arr.filter(a, (x) => x > 3);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(4L, list[0]);
        Assert.Equal(5L, list[1]);
    }

    [Fact]
    public void ArrFilter_NoMatches_EmptyArray()
    {
        var result = Run("let a = [1, 2, 3]; let result = arr.filter(a, (x) => x > 10);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    // arr.forEach
    [Fact]
    public void ArrForEach_VisitsAllElements()
    {
        Assert.Equal(6L, Run("let a = [1, 2, 3]; let sum = 0; arr.forEach(a, (x) => { sum = sum + x; }); let result = sum;"));
    }

    // arr.find
    [Fact]
    public void ArrFind_ReturnsFirstMatch()
    {
        Assert.Equal(4L, Run("let a = [1, 2, 3, 4, 5]; let result = arr.find(a, (x) => x > 3);"));
    }

    [Fact]
    public void ArrFind_NoMatch_ReturnsNull()
    {
        Assert.Null(Run("let a = [1, 2, 3]; let result = arr.find(a, (x) => x > 10);"));
    }

    // arr.reduce
    [Fact]
    public void ArrReduce_SumsArray()
    {
        Assert.Equal(6L, Run("let a = [1, 2, 3]; let result = arr.reduce(a, (acc, x) => acc + x, 0);"));
    }

    [Fact]
    public void ArrReduce_WithInitialValue()
    {
        Assert.Equal(16L, Run("let a = [1, 2, 3]; let result = arr.reduce(a, (acc, x) => acc + x, 10);"));
    }

    [Fact]
    public void ArrReduce_BuildsString()
    {
        Assert.Equal("a-1-2-3", Run("let a = [1, 2, 3]; let result = arr.reduce(a, (acc, x) => acc + \"-\" + x, \"a\");"));
    }

    // arr.unique
    [Fact]
    public void ArrUnique_RemovesDuplicates()
    {
        var result = Run("let a = [1, 2, 2, 3, 1, 3]; let result = arr.unique(a);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(3L, list[2]);
    }

    [Fact]
    public void ArrUnique_PreservesOrder()
    {
        var result = Run("let a = [3, 1, 2, 1, 3]; let result = arr.unique(a);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(3L, list[0]);
        Assert.Equal(1L, list[1]);
        Assert.Equal(2L, list[2]);
    }

    [Fact]
    public void ArrUnique_EmptyArray()
    {
        var result = Run("let result = arr.unique([]);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void ArrUnique_MixedTypes()
    {
        var result = Run("let a = [1, \"1\", true, 1, \"1\"]; let result = arr.unique(a);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void ArrUnique_ReturnsNewArray()
    {
        Assert.Equal(3L, Run("let a = [1, 2, 2]; let b = arr.unique(a); let result = len(a);"));
    }

    // arr.any
    [Fact]
    public void ArrAny_TrueWhenMatch()
    {
        Assert.Equal(true, Run("let a = [1, 2, 3]; let result = arr.any(a, (x) => x > 2);"));
    }

    [Fact]
    public void ArrAny_FalseWhenNoMatch()
    {
        Assert.Equal(false, Run("let a = [1, 2, 3]; let result = arr.any(a, (x) => x > 10);"));
    }

    [Fact]
    public void ArrAny_EmptyArrayReturnsFalse()
    {
        Assert.Equal(false, Run("let result = arr.any([], (x) => true);"));
    }

    // arr.every
    [Fact]
    public void ArrEvery_TrueWhenAllMatch()
    {
        Assert.Equal(true, Run("let a = [2, 4, 6]; let result = arr.every(a, (x) => x % 2 == 0);"));
    }

    [Fact]
    public void ArrEvery_FalseWhenSomeFail()
    {
        Assert.Equal(false, Run("let a = [2, 3, 6]; let result = arr.every(a, (x) => x % 2 == 0);"));
    }

    [Fact]
    public void ArrEvery_EmptyArrayReturnsTrue()
    {
        Assert.Equal(true, Run("let result = arr.every([], (x) => false);"));
    }

    // arr.flat
    [Fact]
    public void ArrFlat_FlattensOneLevel()
    {
        var result = Run("let a = [[1, 2], [3, 4], [5]]; let result = arr.flat(a);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(5, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(5L, list[4]);
    }

    [Fact]
    public void ArrFlat_MixedNestedAndFlat()
    {
        var result = Run("let a = [1, [2, 3], 4]; let result = arr.flat(a);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(4, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(3L, list[2]);
        Assert.Equal(4L, list[3]);
    }

    [Fact]
    public void ArrFlat_DoesNotFlattenDeep()
    {
        var result = Run("let a = [[1, [2, 3]]]; let result = arr.flat(a);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.IsType<List<object?>>(list[1]);
    }

    [Fact]
    public void ArrFlat_EmptyArray()
    {
        var result = Run("let result = arr.flat([]);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    // arr.flatMap
    [Fact]
    public void ArrFlatMap_MapsAndFlattens()
    {
        var result = Run("let a = [1, 2, 3]; let result = arr.flatMap(a, (x) => [x, x * 2]);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(6, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(2L, list[2]);
        Assert.Equal(4L, list[3]);
    }

    [Fact]
    public void ArrFlatMap_NonArrayResults()
    {
        var result = Run("let a = [1, 2, 3]; let result = arr.flatMap(a, (x) => x * 10);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(10L, list[0]);
        Assert.Equal(20L, list[1]);
        Assert.Equal(30L, list[2]);
    }

    // arr.findIndex
    [Fact]
    public void ArrFindIndex_ReturnsIndex()
    {
        Assert.Equal(2L, Run("let a = [1, 2, 3, 4]; let result = arr.findIndex(a, (x) => x > 2);"));
    }

    [Fact]
    public void ArrFindIndex_NoMatch_ReturnsNegativeOne()
    {
        Assert.Equal(-1L, Run("let a = [1, 2, 3]; let result = arr.findIndex(a, (x) => x > 10);"));
    }

    [Fact]
    public void ArrFindIndex_ReturnsFirstMatch()
    {
        Assert.Equal(1L, Run("let a = [1, 4, 6, 8]; let result = arr.findIndex(a, (x) => x % 2 == 0);"));
    }

    // arr.count
    [Fact]
    public void ArrCount_CountsMatches()
    {
        Assert.Equal(3L, Run("let a = [1, 2, 3, 4, 5, 6]; let result = arr.count(a, (x) => x % 2 == 0);"));
    }

    [Fact]
    public void ArrCount_NoMatches()
    {
        Assert.Equal(0L, Run("let a = [1, 3, 5]; let result = arr.count(a, (x) => x % 2 == 0);"));
    }

    [Fact]
    public void ArrCount_AllMatch()
    {
        Assert.Equal(3L, Run("let a = [2, 4, 6]; let result = arr.count(a, (x) => x % 2 == 0);"));
    }

    [Fact]
    public void ArrCount_EmptyArray()
    {
        Assert.Equal(0L, Run("let result = arr.count([], (x) => true);"));
    }

    // ── Dictionary Tests ─────────────────────────────────────────────

    // dict.new
    [Fact]
    public void DictNew_CreatesEmptyDictionary()
    {
        var result = Run("let d = dict.new(); let result = d;");
        Assert.IsType<StashDictionary>(result);
    }

    // dict.set + dict.get
    [Fact]
    public void DictSetGet_StringKey()
    {
        Assert.Equal(42L, Run("let d = dict.new(); dict.set(d, \"age\", 42); let result = dict.get(d, \"age\");"));
    }

    [Fact]
    public void DictSetGet_IntKey()
    {
        Assert.Equal("hello", Run("let d = dict.new(); dict.set(d, 1, \"hello\"); let result = dict.get(d, 1);"));
    }

    [Fact]
    public void DictGet_MissingKey_ReturnsNull()
    {
        Assert.Null(Run("let d = dict.new(); let result = dict.get(d, \"missing\");"));
    }

    [Fact]
    public void DictSet_OverwritesExistingKey()
    {
        Assert.Equal("new", Run("let d = dict.new(); dict.set(d, \"k\", \"old\"); dict.set(d, \"k\", \"new\"); let result = dict.get(d, \"k\");"));
    }

    // Index syntax d["key"] and d["key"] = value
    [Fact]
    public void DictIndex_Get()
    {
        Assert.Equal(10L, Run("let d = dict.new(); dict.set(d, \"x\", 10); let result = d[\"x\"];"));
    }

    [Fact]
    public void DictIndex_Set()
    {
        Assert.Equal(99L, Run("let d = dict.new(); d[\"x\"] = 99; let result = d[\"x\"];"));
    }

    [Fact]
    public void DictIndex_SetAndGet_IntKey()
    {
        Assert.Equal("val", Run("let d = dict.new(); d[42] = \"val\"; let result = d[42];"));
    }

    [Fact]
    public void DictIndex_MissingKey_ReturnsNull()
    {
        Assert.Null(Run("let d = dict.new(); let result = d[\"nope\"];"));
    }

    [Fact]
    public void DictIndex_NullKey_Throws()
    {
        RunExpectingError("let d = dict.new(); d[null] = 1;");
    }

    [Fact]
    public void DictIndex_GetNullKey_Throws()
    {
        RunExpectingError("let d = dict.new(); let result = d[null];");
    }

    // dict.has
    [Fact]
    public void DictHas_ExistingKey()
    {
        Assert.Equal(true, Run("let d = dict.new(); d[\"a\"] = 1; let result = dict.has(d, \"a\");"));
    }

    [Fact]
    public void DictHas_MissingKey()
    {
        Assert.Equal(false, Run("let d = dict.new(); let result = dict.has(d, \"a\");"));
    }

    // dict.remove
    [Fact]
    public void DictRemove_ExistingKey()
    {
        Assert.Equal(true, Run("let d = dict.new(); d[\"a\"] = 1; let result = dict.remove(d, \"a\");"));
    }

    [Fact]
    public void DictRemove_MissingKey()
    {
        Assert.Equal(false, Run("let d = dict.new(); let result = dict.remove(d, \"x\");"));
    }

    [Fact]
    public void DictRemove_KeyNoLongerExists()
    {
        Assert.Equal(false, Run("let d = dict.new(); d[\"a\"] = 1; dict.remove(d, \"a\"); let result = dict.has(d, \"a\");"));
    }

    // dict.clear
    [Fact]
    public void DictClear_EmptiesDictionary()
    {
        Assert.Equal(0L, Run("let d = dict.new(); d[\"a\"] = 1; d[\"b\"] = 2; dict.clear(d); let result = len(d);"));
    }

    // dict.keys
    [Fact]
    public void DictKeys_ReturnsAllKeys()
    {
        var result = Run("let d = dict.new(); d[\"a\"] = 1; d[\"b\"] = 2; let result = dict.keys(d);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Contains("a", list);
        Assert.Contains("b", list);
    }

    // dict.values
    [Fact]
    public void DictValues_ReturnsAllValues()
    {
        var result = Run("let d = dict.new(); d[\"x\"] = 10; d[\"y\"] = 20; let result = dict.values(d);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Contains(10L, list);
        Assert.Contains(20L, list);
    }

    // dict.size
    [Fact]
    public void DictSize_ReturnsCount()
    {
        Assert.Equal(2L, Run("let d = dict.new(); d[\"a\"] = 1; d[\"b\"] = 2; let result = dict.size(d);"));
    }

    [Fact]
    public void DictSize_EmptyDict()
    {
        Assert.Equal(0L, Run("let d = dict.new(); let result = dict.size(d);"));
    }

    // dict.pairs
    [Fact]
    public void DictPairs_ReturnsPairStructs()
    {
        var result = Run("let d = dict.new(); d[\"k\"] = \"v\"; let result = dict.pairs(d);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        var pair = Assert.IsType<StashInstance>(list[0]);
        Assert.Equal("Pair", pair.TypeName);
    }

    [Fact]
    public void DictPairs_DotAccessKey()
    {
        Assert.Equal("k", Run("let d = dict.new(); d[\"k\"] = \"v\"; let pairs = dict.pairs(d); let result = pairs[0].key;"));
    }

    [Fact]
    public void DictPairs_DotAccessValue()
    {
        Assert.Equal("v", Run("let d = dict.new(); d[\"k\"] = \"v\"; let pairs = dict.pairs(d); let result = pairs[0].value;"));
    }

    [Fact]
    public void DictPairs_ForInDotAccess()
    {
        Assert.Equal(30L, Run("let d = dict.new(); d[\"x\"] = 10; d[\"y\"] = 20; let sum = 0; for (let pair in dict.pairs(d)) { sum = sum + pair.value; } let result = sum;"));
    }

    // dict.merge
    [Fact]
    public void DictMerge_CombinesDictionaries()
    {
        Assert.Equal(3L, Run("let a = dict.new(); a[\"x\"] = 1; let b = dict.new(); b[\"y\"] = 2; b[\"z\"] = 3; let merged = dict.merge(a, b); let result = dict.size(merged);"));
    }

    [Fact]
    public void DictMerge_SecondWinsOnConflict()
    {
        Assert.Equal("new", Run("let a = dict.new(); a[\"k\"] = \"old\"; let b = dict.new(); b[\"k\"] = \"new\"; let merged = dict.merge(a, b); let result = merged[\"k\"];"));
    }

    [Fact]
    public void DictMerge_DoesNotMutateOriginals()
    {
        Assert.Equal(1L, Run("let a = dict.new(); a[\"x\"] = 1; let b = dict.new(); b[\"y\"] = 2; dict.merge(a, b); let result = dict.size(a);"));
    }

    // dict.forEach
    [Fact]
    public void DictForEach_VisitsAllEntries()
    {
        Assert.Equal(2L, Run("let d = dict.new(); d[\"a\"] = 10; d[\"b\"] = 20; let count = 0; dict.forEach(d, (k, v) => { count = count + 1; }); let result = count;"));
    }

    // typeof
    [Fact]
    public void Dict_TypeofReturnsDict()
    {
        Assert.Equal("dict", Run("let d = dict.new(); let result = typeof(d);"));
    }

    // len
    [Fact]
    public void Dict_LenReturnsCount()
    {
        Assert.Equal(2L, Run("let d = dict.new(); d[\"a\"] = 1; d[\"b\"] = 2; let result = len(d);"));
    }

    // for-in iteration
    [Fact]
    public void Dict_ForIn_IteratesKeys()
    {
        Assert.Equal(2L, Run("let d = dict.new(); d[\"x\"] = 10; d[\"y\"] = 20; let count = 0; for (let k in d) { count = count + 1; } let result = count;"));
    }

    [Fact]
    public void Dict_ForIn_CanAccessValues()
    {
        Assert.Equal(30L, Run("let d = dict.new(); d[\"x\"] = 10; d[\"y\"] = 20; let sum = 0; for (let k in d) { sum = sum + d[k]; } let result = sum;"));
    }

    // Stringify
    [Fact]
    public void Dict_Stringify_SingleEntry()
    {
        var result = Run("let d = dict.new(); d[\"name\"] = \"John\"; let result = conv.toStr(d);");
        Assert.Equal("{name: John}", result);
    }

    // Error cases
    [Fact]
    public void DictSet_NonDict_Throws()
    {
        RunExpectingError("dict.set(\"hello\", \"key\", \"val\");");
    }

    [Fact]
    public void DictGet_NonDict_Throws()
    {
        RunExpectingError("dict.get(42, \"key\");");
    }

    [Fact]
    public void DictSet_NullKey_Throws()
    {
        RunExpectingError("let d = dict.new(); dict.set(d, null, 1);");
    }

    // Mixed value types
    [Fact]
    public void Dict_MixedValueTypes()
    {
        Assert.Equal(true, Run("let d = dict.new(); d[\"int\"] = 42; d[\"str\"] = \"hello\"; d[\"bool\"] = true; d[\"arr\"] = [1, 2]; let result = d[\"bool\"];"));
    }

    // Bool key
    [Fact]
    public void Dict_BoolKey()
    {
        Assert.Equal("yes", Run("let d = dict.new(); d[true] = \"yes\"; d[false] = \"no\"; let result = d[true];"));
    }

    // Array key should fail
    [Fact]
    public void Dict_ArrayKey_Throws()
    {
        RunExpectingError("let d = dict.new(); d[[1,2]] = \"val\";");
    }

    // ── str namespace ────────────────────────────────────────────────

    // str.upper
    [Fact]
    public void StrUpper_ConvertsToUppercase()
    {
        Assert.Equal("HELLO", Run("let result = str.upper(\"hello\");"));
    }

    [Fact]
    public void StrUpper_EmptyString()
    {
        Assert.Equal("", Run("let result = str.upper(\"\");"));
    }

    [Fact]
    public void StrUpper_NonString_Throws()
    {
        RunExpectingError("str.upper(123);");
    }

    // str.lower
    [Fact]
    public void StrLower_ConvertsToLowercase()
    {
        Assert.Equal("hello", Run("let result = str.lower(\"HELLO\");"));
    }

    [Fact]
    public void StrLower_EmptyString()
    {
        Assert.Equal("", Run("let result = str.lower(\"\");"));
    }

    [Fact]
    public void StrLower_NonString_Throws()
    {
        RunExpectingError("str.lower(123);");
    }

    // str.trim
    [Fact]
    public void StrTrim_RemovesBothEnds()
    {
        Assert.Equal("hi", Run("let result = str.trim(\"  hi  \");"));
    }

    [Fact]
    public void StrTrim_NoWhitespace_ReturnsSame()
    {
        Assert.Equal("hi", Run("let result = str.trim(\"hi\");"));
    }

    [Fact]
    public void StrTrim_NonString_Throws()
    {
        RunExpectingError("str.trim(42);");
    }

    // str.trimStart
    [Fact]
    public void StrTrimStart_RemovesLeadingWhitespace()
    {
        Assert.Equal("hi  ", Run("let result = str.trimStart(\"  hi  \");"));
    }

    [Fact]
    public void StrTrimStart_NoLeadingWhitespace_ReturnsSame()
    {
        Assert.Equal("hi  ", Run("let result = str.trimStart(\"hi  \");"));
    }

    [Fact]
    public void StrTrimStart_NonString_Throws()
    {
        RunExpectingError("str.trimStart(42);");
    }

    // str.trimEnd
    [Fact]
    public void StrTrimEnd_RemovesTrailingWhitespace()
    {
        Assert.Equal("  hi", Run("let result = str.trimEnd(\"  hi  \");"));
    }

    [Fact]
    public void StrTrimEnd_NoTrailingWhitespace_ReturnsSame()
    {
        Assert.Equal("  hi", Run("let result = str.trimEnd(\"  hi\");"));
    }

    [Fact]
    public void StrTrimEnd_NonString_Throws()
    {
        RunExpectingError("str.trimEnd(42);");
    }

    // str.contains
    [Fact]
    public void StrContains_SubstringPresent_ReturnsTrue()
    {
        Assert.Equal(true, Run("let result = str.contains(\"hello\", \"ell\");"));
    }

    [Fact]
    public void StrContains_SubstringAbsent_ReturnsFalse()
    {
        Assert.Equal(false, Run("let result = str.contains(\"hello\", \"xyz\");"));
    }

    [Fact]
    public void StrContains_NonString_Throws()
    {
        RunExpectingError("str.contains(123, \"x\");");
    }

    // str.startsWith
    [Fact]
    public void StrStartsWith_MatchingPrefix_ReturnsTrue()
    {
        Assert.Equal(true, Run("let result = str.startsWith(\"hello\", \"hel\");"));
    }

    [Fact]
    public void StrStartsWith_NonMatchingPrefix_ReturnsFalse()
    {
        Assert.Equal(false, Run("let result = str.startsWith(\"hello\", \"xyz\");"));
    }

    [Fact]
    public void StrStartsWith_NonString_Throws()
    {
        RunExpectingError("str.startsWith(123, \"h\");");
    }

    // str.endsWith
    [Fact]
    public void StrEndsWith_MatchingSuffix_ReturnsTrue()
    {
        Assert.Equal(true, Run("let result = str.endsWith(\"hello\", \"llo\");"));
    }

    [Fact]
    public void StrEndsWith_NonMatchingSuffix_ReturnsFalse()
    {
        Assert.Equal(false, Run("let result = str.endsWith(\"hello\", \"xyz\");"));
    }

    [Fact]
    public void StrEndsWith_NonString_Throws()
    {
        RunExpectingError("str.endsWith(123, \"o\");");
    }

    // str.indexOf
    [Fact]
    public void StrIndexOf_SubstringFound_ReturnsIndex()
    {
        Assert.Equal(2L, Run("let result = str.indexOf(\"hello\", \"ll\");"));
    }

    [Fact]
    public void StrIndexOf_SubstringNotFound_ReturnsNegativeOne()
    {
        Assert.Equal(-1L, Run("let result = str.indexOf(\"hello\", \"xyz\");"));
    }

    [Fact]
    public void StrIndexOf_NonString_Throws()
    {
        RunExpectingError("str.indexOf(123, \"l\");");
    }

    // str.lastIndexOf
    [Fact]
    public void StrLastIndexOf_SubstringFound_ReturnsLastIndex()
    {
        Assert.Equal(3L, Run("let result = str.lastIndexOf(\"abcabc\", \"abc\");"));
    }

    [Fact]
    public void StrLastIndexOf_SubstringNotFound_ReturnsNegativeOne()
    {
        Assert.Equal(-1L, Run("let result = str.lastIndexOf(\"abcabc\", \"xyz\");"));
    }

    [Fact]
    public void StrLastIndexOf_NonString_Throws()
    {
        RunExpectingError("str.lastIndexOf(123, \"a\");");
    }

    // str.substring
    [Fact]
    public void StrSubstring_StartAndEnd_ReturnsSlice()
    {
        Assert.Equal("ell", Run("let result = str.substring(\"hello\", 1, 4);"));
    }

    [Fact]
    public void StrSubstring_StartOnly_ReturnsToEnd()
    {
        Assert.Equal("llo", Run("let result = str.substring(\"hello\", 2);"));
    }

    [Fact]
    public void StrSubstring_OutOfBounds_Throws()
    {
        RunExpectingError("str.substring(\"hello\", 2, 99);");
    }

    [Fact]
    public void StrSubstring_NonString_Throws()
    {
        RunExpectingError("str.substring(123, 0, 1);");
    }

    // str.replace
    [Fact]
    public void StrReplace_ReplacesFirstOccurrence()
    {
        Assert.Equal("baa", Run("let result = str.replace(\"aaa\", \"a\", \"b\");"));
    }

    [Fact]
    public void StrReplace_PatternNotFound_ReturnsSame()
    {
        Assert.Equal("hello", Run("let result = str.replace(\"hello\", \"xyz\", \"z\");"));
    }

    [Fact]
    public void StrReplace_NonString_Throws()
    {
        RunExpectingError("str.replace(123, \"a\", \"b\");");
    }

    // str.replaceAll
    [Fact]
    public void StrReplaceAll_ReplacesAllOccurrences()
    {
        Assert.Equal("bbb", Run("let result = str.replaceAll(\"aaa\", \"a\", \"b\");"));
    }

    [Fact]
    public void StrReplaceAll_PatternNotFound_ReturnsSame()
    {
        Assert.Equal("hello", Run("let result = str.replaceAll(\"hello\", \"xyz\", \"z\");"));
    }

    [Fact]
    public void StrReplaceAll_NonString_Throws()
    {
        RunExpectingError("str.replaceAll(123, \"a\", \"b\");");
    }

    // str.split
    [Fact]
    public void StrSplit_SplitsByDelimiter()
    {
        var result = Run("let result = str.split(\"a,b,c\", \",\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("b", list[1]);
        Assert.Equal("c", list[2]);
    }

    [Fact]
    public void StrSplit_DelimiterNotFound_ReturnsSingleElement()
    {
        var result = Run("let result = str.split(\"hello\", \",\");");
        var list = Assert.IsType<List<object?>>(result);
        var single = Assert.Single(list);
        Assert.Equal("hello", single);
    }

    [Fact]
    public void StrSplit_NonString_Throws()
    {
        RunExpectingError("str.split(123, \",\");");
    }

    // str.repeat
    [Fact]
    public void StrRepeat_RepeatsTimes()
    {
        Assert.Equal("ababab", Run("let result = str.repeat(\"ab\", 3);"));
    }

    [Fact]
    public void StrRepeat_ZeroTimes_ReturnsEmpty()
    {
        Assert.Equal("", Run("let result = str.repeat(\"ab\", 0);"));
    }

    [Fact]
    public void StrRepeat_NegativeCount_Throws()
    {
        RunExpectingError("str.repeat(\"ab\", -1);");
    }

    [Fact]
    public void StrRepeat_NonString_Throws()
    {
        RunExpectingError("str.repeat(123, 2);");
    }

    // str.reverse
    [Fact]
    public void StrReverse_ReversesString()
    {
        Assert.Equal("olleh", Run("let result = str.reverse(\"hello\");"));
    }

    [Fact]
    public void StrReverse_EmptyString()
    {
        Assert.Equal("", Run("let result = str.reverse(\"\");"));
    }

    [Fact]
    public void StrReverse_NonString_Throws()
    {
        RunExpectingError("str.reverse(123);");
    }

    // str.chars
    [Fact]
    public void StrChars_ReturnsCharArray()
    {
        var result = Run("let result = str.chars(\"abc\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("b", list[1]);
        Assert.Equal("c", list[2]);
    }

    [Fact]
    public void StrChars_EmptyString_ReturnsEmptyArray()
    {
        var result = Run("let result = str.chars(\"\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void StrChars_NonString_Throws()
    {
        RunExpectingError("str.chars(123);");
    }

    // str.padStart
    [Fact]
    public void StrPadStart_PadsWithCustomFill()
    {
        Assert.Equal("00042", Run("let result = str.padStart(\"42\", 5, \"0\");"));
    }

    [Fact]
    public void StrPadStart_DefaultFill_PadsWithSpaces()
    {
        Assert.Equal("   hi", Run("let result = str.padStart(\"hi\", 5);"));
    }

    [Fact]
    public void StrPadStart_AlreadyLongEnough_ReturnsSame()
    {
        Assert.Equal("hello", Run("let result = str.padStart(\"hello\", 3);"));
    }

    [Fact]
    public void StrPadStart_NonString_Throws()
    {
        RunExpectingError("str.padStart(123, 5);");
    }

    // str.padEnd
    [Fact]
    public void StrPadEnd_DefaultFill_PadsWithSpaces()
    {
        Assert.Equal("hi   ", Run("let result = str.padEnd(\"hi\", 5);"));
    }

    [Fact]
    public void StrPadEnd_PadsWithCustomFill()
    {
        Assert.Equal("hi...", Run("let result = str.padEnd(\"hi\", 5, \".\");"));
    }

    [Fact]
    public void StrPadEnd_AlreadyLongEnough_ReturnsSame()
    {
        Assert.Equal("hello", Run("let result = str.padEnd(\"hello\", 3);"));
    }

    [Fact]
    public void StrPadEnd_NonString_Throws()
    {
        RunExpectingError("str.padEnd(123, 5);");
    }

    [Fact]
    public void ForIn_Dict_SafeWithModification()
    {
        // Modifying a dict during for-in iteration should not crash
        // because keys are snapshot'd with .ToList()
        var result = Run(@"
            let d = dict.new();
            dict.set(d, ""a"", 1);
            dict.set(d, ""b"", 2);
            dict.set(d, ""c"", 3);
            let result = 0;
            for (let key in d) {
                dict.remove(d, key);
                result = result + 1;
            }
        ");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Dict_Stringify_EmptyDict()
    {
        var result = Run(@"
            let d = dict.new();
            let result = conv.toStr(d);
        ");
        Assert.Equal("{}", result);
    }

    [Fact]
    public void Dict_Stringify_MultiplePairs()
    {
        var result = Run(@"
            let d = dict.new();
            dict.set(d, ""a"", 1);
            let result = conv.toStr(d);
        ");
        Assert.Equal("{a: 1}", result);
    }

    [Fact]
    public void StrReplaceAll_OrdinalComparison()
    {
        // Ensure replaceAll uses exact ordinal matching
        var result = Run(@"
            let result = str.replaceAll(""aAbBaAbB"", ""aA"", ""X"");
        ");
        Assert.Equal("XbBXbB", result);
    }

    // ── math namespace ──────────────────────────────────────────────

    [Fact]
    public void MathAbs_PositiveInt()
    {
        Assert.Equal(5L, Run("let result = math.abs(5);"));
    }

    [Fact]
    public void MathAbs_NegativeInt()
    {
        Assert.Equal(7L, Run("let result = math.abs(-7);"));
    }

    [Fact]
    public void MathAbs_NegativeFloat()
    {
        Assert.Equal(3.14, Run("let result = math.abs(-3.14);"));
    }

    [Fact]
    public void MathAbs_Zero()
    {
        Assert.Equal(0L, Run("let result = math.abs(0);"));
    }

    [Fact]
    public void MathAbs_NonNumber_Throws()
    {
        RunExpectingError("math.abs(\"hello\");");
    }

    [Fact]
    public void MathCeil_Float()
    {
        Assert.Equal(4L, Run("let result = math.ceil(3.2);"));
    }

    [Fact]
    public void MathCeil_NegativeFloat()
    {
        Assert.Equal(-3L, Run("let result = math.ceil(-3.7);"));
    }

    [Fact]
    public void MathCeil_Int()
    {
        Assert.Equal(5L, Run("let result = math.ceil(5);"));
    }

    [Fact]
    public void MathFloor_Float()
    {
        Assert.Equal(3L, Run("let result = math.floor(3.9);"));
    }

    [Fact]
    public void MathFloor_NegativeFloat()
    {
        Assert.Equal(-4L, Run("let result = math.floor(-3.2);"));
    }

    [Fact]
    public void MathFloor_Int()
    {
        Assert.Equal(5L, Run("let result = math.floor(5);"));
    }

    [Fact]
    public void MathRound_RoundsUp()
    {
        Assert.Equal(4L, Run("let result = math.round(3.7);"));
    }

    [Fact]
    public void MathRound_RoundsDown()
    {
        Assert.Equal(3L, Run("let result = math.round(3.2);"));
    }

    [Fact]
    public void MathRound_Int()
    {
        Assert.Equal(5L, Run("let result = math.round(5);"));
    }

    [Fact]
    public void MathMin_Ints()
    {
        Assert.Equal(2L, Run("let result = math.min(5, 2);"));
    }

    [Fact]
    public void MathMin_Floats()
    {
        Assert.Equal(1.5, Run("let result = math.min(2.5, 1.5);"));
    }

    [Fact]
    public void MathMin_MixedTypes()
    {
        Assert.Equal(1.5, Run("let result = math.min(2, 1.5);"));
    }

    [Fact]
    public void MathMax_Ints()
    {
        Assert.Equal(5L, Run("let result = math.max(5, 2);"));
    }

    [Fact]
    public void MathMax_Floats()
    {
        Assert.Equal(2.5, Run("let result = math.max(2.5, 1.5);"));
    }

    [Fact]
    public void MathPow_IntExponent()
    {
        Assert.Equal(8.0, Run("let result = math.pow(2, 3);"));
    }

    [Fact]
    public void MathSqrt_PerfectSquare()
    {
        Assert.Equal(3.0, Run("let result = math.sqrt(9);"));
    }

    [Fact]
    public void MathSqrt_Float()
    {
        Assert.Equal(2.0, Run("let result = math.sqrt(4.0);"));
    }

    [Fact]
    public void MathLog_E()
    {
        Assert.Equal(1.0, (double)Run("let result = math.log(math.E);")!, 5);
    }

    [Fact]
    public void MathRandom_InRange()
    {
        // Random returns 0.0 to 1.0
        var result = (double)Run("let result = math.random();")!;
        Assert.InRange(result, 0.0, 1.0);
    }

    [Fact]
    public void MathRandomInt_InRange()
    {
        var result = (long)Run("let result = math.randomInt(1, 10);")!;
        Assert.InRange(result, 1L, 10L);
    }

    [Fact]
    public void MathClamp_WithinRange()
    {
        Assert.Equal(5L, Run("let result = math.clamp(5, 1, 10);"));
    }

    [Fact]
    public void MathClamp_BelowMin()
    {
        Assert.Equal(1L, Run("let result = math.clamp(-5, 1, 10);"));
    }

    [Fact]
    public void MathClamp_AboveMax()
    {
        Assert.Equal(10L, Run("let result = math.clamp(15, 1, 10);"));
    }

    [Fact]
    public void MathClamp_FloatValues()
    {
        Assert.Equal(1.5, Run("let result = math.clamp(0.5, 1.5, 10.0);"));
    }

    [Fact]
    public void MathPI_IsConstant()
    {
        Assert.Equal(Math.PI, Run("let result = math.PI;"));
    }

    [Fact]
    public void MathE_IsConstant()
    {
        Assert.Equal(Math.E, Run("let result = math.E;"));
    }

    // math.sin
    [Fact]
    public void MathSin_Zero()
    {
        Assert.Equal(0.0, Run("let result = math.sin(0);"));
    }

    [Fact]
    public void MathSin_PiOverTwo()
    {
        Assert.Equal(1.0, (double)Run("let result = math.sin(math.PI / 2);")!, 5);
    }

    // math.cos
    [Fact]
    public void MathCos_Zero()
    {
        Assert.Equal(1.0, Run("let result = math.cos(0);"));
    }

    [Fact]
    public void MathCos_Pi()
    {
        Assert.Equal(-1.0, (double)Run("let result = math.cos(math.PI);")!, 5);
    }

    // math.tan
    [Fact]
    public void MathTan_Zero()
    {
        Assert.Equal(0.0, Run("let result = math.tan(0);"));
    }

    [Fact]
    public void MathTan_PiOverFour()
    {
        Assert.Equal(1.0, (double)Run("let result = math.tan(math.PI / 4);")!, 5);
    }

    // math.asin
    [Fact]
    public void MathAsin_Zero()
    {
        Assert.Equal(0.0, Run("let result = math.asin(0);"));
    }

    [Fact]
    public void MathAsin_One()
    {
        Assert.Equal(Math.PI / 2, (double)Run("let result = math.asin(1);")!, 5);
    }

    // math.acos
    [Fact]
    public void MathAcos_One()
    {
        Assert.Equal(0.0, Run("let result = math.acos(1);"));
    }

    [Fact]
    public void MathAcos_Zero()
    {
        Assert.Equal(Math.PI / 2, (double)Run("let result = math.acos(0);")!, 5);
    }

    // math.atan
    [Fact]
    public void MathAtan_Zero()
    {
        Assert.Equal(0.0, Run("let result = math.atan(0);"));
    }

    [Fact]
    public void MathAtan_One()
    {
        Assert.Equal(Math.PI / 4, (double)Run("let result = math.atan(1);")!, 5);
    }

    // math.atan2
    [Fact]
    public void MathAtan2_OneOne()
    {
        Assert.Equal(Math.PI / 4, (double)Run("let result = math.atan2(1, 1);")!, 5);
    }

    [Fact]
    public void MathAtan2_ZeroNegativeOne()
    {
        Assert.Equal(Math.PI, (double)Run("let result = math.atan2(0, -1);")!, 5);
    }

    // math.sign
    [Fact]
    public void MathSign_Positive()
    {
        Assert.Equal(1L, Run("let result = math.sign(42);"));
    }

    [Fact]
    public void MathSign_Negative()
    {
        Assert.Equal(-1L, Run("let result = math.sign(-7);"));
    }

    [Fact]
    public void MathSign_Zero()
    {
        Assert.Equal(0L, Run("let result = math.sign(0);"));
    }

    [Fact]
    public void MathSign_Float()
    {
        Assert.Equal(-1L, Run("let result = math.sign(-3.14);"));
    }

    // math.exp
    [Fact]
    public void MathExp_Zero()
    {
        Assert.Equal(1.0, Run("let result = math.exp(0);"));
    }

    [Fact]
    public void MathExp_One()
    {
        Assert.Equal(Math.E, (double)Run("let result = math.exp(1);")!, 5);
    }

    // math.log10
    [Fact]
    public void MathLog10_Ten()
    {
        Assert.Equal(1.0, Run("let result = math.log10(10);"));
    }

    [Fact]
    public void MathLog10_Hundred()
    {
        Assert.Equal(2.0, Run("let result = math.log10(100);"));
    }

    // math.log2
    [Fact]
    public void MathLog2_Two()
    {
        Assert.Equal(1.0, Run("let result = math.log2(2);"));
    }

    [Fact]
    public void MathLog2_Eight()
    {
        Assert.Equal(3.0, Run("let result = math.log2(8);"));
    }

    [Fact]
    public void MathLog2_NonNumber_Throws()
    {
        RunExpectingError("math.log2(\"hello\");");
    }

    // ── time namespace ──────────────────────────────────────────────

    [Fact]
    public void TimeNow_ReturnsFloat()
    {
        var result = Run("let result = time.now();");
        Assert.IsType<double>(result);
        Assert.True((double)result! > 0);
    }

    [Fact]
    public void TimeMillis_ReturnsLong()
    {
        var result = Run("let result = time.millis();");
        Assert.IsType<long>(result);
        Assert.True((long)result! > 0);
    }

    [Fact]
    public void TimeDate_ReturnsFormattedDate()
    {
        var result = (string)Run("let result = time.date();")!;
        // Should match YYYY-MM-DD format
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", result);
    }

    [Fact]
    public void TimeIso_ReturnsIsoString()
    {
        var result = (string)Run("let result = time.iso();")!;
        Assert.Contains("T", result);
    }

    [Fact]
    public void TimeFormat_CustomFormat()
    {
        // Format epoch 0 which is 1970-01-01
        var result = Run("let result = time.format(0, \"yyyy\");");
        Assert.Equal("1970", result);
    }

    [Fact]
    public void TimeParse_ValidDate()
    {
        var result = (double)Run("let result = time.parse(\"2000-01-01\", \"yyyy-MM-dd\");")!;
        Assert.True(result > 0);
    }

    [Fact]
    public void TimeParse_InvalidDate_Throws()
    {
        RunExpectingError("time.parse(\"not-a-date\", \"yyyy-MM-dd\");");
    }

    [Fact]
    public void TimeClock_ReturnsFloat()
    {
        var result = Run("let result = time.clock();");
        Assert.IsType<double>(result);
    }

    [Fact]
    public void TimeSleep_NonNumber_Throws()
    {
        RunExpectingError("time.sleep(\"abc\");");
    }

    // ── json namespace ──────────────────────────────────────────────

    [Fact]
    public void JsonParse_Object()
    {
        var result = Run("let d = json.parse(\"{\\\"name\\\": \\\"Alice\\\", \\\"age\\\": 30}\"); let result = d[\"name\"];");
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void JsonParse_Array()
    {
        var result = Run("let a = json.parse(\"[1, 2, 3]\"); let result = len(a);");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void JsonParse_Nested()
    {
        var result = Run("let d = json.parse(\"{\\\"a\\\": {\\\"b\\\": 42}}\"); let result = d[\"a\"][\"b\"];");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void JsonParse_Boolean()
    {
        Assert.Equal(true, Run("let d = json.parse(\"{\\\"ok\\\": true}\"); let result = d[\"ok\"];"));
    }

    [Fact]
    public void JsonParse_Null()
    {
        Assert.Null(Run("let d = json.parse(\"{\\\"val\\\": null}\"); let result = d[\"val\"];"));
    }

    [Fact]
    public void JsonParse_InvalidJson_Throws()
    {
        RunExpectingError("json.parse(\"not json\");");
    }

    [Fact]
    public void JsonStringify_Dict()
    {
        var result = (string)Run("let d = dict.new(); d[\"a\"] = 1; let result = json.stringify(d);")!;
        Assert.Contains("\"a\"", result);
        Assert.Contains("1", result);
    }

    [Fact]
    public void JsonStringify_Array()
    {
        Assert.Equal("[1,2,3]", Run("let result = json.stringify([1, 2, 3]);"));
    }

    [Fact]
    public void JsonStringify_String()
    {
        Assert.Equal("\"hello\"", Run("let result = json.stringify(\"hello\");"));
    }

    [Fact]
    public void JsonStringify_Null()
    {
        Assert.Equal("null", Run("let result = json.stringify(null);"));
    }

    [Fact]
    public void JsonStringify_Bool()
    {
        Assert.Equal("true", Run("let result = json.stringify(true);"));
    }

    [Fact]
    public void JsonPretty_Indented()
    {
        var result = (string)Run("let result = json.pretty([1, 2]);")!;
        Assert.Contains("\n", result);
    }

    // ── conv additions ──────────────────────────────────────────────

    [Fact]
    public void ConvToBool_Truthy()
    {
        Assert.Equal(true, Run("let result = conv.toBool(1);"));
    }

    [Fact]
    public void ConvToBool_Falsy()
    {
        Assert.Equal(false, Run("let result = conv.toBool(0);"));
    }

    [Fact]
    public void ConvToBool_NullIsFalsy()
    {
        Assert.Equal(false, Run("let result = conv.toBool(null);"));
    }

    [Fact]
    public void ConvToBool_EmptyStringFalsy()
    {
        Assert.Equal(false, Run("let result = conv.toBool(\"\");"));
    }

    [Fact]
    public void ConvToHex_Basic()
    {
        Assert.Equal("ff", Run("let result = conv.toHex(255);"));
    }

    [Fact]
    public void ConvToHex_Zero()
    {
        Assert.Equal("0", Run("let result = conv.toHex(0);"));
    }

    [Fact]
    public void ConvToOct_Basic()
    {
        Assert.Equal("10", Run("let result = conv.toOct(8);"));
    }

    [Fact]
    public void ConvToBin_Basic()
    {
        Assert.Equal("1010", Run("let result = conv.toBin(10);"));
    }

    [Fact]
    public void ConvFromHex_Basic()
    {
        Assert.Equal(255L, Run("let result = conv.fromHex(\"ff\");"));
    }

    [Fact]
    public void ConvFromHex_WithPrefix()
    {
        Assert.Equal(255L, Run("let result = conv.fromHex(\"0xff\");"));
    }

    [Fact]
    public void ConvFromOct_Basic()
    {
        Assert.Equal(8L, Run("let result = conv.fromOct(\"10\");"));
    }

    [Fact]
    public void ConvFromBin_Basic()
    {
        Assert.Equal(10L, Run("let result = conv.fromBin(\"1010\");"));
    }

    [Fact]
    public void ConvFromBin_WithPrefix()
    {
        Assert.Equal(10L, Run("let result = conv.fromBin(\"0b1010\");"));
    }

    [Fact]
    public void ConvCharCode_Basic()
    {
        Assert.Equal(65L, Run("let result = conv.charCode(\"A\");"));
    }

    [Fact]
    public void ConvFromCharCode_Basic()
    {
        Assert.Equal("A", Run("let result = conv.fromCharCode(65);"));
    }

    [Fact]
    public void ConvFromHex_Invalid_Throws()
    {
        RunExpectingError("conv.fromHex(\"xyz\");");
    }

    // ── str additions ───────────────────────────────────────────────

    [Fact]
    public void StrIsDigit_True()
    {
        Assert.Equal(true, Run("let result = str.isDigit(\"12345\");"));
    }

    [Fact]
    public void StrIsDigit_False()
    {
        Assert.Equal(false, Run("let result = str.isDigit(\"123a\");"));
    }

    [Fact]
    public void StrIsDigit_Empty()
    {
        Assert.Equal(false, Run("let result = str.isDigit(\"\");"));
    }

    [Fact]
    public void StrIsAlpha_True()
    {
        Assert.Equal(true, Run("let result = str.isAlpha(\"hello\");"));
    }

    [Fact]
    public void StrIsAlpha_False()
    {
        Assert.Equal(false, Run("let result = str.isAlpha(\"hello1\");"));
    }

    [Fact]
    public void StrIsAlphaNum_True()
    {
        Assert.Equal(true, Run("let result = str.isAlphaNum(\"abc123\");"));
    }

    [Fact]
    public void StrIsAlphaNum_False()
    {
        Assert.Equal(false, Run("let result = str.isAlphaNum(\"abc-123\");"));
    }

    [Fact]
    public void StrIsUpper_True()
    {
        Assert.Equal(true, Run("let result = str.isUpper(\"HELLO\");"));
    }

    [Fact]
    public void StrIsUpper_False()
    {
        Assert.Equal(false, Run("let result = str.isUpper(\"Hello\");"));
    }

    [Fact]
    public void StrIsLower_True()
    {
        Assert.Equal(true, Run("let result = str.isLower(\"hello\");"));
    }

    [Fact]
    public void StrIsLower_False()
    {
        Assert.Equal(false, Run("let result = str.isLower(\"Hello\");"));
    }

    [Fact]
    public void StrIsEmpty_Empty()
    {
        Assert.Equal(true, Run("let result = str.isEmpty(\"\");"));
    }

    [Fact]
    public void StrIsEmpty_Whitespace()
    {
        Assert.Equal(true, Run("let result = str.isEmpty(\"   \");"));
    }

    [Fact]
    public void StrIsEmpty_NonEmpty()
    {
        Assert.Equal(false, Run("let result = str.isEmpty(\"hello\");"));
    }

    [Fact]
    public void StrMatch_Found()
    {
        Assert.Equal("123", Run("let result = str.match(\"abc123def\", \"\\\\d+\");"));
    }

    [Fact]
    public void StrMatch_NotFound()
    {
        Assert.Null(Run("let result = str.match(\"abcdef\", \"\\\\d+\");"));
    }

    [Fact]
    public void StrMatchAll_MultipleMatches()
    {
        var result = Run("let matches = str.matchAll(\"a1b2c3\", \"\\\\d\"); let result = len(matches);");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void StrIsMatch_True()
    {
        Assert.Equal(true, Run("let result = str.isMatch(\"hello123\", \"\\\\d+\");"));
    }

    [Fact]
    public void StrIsMatch_False()
    {
        Assert.Equal(false, Run("let result = str.isMatch(\"hello\", \"\\\\d+\");"));
    }

    [Fact]
    public void StrReplaceRegex_Basic()
    {
        Assert.Equal("abc_def", Run("let result = str.replaceRegex(\"abc123def\", \"\\\\d+\", \"_\");"));
    }

    [Fact]
    public void StrCount_Basic()
    {
        Assert.Equal(3L, Run("let result = str.count(\"abcabc abc\", \"abc\");"));
    }

    [Fact]
    public void StrCount_NoMatch()
    {
        Assert.Equal(0L, Run("let result = str.count(\"hello\", \"xyz\");"));
    }

    [Fact]
    public void StrFormat_Basic()
    {
        Assert.Equal("Hello Alice, you are 30", Run("let result = str.format(\"Hello {0}, you are {1}\", \"Alice\", 30);"));
    }

    [Fact]
    public void StrFormat_NoPlaceholders()
    {
        Assert.Equal("hello", Run("let result = str.format(\"hello\");"));
    }

    [Fact]
    public void StrCount_EmptySub_Throws()
    {
        RunExpectingError("str.count(\"hello\", \"\");");
    }

    // ── env additions ───────────────────────────────────────────────

    [Fact]
    public void EnvHas_Existing()
    {
        // PATH should always exist
        Assert.Equal(true, Run("let result = env.has(\"PATH\");"));
    }

    [Fact]
    public void EnvHas_NonExisting()
    {
        Assert.Equal(false, Run("let result = env.has(\"STASH_TEST_NONEXISTENT_VAR_12345\");"));
    }

    [Fact]
    public void EnvAll_ReturnsDict()
    {
        Assert.Equal("dict", Run("let result = typeof(env.all());"));
    }

    [Fact]
    public void EnvRemove_ClearsVariable()
    {
        var result = Run("env.set(\"STASH_TEST_REMOVE\", \"value\"); env.remove(\"STASH_TEST_REMOVE\"); let result = env.get(\"STASH_TEST_REMOVE\");");
        Assert.Null(result);
    }

    [Fact]
    public void EnvCwd_ReturnsString()
    {
        var result = Run("let result = env.cwd();");
        Assert.IsType<string>(result);
        Assert.True(((string)result!).Length > 0);
    }

    [Fact]
    public void EnvHome_ReturnsString()
    {
        var result = Run("let result = env.home();");
        Assert.IsType<string>(result);
    }

    [Fact]
    public void EnvHostname_ReturnsString()
    {
        var result = Run("let result = env.hostname();");
        Assert.IsType<string>(result);
    }

    [Fact]
    public void EnvUser_ReturnsString()
    {
        var result = Run("let result = env.user();");
        Assert.IsType<string>(result);
    }

    [Fact]
    public void EnvOs_ReturnsKnownValue()
    {
        var result = (string)Run("let result = env.os();")!;
        Assert.Contains(result, new[] { "linux", "macos", "windows", "unknown" });
    }

    [Fact]
    public void EnvArch_ReturnsString()
    {
        var result = (string)Run("let result = env.arch();")!;
        Assert.True(result.Length > 0);
    }

    // ── fs additions ────────────────────────────────────────────────

    [Fact]
    public void FsReadLines_ReturnsArray()
    {
        var result = Run(
            "fs.writeFile(\"/tmp/stash_test_readlines.txt\", \"line1\\nline2\\nline3\");" +
            "let lines = fs.readLines(\"/tmp/stash_test_readlines.txt\");" +
            "let result = len(lines);" +
            "fs.delete(\"/tmp/stash_test_readlines.txt\");");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void FsIsFile_True()
    {
        var result = Run(
            "fs.writeFile(\"/tmp/stash_test_isfile.txt\", \"test\");" +
            "let result = fs.isFile(\"/tmp/stash_test_isfile.txt\");" +
            "fs.delete(\"/tmp/stash_test_isfile.txt\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void FsIsFile_False()
    {
        Assert.Equal(false, Run("let result = fs.isFile(\"/tmp/stash_nonexistent_12345\");"));
    }

    [Fact]
    public void FsIsDir_True()
    {
        Assert.Equal(true, Run("let result = fs.isDir(\"/tmp\");"));
    }

    [Fact]
    public void FsIsDir_False()
    {
        Assert.Equal(false, Run("let result = fs.isDir(\"/tmp/stash_nonexistent_dir_12345\");"));
    }

    [Fact]
    public void FsTempFile_ReturnsPath()
    {
        var result = Run("let result = fs.tempFile();");
        Assert.IsType<string>(result);
        Assert.True(((string)result!).Length > 0);
    }

    [Fact]
    public void FsTempDir_ReturnsPath()
    {
        var result = (string)Run("let result = fs.tempDir();")!;
        Assert.True(result.Length > 0);
        Assert.True(System.IO.Directory.Exists(result));
        // Cleanup
        System.IO.Directory.Delete(result, true);
    }

    [Fact]
    public void FsModifiedAt_ReturnsTimestamp()
    {
        var result = Run(
            "fs.writeFile(\"/tmp/stash_test_modat.txt\", \"test\");" +
            "let result = fs.modifiedAt(\"/tmp/stash_test_modat.txt\");" +
            "fs.delete(\"/tmp/stash_test_modat.txt\");");
        Assert.IsType<double>(result);
        Assert.True((double)result! > 0);
    }

    [Fact]
    public void FsWalk_ReturnsFiles()
    {
        var result = Run(
            "fs.createDir(\"/tmp/stash_test_walk\");" +
            "fs.writeFile(\"/tmp/stash_test_walk/a.txt\", \"a\");" +
            "fs.writeFile(\"/tmp/stash_test_walk/b.txt\", \"b\");" +
            "let files = fs.walk(\"/tmp/stash_test_walk\");" +
            "let result = len(files);" +
            "fs.delete(\"/tmp/stash_test_walk\");");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void FsGlob_FindsFiles()
    {
        var result = Run(
            "fs.createDir(\"/tmp/stash_test_glob\");" +
            "fs.writeFile(\"/tmp/stash_test_glob/test.txt\", \"data\");" +
            "let files = fs.glob(\"/tmp/stash_test_glob/*.txt\");" +
            "let result = len(files);" +
            "fs.delete(\"/tmp/stash_test_glob\");");
        Assert.True((long)result! >= 1L);
    }

    [Fact]
    public void FsIsSymlink_False()
    {
        var result = Run(
            "fs.writeFile(\"/tmp/stash_test_symlink.txt\", \"test\");" +
            "let result = fs.isSymlink(\"/tmp/stash_test_symlink.txt\");" +
            "fs.delete(\"/tmp/stash_test_symlink.txt\");");
        Assert.Equal(false, result);
    }

    // ── global utilities ────────────────────────────────────────────

    [Fact]
    public void Range_SingleArg()
    {
        Assert.Equal(5L, Run("let result = len(range(5));"));
    }

    [Fact]
    public void Range_SingleArg_Values()
    {
        Assert.Equal(0L, Run("let r = range(5); let result = r[0];"));
    }

    [Fact]
    public void Range_SingleArg_LastValue()
    {
        Assert.Equal(4L, Run("let r = range(5); let result = r[4];"));
    }

    [Fact]
    public void Range_TwoArgs()
    {
        Assert.Equal(3L, Run("let result = len(range(2, 5));"));
    }

    [Fact]
    public void Range_TwoArgs_StartValue()
    {
        Assert.Equal(2L, Run("let r = range(2, 5); let result = r[0];"));
    }

    [Fact]
    public void Range_ThreeArgs_Step()
    {
        Assert.Equal(3L, Run("let result = len(range(0, 10, 4));"));
    }

    [Fact]
    public void Range_NegativeStep()
    {
        Assert.Equal(10L, Run("let r = range(10, 0, -2); let result = r[0];"));
    }

    [Fact]
    public void Range_NegativeStep_Length()
    {
        Assert.Equal(5L, Run("let result = len(range(10, 0, -2));"));
    }

    [Fact]
    public void Range_ZeroStep_Throws()
    {
        RunExpectingError("range(0, 10, 0);");
    }

    [Fact]
    public void Range_EmptyRange()
    {
        Assert.Equal(0L, Run("let result = len(range(0));"));
    }

    [Fact]
    public void Range_NonInt_Throws()
    {
        RunExpectingError("range(1.5);");
    }

    [Fact]
    public void Hash_IntValue()
    {
        var result = Run("let result = hash(42);");
        Assert.IsType<long>(result);
    }

    [Fact]
    public void Hash_NullValue()
    {
        Assert.Equal(0L, Run("let result = hash(null);"));
    }

    [Fact]
    public void Hash_SameValuesSameHash()
    {
        Assert.Equal(true, Run("let result = hash(\"hello\") == hash(\"hello\");"));
    }

    // ── fs.readable / fs.writable / fs.executable ──

    [Fact]
    public void FsReadable_ExistingFile_ReturnsTrue()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            var result = Run($"let result = fs.readable(\"{tmp.Replace("\\\\", "\\\\\\\\")}\");");
            Assert.Equal(true, result);
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Fact]
    public void FsReadable_NonExistentFile_ReturnsFalse()
    {
        Assert.Equal(false, Run("let result = fs.readable(\"/nonexistent/path/xyz.txt\");"));
    }

    [Fact]
    public void FsWritable_ExistingFile_ReturnsTrue()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            var result = Run($"let result = fs.writable(\"{tmp.Replace("\\\\", "\\\\\\\\")}\");");
            Assert.Equal(true, result);
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Fact]
    public void FsWritable_NonExistentFile_ReturnsFalse()
    {
        Assert.Equal(false, Run("let result = fs.writable(\"/nonexistent/path/xyz.txt\");"));
    }

    [Fact]
    public void FsExecutable_NonExistentFile_ReturnsFalse()
    {
        Assert.Equal(false, Run("let result = fs.executable(\"/nonexistent/path/xyz\");"));
    }

    [Fact]
    public void FsExecutable_RegularFile_ReturnsFalse()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            // Temp files are created without execute permission
            var result = Run($"let result = fs.executable(\"{tmp.Replace("\\\\", "\\\\\\\\")}\");");
            Assert.Equal(false, result);
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Fact]
    public void FsExecutable_ExecutableFile_ReturnsTrue()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            // Make the file executable
            System.IO.File.SetUnixFileMode(tmp,
                System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute);
            var result = Run($"let result = fs.executable(\"{tmp.Replace("\\\\", "\\\\\\\\")}\");");
            Assert.Equal(true, result);
        }
        finally { System.IO.File.Delete(tmp); }
    }

    // ── Tilde Expansion ──

    [Fact]
    public void TildeExpansion_InCommand_ExpandsHomeDirectory()
    {
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var result = Run("let r = $(echo ~); let result = r.stdout;");
        Assert.Contains(home, (string)result!);
    }

    [Fact]
    public void TildeExpansion_InFsDirExists_ExpandsHomeDirectory()
    {
        // The home directory should exist
        Assert.Equal(true, Run("let result = fs.dirExists(\"~/\");"));
    }

    // ── process.exec ──

    [Fact]
    public void ProcessExec_NonStringArg_ThrowsError()
    {
        RunExpectingError("process.exec(42);");
    }

    [Fact]
    public void ProcessExec_NonExistentProgram_ThrowsError()
    {
        RunExpectingError("process.exec(\"__stash_nonexistent_program_xyz__\");");
    }

    // ── env.loadFile / env.saveFile ──

    [Fact]
    public void EnvLoadFile_LoadsVariables()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tmp, "MY_TEST_VAR_1=hello\nMY_TEST_VAR_2=world\n");
            var result = Run($"let result = env.loadFile(\"{tmp.Replace("\\", "\\\\")}\");");
            Assert.Equal(2L, result);
            Assert.Equal("hello", System.Environment.GetEnvironmentVariable("MY_TEST_VAR_1"));
            Assert.Equal("world", System.Environment.GetEnvironmentVariable("MY_TEST_VAR_2"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MY_TEST_VAR_1", null);
            System.Environment.SetEnvironmentVariable("MY_TEST_VAR_2", null);
            System.IO.File.Delete(tmp);
        }
    }

    [Fact]
    public void EnvLoadFile_SkipsCommentsAndBlankLines()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tmp, "# This is a comment\n\nMY_TEST_VAR_3=value\n# Another comment\n");
            var result = Run($"let result = env.loadFile(\"{tmp.Replace("\\", "\\\\")}\");");
            Assert.Equal(1L, result);
            Assert.Equal("value", System.Environment.GetEnvironmentVariable("MY_TEST_VAR_3"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MY_TEST_VAR_3", null);
            System.IO.File.Delete(tmp);
        }
    }

    [Fact]
    public void EnvLoadFile_StripsQuotes()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tmp, "MY_TEST_VAR_4=\"quoted value\"\nMY_TEST_VAR_5='single quoted'\n");
            var result = Run($"let result = env.loadFile(\"{tmp.Replace("\\", "\\\\")}\");");
            Assert.Equal(2L, result);
            Assert.Equal("quoted value", System.Environment.GetEnvironmentVariable("MY_TEST_VAR_4"));
            Assert.Equal("single quoted", System.Environment.GetEnvironmentVariable("MY_TEST_VAR_5"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MY_TEST_VAR_4", null);
            System.Environment.SetEnvironmentVariable("MY_TEST_VAR_5", null);
            System.IO.File.Delete(tmp);
        }
    }

    [Fact]
    public void EnvLoadFile_NonExistentFile_ThrowsError()
    {
        RunExpectingError("env.loadFile(\"/nonexistent/path/.env\");");
    }

    [Fact]
    public void EnvSaveFile_WritesVariables()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            System.Environment.SetEnvironmentVariable("MY_SAVE_TEST_VAR", "testvalue");
            Run($"env.saveFile(\"{tmp.Replace("\\", "\\\\")}\"); let result = 1;");
            string content = System.IO.File.ReadAllText(tmp);
            Assert.Contains("MY_SAVE_TEST_VAR=testvalue", content);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MY_SAVE_TEST_VAR", null);
            System.IO.File.Delete(tmp);
        }
    }

    [Fact]
    public void EnvSaveFile_NonStringArg_ThrowsError()
    {
        RunExpectingError("env.saveFile(42);");
    }

    // ── env.withPrefix tests ─────────────────────────────────────────────────

    [Fact]
    public void EnvWithPrefix_ReturnsMatchingVars()
    {
        var result = Run(@"
            env.set(""STASH_PFX_A"", ""alpha"");
            env.set(""STASH_PFX_B"", ""bravo"");
            env.set(""STASH_OTHER_C"", ""charlie"");
            let d = env.withPrefix(""STASH_PFX_"");
            let result = dict.has(d, ""STASH_PFX_A"") && dict.has(d, ""STASH_PFX_B"") && !dict.has(d, ""STASH_OTHER_C"");
        ");
        try
        {
            Assert.Equal(true, result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("STASH_PFX_A", null);
            System.Environment.SetEnvironmentVariable("STASH_PFX_B", null);
            System.Environment.SetEnvironmentVariable("STASH_OTHER_C", null);
        }
    }

    [Fact]
    public void EnvWithPrefix_ReturnsEmptyDictWhenNoMatch()
    {
        Assert.Equal(0L, Run(@"
            let d = env.withPrefix(""STASH_ZZZZ_NONEXISTENT_"");
            let result = dict.size(d);
        "));
    }

    [Fact]
    public void EnvWithPrefix_NonStringArg_ThrowsError()
    {
        RunExpectingError("env.withPrefix(42);");
    }

    [Fact]
    public void EnvWithPrefix_PreservesFullKeyNames()
    {
        try
        {
            System.Environment.SetEnvironmentVariable("STASH_WP_KEY1", "val1");
            var result = Run(@"
                let d = env.withPrefix(""STASH_WP_"");
                let result = d[""STASH_WP_KEY1""];
            ");
            Assert.Equal("val1", result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("STASH_WP_KEY1", null);
        }
    }

    // ── env.loadFile with prefix tests ───────────────────────────────────────

    [Fact]
    public void EnvLoadFile_WithPrefix_AddsPrefix()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tmp, "HOST=localhost\nPORT=8080\n");
            var result = Run($"let result = env.loadFile(\"{tmp.Replace("\\", "\\\\")}\", \"MYAPP_\");");
            Assert.Equal(2L, result);
            Assert.Equal("localhost", System.Environment.GetEnvironmentVariable("MYAPP_HOST"));
            Assert.Equal("8080", System.Environment.GetEnvironmentVariable("MYAPP_PORT"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MYAPP_HOST", null);
            System.Environment.SetEnvironmentVariable("MYAPP_PORT", null);
            System.IO.File.Delete(tmp);
        }
    }

    [Fact]
    public void EnvLoadFile_WithoutPrefix_WorksAsDefault()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tmp, "STASH_LF_NOPREFIX=value123\n");
            var result = Run($"let result = env.loadFile(\"{tmp.Replace("\\", "\\\\")}\");");
            Assert.Equal(1L, result);
            Assert.Equal("value123", System.Environment.GetEnvironmentVariable("STASH_LF_NOPREFIX"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("STASH_LF_NOPREFIX", null);
            System.IO.File.Delete(tmp);
        }
    }

    [Fact]
    public void EnvLoadFile_PrefixNonString_ThrowsError()
    {
        string tmp = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tmp, "KEY=val\n");
            RunExpectingError($"env.loadFile(\"{tmp.Replace("\\", "\\\\")}\", 42);");
        }
        finally
        {
            System.IO.File.Delete(tmp);
        }
    }

    [Fact]
    public void EnvLoadFile_TooManyArgs_ThrowsError()
    {
        RunExpectingError("env.loadFile(\"path\", \"prefix\", \"extra\");");
    }

    // ── Default parameter value tests ────────────────────────────────────────

    [Fact]
    public void Function_DefaultParam_UsedWhenArgOmitted()
    {
        Assert.Equal("Hello", Run("fn greet(greeting = \"Hello\") { return greeting; } let result = greet();"));
    }

    [Fact]
    public void Function_DefaultParam_OverriddenWhenArgProvided()
    {
        Assert.Equal("Hi", Run("fn greet(greeting = \"Hello\") { return greeting; } let result = greet(\"Hi\");"));
    }

    [Fact]
    public void Function_DefaultParam_MixedRequiredAndOptional()
    {
        Assert.Equal(15L, Run("fn add(a, b = 10) { return a + b; } let result = add(5);"));
    }

    [Fact]
    public void Function_DefaultParam_AllOptionalProvided()
    {
        Assert.Equal(7L, Run("fn add(a, b = 10) { return a + b; } let result = add(5, 2);"));
    }

    [Fact]
    public void Function_DefaultParam_MultipleDefaults()
    {
        Assert.Equal("A-B-C", Run("fn join(a, b = \"B\", c = \"C\") { return a + \"-\" + b + \"-\" + c; } let result = join(\"A\");"));
    }

    [Fact]
    public void Function_DefaultParam_PartialOverride()
    {
        Assert.Equal("A-X-C", Run("fn join(a, b = \"B\", c = \"C\") { return a + \"-\" + b + \"-\" + c; } let result = join(\"A\", \"X\");"));
    }

    [Fact]
    public void Function_DefaultParam_TooFewArgs_ThrowsError()
    {
        RunExpectingError("fn f(a, b = 5) { return a + b; } f();");
    }

    [Fact]
    public void Function_DefaultParam_TooManyArgs_ThrowsError()
    {
        RunExpectingError("fn f(a, b = 5) { return a + b; } f(1, 2, 3);");
    }

    [Fact]
    public void Function_DefaultParam_NullDefault()
    {
        Assert.Null(Run("fn f(a = null) { return a; } let result = f();"));
    }

    [Fact]
    public void Function_DefaultParam_BoolDefault()
    {
        Assert.Equal(false, Run("fn f(verbose = false) { return verbose; } let result = f();"));
    }

    [Fact]
    public void Function_DefaultParam_ExpressionDefault()
    {
        Assert.Equal(15L, Run("let base = 10; fn f(x = base + 5) { return x; } let result = f();"));
    }

    [Fact]
    public void Lambda_DefaultParam_UsedWhenArgOmitted()
    {
        Assert.Equal(20L, Run("let double = (x, factor = 2) => x * factor; let result = double(10);"));
    }

    [Fact]
    public void Lambda_DefaultParam_OverriddenWhenArgProvided()
    {
        Assert.Equal(30L, Run("let multiply = (x, factor = 2) => x * factor; let result = multiply(10, 3);"));
    }

    [Fact]
    public void Lambda_DefaultParam_BlockBody()
    {
        Assert.Equal(10L, Run(@"
            let f = (a, b = 5) => {
                return a + b;
            };
            let result = f(5);
        "));
    }


    // ── In operator ───────────────────────────────────────────────────

    [Fact]
    public void InOperator_Array_ContainsElement_ReturnsTrue()
    {
        Assert.Equal(true, Eval("42 in [1, 42, 99]"));
    }

    [Fact]
    public void InOperator_Array_DoesNotContain_ReturnsFalse()
    {
        Assert.Equal(false, Eval("5 in [1, 42, 99]"));
    }

    [Fact]
    public void InOperator_String_ContainsSubstring_ReturnsTrue()
    {
        Assert.Equal(true, Eval("\"error\" in \"file error found\""));
    }

    [Fact]
    public void InOperator_String_DoesNotContain_ReturnsFalse()
    {
        Assert.Equal(false, Eval("\"warning\" in \"file error found\""));
    }

    [Fact]
    public void InOperator_Dict_ContainsKey_ReturnsTrue()
    {
        Assert.Equal(true, Run("let d = dict.new(); dict.set(d, \"host\", \"localhost\"); let result = \"host\" in d;"));
    }

    [Fact]
    public void InOperator_Dict_MissingKey_ReturnsFalse()
    {
        Assert.Equal(false, Run("let d = dict.new(); dict.set(d, \"host\", \"localhost\"); let result = \"port\" in d;"));
    }

    [Fact]
    public void InOperator_WithLogicalAnd()
    {
        Assert.Equal(true, Eval("1 in [1, 2] && 3 in [3, 4]"));
    }

    [Fact]
    public void InOperator_NegationWithBang()
    {
        Assert.Equal(true, Eval("!(5 in [1, 2, 3])"));
    }

    [Fact]
    public void InOperator_WrongRightType_ThrowsRuntimeError()
    {
        Assert.Throws<RuntimeError>(() => Eval("1 in 42"));
    }

    [Fact]
    public void InOperator_String_NonStringLeft_ThrowsRuntimeError()
    {
        Assert.Throws<RuntimeError>(() => Eval("42 in \"hello\""));
    }

    // ===== Range expressions =====

    [Fact]
    public void ForIn_Range_IteratesCorrectly()
    {
        Assert.Equal("01234", Run("let result = \"\"; for (let i in 0..5) { result = result + conv.toStr(i); }"));
    }

    [Fact]
    public void ForIn_RangeWithStep_IteratesCorrectly()
    {
        Assert.Equal("02468", Run("let result = \"\"; for (let i in 0..10..2) { result = result + conv.toStr(i); }"));
    }

    [Fact]
    public void ForIn_RangeDescending_IteratesCorrectly()
    {
        Assert.Equal("54321", Run("let result = \"\"; for (let i in 5..0) { result = result + conv.toStr(i); }"));
    }

    [Fact]
    public void Range_TypeError_NonIntegerStart_ThrowsRuntimeError()
    {
        RunExpectingError("for (let i in 0.0..10) { }");
    }

    [Fact]
    public void Range_TypeError_NonIntegerEnd_ThrowsRuntimeError()
    {
        RunExpectingError("for (let i in 0..10.0) { }");
    }

    [Fact]
    public void Range_StepZero_ThrowsRuntimeError()
    {
        RunExpectingError("for (let i in 0..10..0) { }");
    }

    // ===== Destructuring =====

    [Fact]
    public void Destructure_Array_BindsVariables()
    {
        Assert.Equal(1L, Run("let [a, b, c] = [1, 2, 3]; let result = a;"));
    }

    [Fact]
    public void Destructure_Array_SecondVariable()
    {
        Assert.Equal(2L, Run("let [a, b] = [1, 2]; let result = b;"));
    }

    [Fact]
    public void Destructure_Array_PartialBinding()
    {
        Assert.Equal(1L, Run("let [a, b] = [1, 2, 3]; let result = a;"));
    }

    [Fact]
    public void Destructure_Array_ExcessNames_Null()
    {
        Assert.Null(Run("let [a, b, c] = [1]; let result = c;"));
    }

    [Fact]
    public void Destructure_Array_OnNonArray_Throws()
    {
        RunExpectingError("let [a, b] = \"hello\";");
    }

    [Fact]
    public void Destructure_Const_Array()
    {
        Assert.Equal(1L, Run("const [a, b] = [1, 2]; let result = a;"));
    }

    [Fact]
    public void Destructure_Const_Array_ReassignThrows()
    {
        RunExpectingError("const [a, b] = [1, 2]; a = 99;");
    }

    [Fact]
    public void Destructure_Object_BindsFromDict()
    {
        Assert.Equal("Alice", Run("let d = dict.new(); d[\"name\"] = \"Alice\"; d[\"age\"] = 30; let { name, age } = d; let result = name;"));
    }

    [Fact]
    public void Destructure_Object_BindsFromDict_SecondField()
    {
        Assert.Equal(30L, Run("let d = dict.new(); d[\"name\"] = \"Alice\"; d[\"age\"] = 30; let { name, age } = d; let result = age;"));
    }

    [Fact]
    public void Destructure_Object_BindsFromStruct()
    {
        Assert.Equal(10L, Run("struct Point { x, y } let p = Point { x: 10, y: 20 }; let { x, y } = p; let result = x;"));
    }

    [Fact]
    public void Destructure_Object_BindsFromStruct_SecondField()
    {
        Assert.Equal(20L, Run("struct Point { x, y } let p = Point { x: 10, y: 20 }; let { x, y } = p; let result = y;"));
    }

    [Fact]
    public void Destructure_Object_OnNonObject_Throws()
    {
        RunExpectingError("let { a } = 42;");
    }

    // ── Optional Chaining (?.) ──────────────────────────────────────────

    [Fact]
    public void OptionalChaining_NullObject_ReturnsNull()
    {
        Assert.Null(Run("let x = null; let result = x?.name;"));
    }

    [Fact]
    public void OptionalChaining_NonNullStruct_ReturnsField()
    {
        Assert.Equal("hello", Run("struct S { name } let x = S { name: \"hello\" }; let result = x?.name;"));
    }

    [Fact]
    public void OptionalChaining_NonNullDict_ReturnsValue()
    {
        Assert.Equal(42L, Run("let d = dict.new(); d[\"x\"] = 42; let result = d?.x;"));
    }

    [Fact]
    public void OptionalChaining_ChainedNullFirst_ReturnsNull()
    {
        Assert.Null(Run("let x = null; let result = x?.a?.b;"));
    }

    [Fact]
    public void OptionalChaining_ChainedNullMiddle_ReturnsNull()
    {
        Assert.Null(Run("let d = dict.new(); d[\"a\"] = null; let result = d?.a?.b;"));
    }

    [Fact]
    public void OptionalChaining_WithNullCoalescing_ReturnsDefault()
    {
        Assert.Equal("default", Run("let x = null; let result = x?.name ?? \"default\";"));
    }

    [Fact]
    public void OptionalChaining_NonNull_WithNullCoalescing_ReturnsValue()
    {
        Assert.Equal("hello", Run("struct S { name } let x = S { name: \"hello\" }; let result = x?.name ?? \"default\";"));
    }

    [Fact]
    public void OptionalChaining_RegularDotOnNull_Throws()
    {
        RunExpectingError("let x = null; let y = x.name;");
    }

    // ── Shorthand Struct Init ───────────────────────────────────────────

    [Fact]
    public void ShorthandStructInit_SingleField()
    {
        Assert.Equal("localhost", Run("struct S { host } let host = \"localhost\"; let s = S { host }; let result = s.host;"));
    }

    [Fact]
    public void ShorthandStructInit_MultipleFields()
    {
        Assert.Equal(8080L, Run("struct S { host, port } let host = \"localhost\"; let port = 8080; let s = S { host, port }; let result = s.port;"));
    }

    [Fact]
    public void ShorthandStructInit_MixedWithFull()
    {
        Assert.Equal("localhost", Run("struct S { host, port } let host = \"localhost\"; let s = S { host, port: 3000 }; let result = s.host;"));
    }

    [Fact]
    public void ShorthandStructInit_MixedWithFull_FullFirst()
    {
        Assert.Equal(9090L, Run("struct S { host, port } let port = 9090; let s = S { host: \"10.0.0.1\", port }; let result = s.port;"));
    }

    [Fact]
    public void ShorthandStructInit_UnknownField_Throws()
    {
        RunExpectingError("struct S { x } let y = 1; let s = S { y };");
    }

    [Fact]
    public void ShorthandStructInit_UndefinedVariable_Throws()
    {
        RunExpectingError("struct S { x } let s = S { x };");
    }

    // ── For-in with Index ───────────────────────────────────────────────

    [Fact]
    public void ForInIndex_Array_IndexStartsAtZero()
    {
        Assert.Equal(0L, Run("let result = -1; for (let i, item in [\"a\", \"b\", \"c\"]) { if (item == \"a\") { result = i; } }"));
    }

    [Fact]
    public void ForInIndex_Array_CorrectIndices()
    {
        Assert.Equal(6L, Run("let result = 0; for (let i, item in [10, 20, 30, 40]) { result = result + i; }"));
    }

    [Fact]
    public void ForInIndex_String_IteratesWithIndex()
    {
        Assert.Equal(2L, Run("let result = -1; for (let i, ch in \"hello\") { if (ch == \"l\") { result = i; break; } }"));
    }

    [Fact]
    public void ForInIndex_Range_IndexIsSeparateFromValue()
    {
        Assert.Equal(21L, Run("let result = 0; for (let i, val in 5..8) { result = result + i + val; }"));
    }

    [Fact]
    public void ForInIndex_Dict_KeyValueIteration()
    {
        Assert.Equal(6L, Run("let d = dict.new(); d[\"a\"] = 1; d[\"b\"] = 2; d[\"c\"] = 3; let result = 0; for (let key, val in d) { result = result + val; }"));
    }

    [Fact]
    public void ForInIndex_Dict_KeyIsCorrect()
    {
        Assert.Equal("b=2", Run("let d = dict.new(); d[\"a\"] = 1; d[\"b\"] = 2; let result = \"\"; for (let key, val in d) { if (key == \"b\") { result = key + \"=\" + val; } }"));
    }

    [Fact]
    public void ForInIndex_Dict_SingleVar_StillIteratesKeys()
    {
        Assert.Equal("abc", Run("let d = dict.new(); d[\"a\"] = 1; d[\"b\"] = 2; d[\"c\"] = 3; let result = \"\"; for (let key in d) { result = result + key; }"));
    }

    [Fact]
    public void ForInIndex_Dict_KeyValue_WithBreak()
    {
        Assert.Equal(1L, Run("let d = dict.new(); d[\"x\"] = 1; d[\"y\"] = 2; let result = 0; for (let key, val in d) { result = val; break; }"));
    }

    [Fact]
    public void ForInIndex_Dict_KeyValue_WithContinue()
    {
        Assert.Equal(4L, Run("let d = dict.new(); d[\"a\"] = 1; d[\"b\"] = 2; d[\"c\"] = 3; let result = 0; for (let key, val in d) { if (key == \"b\") { continue; } result = result + val; }"));
    }

    [Fact]
    public void ForInIndex_WithBreak_IndexCorrect()
    {
        Assert.Equal(1L, Run("let result = 0; for (let i, item in [10, 20, 30]) { result = i; if (item == 20) { break; } }"));
    }

    [Fact]
    public void ForInIndex_WithContinue_IndexStillIncrements()
    {
        Assert.Equal(5L, Run("let result = 0; for (let i, item in [10, 20, 30, 40]) { if (item == 20) { continue; } result = result + i; }"));
    }

    [Fact]
    public void ForInIndex_WithoutIndex_StillWorks()
    {
        Assert.Equal(6L, Run("let result = 0; for (let item in [1, 2, 3]) { result = result + item; }"));
    }

    // ── Array for-in snapshot safety ────────────────────────────────

    [Fact]
    public void ForIn_ArrayMutation_DoesNotCrash()
    {
        var result = Run(@"
            let items = [1, 2, 3];
            let result = 0;
            for (let item in items) {
                arr.push(items, 99);
                result = result + 1;
            }
        ");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void ForIn_ArrayRemoveDuringIteration()
    {
        var result = Run(@"
            let arr = [10, 20, 30];
            let sum = 0;
            for (let item in arr) {
                sum = sum + item;
            }
            let result = sum;
        ");
        Assert.Equal(60L, result);
    }

    // ── StashInstance.ToString() improvements ───────────────────────

    [Fact]
    public void StructToStr_MultipleFields()
    {
        var result = Run(@"
            struct Point { x, y }
            let p = Point { x: 10, y: 20 };
            let result = conv.toStr(p);
        ");
        Assert.Equal("Point { x: 10, y: 20 }", result);
    }

    [Fact]
    public void StructToStr_NestedStruct()
    {
        var result = Run(@"
            struct Inner { v }
            struct Outer { inner }
            let i = Inner { v: 42 };
            let o = Outer { inner: i };
            let result = conv.toStr(o);
        ");
        Assert.Equal("Outer { inner: Inner { v: 42 } }", result);
    }

    [Fact]
    public void StructToStr_InStringInterpolation()
    {
        var result = Run(@"
            struct P { x }
            let p = P { x: 5 };
            let result = ""value: ${p}"";
        ");
        Assert.Equal("value: P { x: 5 }", result);
    }

    [Fact]
    public void StructToStr_InConcatenation()
    {
        var result = Run(@"
            struct P { x }
            let p = P { x: 7 };
            let result = ""got: "" + p;
        ");
        Assert.Equal("got: P { x: 7 }", result);
    }

    // ── dict.map() ─────────────────────────────────────────────────

    [Fact]
    public void DictMap_TransformsValues()
    {
        var result = Run(@"
            let d = dict.new();
            d[""a""] = 1;
            d[""b""] = 2;
            let mapped = dict.map(d, (k, v) => v * 10);
            let result = mapped[""a""] + mapped[""b""];
        ");
        Assert.Equal(30L, result);
    }

    [Fact]
    public void DictMap_PreservesKeys()
    {
        var result = Run(@"
            let d = dict.new();
            d[""x""] = 5;
            let mapped = dict.map(d, (k, v) => k);
            let result = mapped[""x""];
        ");
        Assert.Equal("x", result);
    }

    [Fact]
    public void DictMap_DoesNotMutateOriginal()
    {
        var result = Run(@"
            let d = dict.new();
            d[""a""] = 1;
            dict.map(d, (k, v) => v * 100);
            let result = d[""a""];
        ");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void DictMap_EmptyDict()
    {
        var result = Run(@"
            let d = dict.new();
            let mapped = dict.map(d, (k, v) => v);
            let result = dict.size(mapped);
        ");
        Assert.Equal(0L, result);
    }

    // ── dict.filter() ──────────────────────────────────────────────

    [Fact]
    public void DictFilter_KeepsTruthyEntries()
    {
        var result = Run(@"
            let d = dict.new();
            d[""a""] = 1;
            d[""b""] = 20;
            d[""c""] = 3;
            let filtered = dict.filter(d, (k, v) => v > 5);
            let result = dict.size(filtered);
        ");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void DictFilter_ReturnsCorrectEntries()
    {
        var result = Run(@"
            let d = dict.new();
            d[""a""] = 1;
            d[""b""] = 20;
            let filtered = dict.filter(d, (k, v) => v > 5);
            let result = filtered[""b""];
        ");
        Assert.Equal(20L, result);
    }

    [Fact]
    public void DictFilter_DoesNotMutateOriginal()
    {
        var result = Run(@"
            let d = dict.new();
            d[""a""] = 1;
            d[""b""] = 2;
            dict.filter(d, (k, v) => v > 5);
            let result = dict.size(d);
        ");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void DictFilter_EmptyResult()
    {
        var result = Run(@"
            let d = dict.new();
            d[""a""] = 1;
            let filtered = dict.filter(d, (k, v) => false);
            let result = dict.size(filtered);
        ");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void DictFilter_ByKey()
    {
        var result = Run(@"
            let d = dict.new();
            d[""keep_me""] = 1;
            d[""skip_me""] = 2;
            d[""keep_this""] = 3;
            let filtered = dict.filter(d, (k, v) => str.startsWith(k, ""keep""));
            let result = dict.size(filtered);
        ");
        Assert.Equal(2L, result);
    }
}
