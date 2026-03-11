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
}
