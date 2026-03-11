using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Parsing;

public class ParserTests
{
    private static Expr ParseExpr(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    // 1. Literal expressions

    [Fact]
    public void Parse_IntegerLiteral_ReturnsLiteralExprWithLongValue()
    {
        var result = ParseExpr("42");
        var literal = Assert.IsType<LiteralExpr>(result);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Parse_FloatLiteral_ReturnsLiteralExprWithDoubleValue()
    {
        var result = ParseExpr("3.14");
        var literal = Assert.IsType<LiteralExpr>(result);
        Assert.Equal(3.14, literal.Value);
    }

    [Fact]
    public void Parse_StringLiteral_ReturnsLiteralExprWithStringValue()
    {
        var result = ParseExpr("\"hello\"");
        var literal = Assert.IsType<LiteralExpr>(result);
        Assert.Equal("hello", literal.Value);
    }

    [Fact]
    public void Parse_TrueLiteral_ReturnsLiteralExprWithTrue()
    {
        var result = ParseExpr("true");
        var literal = Assert.IsType<LiteralExpr>(result);
        Assert.Equal(true, literal.Value);
    }

    [Fact]
    public void Parse_FalseLiteral_ReturnsLiteralExprWithFalse()
    {
        var result = ParseExpr("false");
        var literal = Assert.IsType<LiteralExpr>(result);
        Assert.Equal(false, literal.Value);
    }

    [Fact]
    public void Parse_NullLiteral_ReturnsLiteralExprWithNull()
    {
        var result = ParseExpr("null");
        var literal = Assert.IsType<LiteralExpr>(result);
        Assert.Null(literal.Value);
    }

    // 2. Unary negation

    [Fact]
    public void Parse_UnaryNegation_ReturnsUnaryExprWithMinus()
    {
        var result = ParseExpr("-42");
        var unary = Assert.IsType<UnaryExpr>(result);
        Assert.Equal(TokenType.Minus, unary.Operator.Type);
        var right = Assert.IsType<LiteralExpr>(unary.Right);
        Assert.Equal(42L, right.Value);
    }

    // 3. Unary not

    [Fact]
    public void Parse_UnaryNot_ReturnsUnaryExprWithBang()
    {
        var result = ParseExpr("!true");
        var unary = Assert.IsType<UnaryExpr>(result);
        Assert.Equal(TokenType.Bang, unary.Operator.Type);
        var right = Assert.IsType<LiteralExpr>(unary.Right);
        Assert.Equal(true, right.Value);
    }

    // 4. Double negation

    [Fact]
    public void Parse_DoubleNegation_ReturnsNestedUnaryExprs()
    {
        var result = ParseExpr("- -5");
        var outer = Assert.IsType<UnaryExpr>(result);
        Assert.Equal(TokenType.Minus, outer.Operator.Type);
        var inner = Assert.IsType<UnaryExpr>(outer.Right);
        Assert.Equal(TokenType.Minus, inner.Operator.Type);
        var literal = Assert.IsType<LiteralExpr>(inner.Right);
        Assert.Equal(5L, literal.Value);
    }

    // 5. Precedence - multiplication before addition

    [Fact]
    public void Parse_MultiplicationBeforeAddition_CorrectPrecedence()
    {
        var result = ParseExpr("1 + 2 * 3");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Plus, binary.Operator.Type);

        var left = Assert.IsType<LiteralExpr>(binary.Left);
        Assert.Equal(1L, left.Value);

        var right = Assert.IsType<BinaryExpr>(binary.Right);
        Assert.Equal(TokenType.Star, right.Operator.Type);
        var rightLeft = Assert.IsType<LiteralExpr>(right.Left);
        Assert.Equal(2L, rightLeft.Value);
        var rightRight = Assert.IsType<LiteralExpr>(right.Right);
        Assert.Equal(3L, rightRight.Value);
    }

    // 6. Precedence - addition and subtraction left-associative

    [Fact]
    public void Parse_AdditionSubtraction_LeftAssociative()
    {
        var result = ParseExpr("1 + 2 - 3");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Minus, binary.Operator.Type);

        var left = Assert.IsType<BinaryExpr>(binary.Left);
        Assert.Equal(TokenType.Plus, left.Operator.Type);
        var leftLeft = Assert.IsType<LiteralExpr>(left.Left);
        Assert.Equal(1L, leftLeft.Value);
        var leftRight = Assert.IsType<LiteralExpr>(left.Right);
        Assert.Equal(2L, leftRight.Value);

        var right = Assert.IsType<LiteralExpr>(binary.Right);
        Assert.Equal(3L, right.Value);
    }

    // 7. Precedence - multiplication before addition (left-assoc)

    [Fact]
    public void Parse_MultiplicationThenAddition_LeftAssociative()
    {
        var result = ParseExpr("1 * 2 + 3");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Plus, binary.Operator.Type);

        var left = Assert.IsType<BinaryExpr>(binary.Left);
        Assert.Equal(TokenType.Star, left.Operator.Type);
        var leftLeft = Assert.IsType<LiteralExpr>(left.Left);
        Assert.Equal(1L, leftLeft.Value);
        var leftRight = Assert.IsType<LiteralExpr>(left.Right);
        Assert.Equal(2L, leftRight.Value);

        var right = Assert.IsType<LiteralExpr>(binary.Right);
        Assert.Equal(3L, right.Value);
    }

    // 8. Division and modulo

    [Fact]
    public void Parse_Division_ReturnsBinaryExprWithSlash()
    {
        var result = ParseExpr("6 / 2");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Slash, binary.Operator.Type);
        var left = Assert.IsType<LiteralExpr>(binary.Left);
        Assert.Equal(6L, left.Value);
        var right = Assert.IsType<LiteralExpr>(binary.Right);
        Assert.Equal(2L, right.Value);
    }

    [Fact]
    public void Parse_Modulo_ReturnsBinaryExprWithPercent()
    {
        var result = ParseExpr("10 % 3");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Percent, binary.Operator.Type);
        var left = Assert.IsType<LiteralExpr>(binary.Left);
        Assert.Equal(10L, left.Value);
        var right = Assert.IsType<LiteralExpr>(binary.Right);
        Assert.Equal(3L, right.Value);
    }

    // 9. Comparison operators

    [Fact]
    public void Parse_LessThan_ReturnsBinaryExprWithLess()
    {
        var result = ParseExpr("1 < 2");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Less, binary.Operator.Type);
        var left = Assert.IsType<LiteralExpr>(binary.Left);
        Assert.Equal(1L, left.Value);
        var right = Assert.IsType<LiteralExpr>(binary.Right);
        Assert.Equal(2L, right.Value);
    }

    [Fact]
    public void Parse_LessEqual_ReturnsBinaryExprWithLessEqual()
    {
        var result = ParseExpr("1 <= 2");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.LessEqual, binary.Operator.Type);
    }

    [Fact]
    public void Parse_GreaterThan_ReturnsBinaryExprWithGreater()
    {
        var result = ParseExpr("2 > 1");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Greater, binary.Operator.Type);
    }

    [Fact]
    public void Parse_GreaterEqual_ReturnsBinaryExprWithGreaterEqual()
    {
        var result = ParseExpr("2 >= 1");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.GreaterEqual, binary.Operator.Type);
    }

    // 10. Equality operators

    [Fact]
    public void Parse_EqualEqual_ReturnsBinaryExprWithEqualEqual()
    {
        var result = ParseExpr("1 == 2");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.EqualEqual, binary.Operator.Type);
        var left = Assert.IsType<LiteralExpr>(binary.Left);
        Assert.Equal(1L, left.Value);
        var right = Assert.IsType<LiteralExpr>(binary.Right);
        Assert.Equal(2L, right.Value);
    }

    [Fact]
    public void Parse_BangEqual_ReturnsBinaryExprWithBangEqual()
    {
        var result = ParseExpr("1 != 2");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.BangEqual, binary.Operator.Type);
    }

    // 11. Logical AND

    [Fact]
    public void Parse_LogicalAnd_ReturnsBinaryExprWithAmpersandAmpersand()
    {
        var result = ParseExpr("true && false");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.AmpersandAmpersand, binary.Operator.Type);
        var left = Assert.IsType<LiteralExpr>(binary.Left);
        Assert.Equal(true, left.Value);
        var right = Assert.IsType<LiteralExpr>(binary.Right);
        Assert.Equal(false, right.Value);
    }

    // 12. Logical OR

    [Fact]
    public void Parse_LogicalOr_ReturnsBinaryExprWithPipePipe()
    {
        var result = ParseExpr("true || false");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.PipePipe, binary.Operator.Type);
        var left = Assert.IsType<LiteralExpr>(binary.Left);
        Assert.Equal(true, left.Value);
        var right = Assert.IsType<LiteralExpr>(binary.Right);
        Assert.Equal(false, right.Value);
    }

    // 13. Logical precedence - AND binds tighter than OR

    [Fact]
    public void Parse_LogicalPrecedence_AndBindsTighterThanOr()
    {
        var result = ParseExpr("true || false && true");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.PipePipe, binary.Operator.Type);

        var left = Assert.IsType<LiteralExpr>(binary.Left);
        Assert.Equal(true, left.Value);

        var right = Assert.IsType<BinaryExpr>(binary.Right);
        Assert.Equal(TokenType.AmpersandAmpersand, right.Operator.Type);
        var rightLeft = Assert.IsType<LiteralExpr>(right.Left);
        Assert.Equal(false, rightLeft.Value);
        var rightRight = Assert.IsType<LiteralExpr>(right.Right);
        Assert.Equal(true, rightRight.Value);
    }

    // 14. Grouping overrides precedence

    [Fact]
    public void Parse_GroupingOverridesPrecedence()
    {
        var result = ParseExpr("(1 + 2) * 3");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Star, binary.Operator.Type);

        var grouping = Assert.IsType<GroupingExpr>(binary.Left);
        var inner = Assert.IsType<BinaryExpr>(grouping.Expression);
        Assert.Equal(TokenType.Plus, inner.Operator.Type);
        var innerLeft = Assert.IsType<LiteralExpr>(inner.Left);
        Assert.Equal(1L, innerLeft.Value);
        var innerRight = Assert.IsType<LiteralExpr>(inner.Right);
        Assert.Equal(2L, innerRight.Value);

        var right = Assert.IsType<LiteralExpr>(binary.Right);
        Assert.Equal(3L, right.Value);
    }

    // 15. Nested grouping

    [Fact]
    public void Parse_NestedGrouping_ReturnsNestedGroupingExprs()
    {
        var result = ParseExpr("((1))");
        var outer = Assert.IsType<GroupingExpr>(result);
        var inner = Assert.IsType<GroupingExpr>(outer.Expression);
        var literal = Assert.IsType<LiteralExpr>(inner.Expression);
        Assert.Equal(1L, literal.Value);
    }

    // 16. Ternary expression

    [Fact]
    public void Parse_Ternary_ReturnsTernaryExpr()
    {
        var result = ParseExpr("true ? 1 : 2");
        var ternary = Assert.IsType<TernaryExpr>(result);

        var condition = Assert.IsType<LiteralExpr>(ternary.Condition);
        Assert.Equal(true, condition.Value);

        var thenBranch = Assert.IsType<LiteralExpr>(ternary.ThenBranch);
        Assert.Equal(1L, thenBranch.Value);

        var elseBranch = Assert.IsType<LiteralExpr>(ternary.ElseBranch);
        Assert.Equal(2L, elseBranch.Value);
    }

    // 17. Ternary with nested expressions

    [Fact]
    public void Parse_TernaryWithNestedExpressions()
    {
        var result = ParseExpr("1 > 2 ? 3 + 4 : 5 * 6");
        var ternary = Assert.IsType<TernaryExpr>(result);

        var condition = Assert.IsType<BinaryExpr>(ternary.Condition);
        Assert.Equal(TokenType.Greater, condition.Operator.Type);
        var condLeft = Assert.IsType<LiteralExpr>(condition.Left);
        Assert.Equal(1L, condLeft.Value);
        var condRight = Assert.IsType<LiteralExpr>(condition.Right);
        Assert.Equal(2L, condRight.Value);

        var thenBranch = Assert.IsType<BinaryExpr>(ternary.ThenBranch);
        Assert.Equal(TokenType.Plus, thenBranch.Operator.Type);
        var thenLeft = Assert.IsType<LiteralExpr>(thenBranch.Left);
        Assert.Equal(3L, thenLeft.Value);
        var thenRight = Assert.IsType<LiteralExpr>(thenBranch.Right);
        Assert.Equal(4L, thenRight.Value);

        var elseBranch = Assert.IsType<BinaryExpr>(ternary.ElseBranch);
        Assert.Equal(TokenType.Star, elseBranch.Operator.Type);
        var elseLeft = Assert.IsType<LiteralExpr>(elseBranch.Left);
        Assert.Equal(5L, elseLeft.Value);
        var elseRight = Assert.IsType<LiteralExpr>(elseBranch.Right);
        Assert.Equal(6L, elseRight.Value);
    }

    // 18. Error - missing closing paren

    [Fact]
    public void Parse_MissingClosingParen_RecordsError()
    {
        var lexer = new Lexer("(1 + 2");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        parser.Parse();
        Assert.NotEmpty(parser.Errors);
    }

    // 19. Error - empty input

    [Fact]
    public void Parse_EmptyInput_RecordsError()
    {
        var lexer = new Lexer("");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        parser.Parse();
        Assert.NotEmpty(parser.Errors);
    }

    // 20. Complex expression

    [Fact]
    public void Parse_ComplexExpression_CorrectPrecedence()
    {
        var result = ParseExpr("1 + 2 * 3 == 7");
        var equality = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.EqualEqual, equality.Operator.Type);

        var left = Assert.IsType<BinaryExpr>(equality.Left);
        Assert.Equal(TokenType.Plus, left.Operator.Type);

        var leftLeft = Assert.IsType<LiteralExpr>(left.Left);
        Assert.Equal(1L, leftLeft.Value);

        var leftRight = Assert.IsType<BinaryExpr>(left.Right);
        Assert.Equal(TokenType.Star, leftRight.Operator.Type);
        var mulLeft = Assert.IsType<LiteralExpr>(leftRight.Left);
        Assert.Equal(2L, mulLeft.Value);
        var mulRight = Assert.IsType<LiteralExpr>(leftRight.Right);
        Assert.Equal(3L, mulRight.Value);

        var right = Assert.IsType<LiteralExpr>(equality.Right);
        Assert.Equal(7L, right.Value);
    }
}
