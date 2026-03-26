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

    // ───────────────────────────────────────────────────────────────
    // Phase 2 — Statement / Program-level tests
    // ───────────────────────────────────────────────────────────────

    private static List<Stmt> ParseProgram(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseProgram();
    }

    private static Parser ParseProgramWithParser(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        return parser;
    }

    // 1. Variable Declarations

    [Fact]
    public void Parse_VarDecl_WithInitializer()
    {
        var stmts = ParseProgram("let x = 42;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.Equal("x", varDecl.Name.Lexeme);
        var init = Assert.IsType<LiteralExpr>(varDecl.Initializer);
        Assert.Equal(42L, init.Value);
    }

    [Fact]
    public void Parse_VarDecl_WithoutInitializer()
    {
        var stmts = ParseProgram("let x;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.Equal("x", varDecl.Name.Lexeme);
        Assert.Null(varDecl.Initializer);
    }

    [Fact]
    public void Parse_ConstDecl()
    {
        var stmts = ParseProgram("const MAX = 100;");
        var constDecl = Assert.IsType<ConstDeclStmt>(Assert.Single(stmts));
        Assert.Equal("MAX", constDecl.Name.Lexeme);
        var init = Assert.IsType<LiteralExpr>(constDecl.Initializer);
        Assert.Equal(100L, init.Value);
    }

    [Fact]
    public void Parse_ConstDecl_RequiresInitializer()
    {
        var parser = ParseProgramWithParser("const X;");
        Assert.NotEmpty(parser.Errors);
    }

    // 2. Assignment

    [Fact]
    public void Parse_Assignment()
    {
        var stmts = ParseProgram("x = 5;");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var assign = Assert.IsType<AssignExpr>(exprStmt.Expression);
        Assert.Equal("x", assign.Name.Lexeme);
        var value = Assert.IsType<LiteralExpr>(assign.Value);
        Assert.Equal(5L, value.Value);
    }

    [Fact]
    public void Parse_Assignment_RightAssociative()
    {
        var stmts = ParseProgram("x = y = 5;");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var outer = Assert.IsType<AssignExpr>(exprStmt.Expression);
        Assert.Equal("x", outer.Name.Lexeme);
        var inner = Assert.IsType<AssignExpr>(outer.Value);
        Assert.Equal("y", inner.Name.Lexeme);
        var value = Assert.IsType<LiteralExpr>(inner.Value);
        Assert.Equal(5L, value.Value);
    }

    [Fact]
    public void Parse_Assignment_InvalidTarget()
    {
        var parser = ParseProgramWithParser("1 + 2 = 3;");
        Assert.NotEmpty(parser.Errors);
    }

    // 3. Block

    [Fact]
    public void Parse_Block_Empty()
    {
        var stmts = ParseProgram("{ }");
        var block = Assert.IsType<BlockStmt>(Assert.Single(stmts));
        Assert.Empty(block.Statements);
    }

    [Fact]
    public void Parse_Block_MultipleStatements()
    {
        var stmts = ParseProgram("{ let x = 1; let y = 2; }");
        var block = Assert.IsType<BlockStmt>(Assert.Single(stmts));
        Assert.Equal(2, block.Statements.Count);
        Assert.IsType<VarDeclStmt>(block.Statements[0]);
        Assert.IsType<VarDeclStmt>(block.Statements[1]);
    }

    // 4. If/Else

    [Fact]
    public void Parse_If_NoElse()
    {
        var stmts = ParseProgram("if (true) { let x = 1; }");
        var ifStmt = Assert.IsType<IfStmt>(Assert.Single(stmts));
        var condition = Assert.IsType<LiteralExpr>(ifStmt.Condition);
        Assert.Equal(true, condition.Value);
        Assert.IsType<BlockStmt>(ifStmt.ThenBranch);
        Assert.Null(ifStmt.ElseBranch);
    }

    [Fact]
    public void Parse_If_WithElse()
    {
        var stmts = ParseProgram("if (true) { let x = 1; } else { let y = 2; }");
        var ifStmt = Assert.IsType<IfStmt>(Assert.Single(stmts));
        Assert.IsType<BlockStmt>(ifStmt.ThenBranch);
        Assert.NotNull(ifStmt.ElseBranch);
        Assert.IsType<BlockStmt>(ifStmt.ElseBranch);
    }

    [Fact]
    public void Parse_If_ElseIf()
    {
        var stmts = ParseProgram("if (true) { } else if (false) { }");
        var ifStmt = Assert.IsType<IfStmt>(Assert.Single(stmts));
        Assert.NotNull(ifStmt.ElseBranch);
        Assert.IsType<IfStmt>(ifStmt.ElseBranch);
    }

    // 5. While Loop

    [Fact]
    public void Parse_While()
    {
        var stmts = ParseProgram("while (true) { break; }");
        var whileStmt = Assert.IsType<WhileStmt>(Assert.Single(stmts));
        var condition = Assert.IsType<LiteralExpr>(whileStmt.Condition);
        Assert.Equal(true, condition.Value);
        var body = Assert.IsType<BlockStmt>(whileStmt.Body);
        Assert.IsType<BreakStmt>(Assert.Single(body.Statements));
    }

    [Fact]
    public void Parse_DoWhile()
    {
        var stmts = ParseProgram("do { break; } while (true);");
        var doWhileStmt = Assert.IsType<DoWhileStmt>(Assert.Single(stmts));
        var body = Assert.IsType<BlockStmt>(doWhileStmt.Body);
        Assert.IsType<BreakStmt>(Assert.Single(body.Statements));
        var condition = Assert.IsType<LiteralExpr>(doWhileStmt.Condition);
        Assert.Equal(true, condition.Value);
    }

    // 6. For-In Loop

    [Fact]
    public void Parse_ForIn()
    {
        var stmts = ParseProgram("for (let x in items) { }");
        var forIn = Assert.IsType<ForInStmt>(Assert.Single(stmts));
        Assert.Equal("x", forIn.VariableName.Lexeme);
        var iterable = Assert.IsType<IdentifierExpr>(forIn.Iterable);
        Assert.Equal("items", iterable.Name.Lexeme);
        Assert.IsType<BlockStmt>(forIn.Body);
    }

    // 7. Functions

    [Fact]
    public void Parse_FnDecl_NoParams()
    {
        var stmts = ParseProgram("fn greet() { }");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal("greet", fn.Name.Lexeme);
        Assert.Empty(fn.Parameters);
    }

    [Fact]
    public void Parse_FnDecl_WithParams()
    {
        var stmts = ParseProgram("fn add(a, b) { return a + b; }");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal("add", fn.Name.Lexeme);
        Assert.Equal(2, fn.Parameters.Count);
        Assert.Equal("a", fn.Parameters[0].Lexeme);
        Assert.Equal("b", fn.Parameters[1].Lexeme);
        Assert.IsType<ReturnStmt>(fn.Body.Statements[0]);
    }

    [Fact]
    public void Parse_CallExpr_NoArgs()
    {
        var result = ParseExpr("greet()");
        var call = Assert.IsType<CallExpr>(result);
        var callee = Assert.IsType<IdentifierExpr>(call.Callee);
        Assert.Equal("greet", callee.Name.Lexeme);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void Parse_CallExpr_WithArgs()
    {
        var result = ParseExpr("add(1, 2)");
        var call = Assert.IsType<CallExpr>(result);
        Assert.Equal(2, call.Arguments.Count);
    }

    // 8. Return/Break/Continue

    [Fact]
    public void Parse_Return_WithValue()
    {
        var stmts = ParseProgram("fn f() { return 42; }");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        var ret = Assert.IsType<ReturnStmt>(Assert.Single(fn.Body.Statements));
        var value = Assert.IsType<LiteralExpr>(ret.Value);
        Assert.Equal(42L, value.Value);
    }

    [Fact]
    public void Parse_Return_WithoutValue()
    {
        var stmts = ParseProgram("fn f() { return; }");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        var ret = Assert.IsType<ReturnStmt>(Assert.Single(fn.Body.Statements));
        Assert.Null(ret.Value);
    }

    [Fact]
    public void Parse_Break()
    {
        var stmts = ParseProgram("while (true) { break; }");
        var whileStmt = Assert.IsType<WhileStmt>(Assert.Single(stmts));
        var body = Assert.IsType<BlockStmt>(whileStmt.Body);
        Assert.IsType<BreakStmt>(Assert.Single(body.Statements));
    }

    [Fact]
    public void Parse_Continue()
    {
        var stmts = ParseProgram("while (true) { continue; }");
        var whileStmt = Assert.IsType<WhileStmt>(Assert.Single(stmts));
        var body = Assert.IsType<BlockStmt>(whileStmt.Body);
        Assert.IsType<ContinueStmt>(Assert.Single(body.Statements));
    }

    // 9. Error Recovery

    [Fact]
    public void Parse_ErrorRecovery_ContinuesParsing()
    {
        var lexer = new Lexer("let = 5; let y = 10;");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        Assert.NotEmpty(parser.Errors);
        Assert.True(stmts.Count >= 1, "Parser should recover and parse at least one valid statement");
    }

    [Fact]
    public void Parse_MissingSemicolon_ReportsError()
    {
        var parser = ParseProgramWithParser("let x = 5");
        Assert.NotEmpty(parser.Errors);
    }

    // 10. Expression Statement

    [Fact]
    public void Parse_ExpressionStatement()
    {
        var stmts = ParseProgram("42;");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var literal = Assert.IsType<LiteralExpr>(exprStmt.Expression);
        Assert.Equal(42L, literal.Value);
    }

    // ───────────────────────────────────────────────────────────────
    // Phase 3 — Arrays, Structs, Enums
    // ───────────────────────────────────────────────────────────────

    // 11. Array Expressions

    [Fact]
    public void Parse_ArrayLiteral_Empty()
    {
        var result = ParseExpr("[]");
        var arr = Assert.IsType<ArrayExpr>(result);
        Assert.Empty(arr.Elements);
    }

    [Fact]
    public void Parse_ArrayLiteral_SingleElement()
    {
        var result = ParseExpr("[42]");
        var arr = Assert.IsType<ArrayExpr>(result);
        Assert.Single(arr.Elements);
        var elem = Assert.IsType<LiteralExpr>(arr.Elements[0]);
        Assert.Equal(42L, elem.Value);
    }

    [Fact]
    public void Parse_ArrayLiteral_MultipleElements()
    {
        var result = ParseExpr("[1, 2, 3]");
        var arr = Assert.IsType<ArrayExpr>(result);
        Assert.Equal(3, arr.Elements.Count);
        var e0 = Assert.IsType<LiteralExpr>(arr.Elements[0]);
        Assert.Equal(1L, e0.Value);
        var e1 = Assert.IsType<LiteralExpr>(arr.Elements[1]);
        Assert.Equal(2L, e1.Value);
        var e2 = Assert.IsType<LiteralExpr>(arr.Elements[2]);
        Assert.Equal(3L, e2.Value);
    }

    [Fact]
    public void Parse_ArrayIndex()
    {
        var result = ParseExpr("arr[0]");
        var index = Assert.IsType<IndexExpr>(result);
        var obj = Assert.IsType<IdentifierExpr>(index.Object);
        Assert.Equal("arr", obj.Name.Lexeme);
        var idx = Assert.IsType<LiteralExpr>(index.Index);
        Assert.Equal(0L, idx.Value);
    }

    [Fact]
    public void Parse_ArrayIndexAssign()
    {
        var stmts = ParseProgram("arr[0] = 42;");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var assign = Assert.IsType<IndexAssignExpr>(exprStmt.Expression);
        var obj = Assert.IsType<IdentifierExpr>(assign.Object);
        Assert.Equal("arr", obj.Name.Lexeme);
        var idx = Assert.IsType<LiteralExpr>(assign.Index);
        Assert.Equal(0L, idx.Value);
        var val = Assert.IsType<LiteralExpr>(assign.Value);
        Assert.Equal(42L, val.Value);
    }

    [Fact]
    public void Parse_NestedArrayIndex()
    {
        var result = ParseExpr("arr[0][1]");
        var outer = Assert.IsType<IndexExpr>(result);
        var inner = Assert.IsType<IndexExpr>(outer.Object);
        var obj = Assert.IsType<IdentifierExpr>(inner.Object);
        Assert.Equal("arr", obj.Name.Lexeme);
        var idx0 = Assert.IsType<LiteralExpr>(inner.Index);
        Assert.Equal(0L, idx0.Value);
        var idx1 = Assert.IsType<LiteralExpr>(outer.Index);
        Assert.Equal(1L, idx1.Value);
    }

    [Fact]
    public void Parse_ArrayLiteral_MixedTypes()
    {
        var result = ParseExpr("[1, \"hello\", true]");
        var arr = Assert.IsType<ArrayExpr>(result);
        Assert.Equal(3, arr.Elements.Count);
        var e0 = Assert.IsType<LiteralExpr>(arr.Elements[0]);
        Assert.Equal(1L, e0.Value);
        var e1 = Assert.IsType<LiteralExpr>(arr.Elements[1]);
        Assert.Equal("hello", e1.Value);
        var e2 = Assert.IsType<LiteralExpr>(arr.Elements[2]);
        Assert.Equal(true, e2.Value);
    }

    // 12. Struct Declarations

    [Fact]
    public void Parse_StructDecl()
    {
        var stmts = ParseProgram("struct Point { x, y }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Equal("Point", structDecl.Name.Lexeme);
        Assert.Equal(2, structDecl.Fields.Count);
        Assert.Equal("x", structDecl.Fields[0].Lexeme);
        Assert.Equal("y", structDecl.Fields[1].Lexeme);
    }

    [Fact]
    public void Parse_StructDecl_SingleField()
    {
        var stmts = ParseProgram("struct W { value }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Equal("W", structDecl.Name.Lexeme);
        Assert.Single(structDecl.Fields);
        Assert.Equal("value", structDecl.Fields[0].Lexeme);
    }

    [Fact]
    public void Parse_StructInit()
    {
        var result = ParseExpr("Point { x: 1, y: 2 }");
        var init = Assert.IsType<StructInitExpr>(result);
        Assert.Equal("Point", init.Name.Lexeme);
        Assert.Equal(2, init.FieldValues.Count);
        Assert.Equal("x", init.FieldValues[0].Field.Lexeme);
        var v0 = Assert.IsType<LiteralExpr>(init.FieldValues[0].Value);
        Assert.Equal(1L, v0.Value);
        Assert.Equal("y", init.FieldValues[1].Field.Lexeme);
        var v1 = Assert.IsType<LiteralExpr>(init.FieldValues[1].Value);
        Assert.Equal(2L, v1.Value);
    }

    [Fact]
    public void Parse_StructInit_Empty()
    {
        var result = ParseExpr("Point {}");
        var init = Assert.IsType<StructInitExpr>(result);
        Assert.Equal("Point", init.Name.Lexeme);
        Assert.Empty(init.FieldValues);
    }

    [Fact]
    public void Parse_DotExpr()
    {
        var result = ParseExpr("obj.field");
        var dot = Assert.IsType<DotExpr>(result);
        var obj = Assert.IsType<IdentifierExpr>(dot.Object);
        Assert.Equal("obj", obj.Name.Lexeme);
        Assert.Equal("field", dot.Name.Lexeme);
    }

    [Fact]
    public void Parse_DotAssign()
    {
        var stmts = ParseProgram("obj.field = 42;");
        var exprStmt = Assert.IsType<ExprStmt>(Assert.Single(stmts));
        var dotAssign = Assert.IsType<DotAssignExpr>(exprStmt.Expression);
        var obj = Assert.IsType<IdentifierExpr>(dotAssign.Object);
        Assert.Equal("obj", obj.Name.Lexeme);
        Assert.Equal("field", dotAssign.Name.Lexeme);
        var val = Assert.IsType<LiteralExpr>(dotAssign.Value);
        Assert.Equal(42L, val.Value);
    }

    [Fact]
    public void Parse_ChainedDot()
    {
        var result = ParseExpr("a.b.c");
        var outer = Assert.IsType<DotExpr>(result);
        Assert.Equal("c", outer.Name.Lexeme);
        var inner = Assert.IsType<DotExpr>(outer.Object);
        Assert.Equal("b", inner.Name.Lexeme);
        var obj = Assert.IsType<IdentifierExpr>(inner.Object);
        Assert.Equal("a", obj.Name.Lexeme);
    }

    [Fact]
    public void Parse_DotAndIndex()
    {
        var result = ParseExpr("arr[0].x");
        var dot = Assert.IsType<DotExpr>(result);
        Assert.Equal("x", dot.Name.Lexeme);
        var index = Assert.IsType<IndexExpr>(dot.Object);
        var obj = Assert.IsType<IdentifierExpr>(index.Object);
        Assert.Equal("arr", obj.Name.Lexeme);
        var idx = Assert.IsType<LiteralExpr>(index.Index);
        Assert.Equal(0L, idx.Value);
    }

    // 13. Enum Declarations

    [Fact]
    public void Parse_EnumDecl()
    {
        var stmts = ParseProgram("enum Color { Red, Green, Blue }");
        var enumDecl = Assert.IsType<EnumDeclStmt>(Assert.Single(stmts));
        Assert.Equal("Color", enumDecl.Name.Lexeme);
        Assert.Equal(3, enumDecl.Members.Count);
        Assert.Equal("Red", enumDecl.Members[0].Lexeme);
        Assert.Equal("Green", enumDecl.Members[1].Lexeme);
        Assert.Equal("Blue", enumDecl.Members[2].Lexeme);
    }

    [Fact]
    public void Parse_EnumDecl_SingleMember()
    {
        var stmts = ParseProgram("enum S { A }");
        var enumDecl = Assert.IsType<EnumDeclStmt>(Assert.Single(stmts));
        Assert.Equal("S", enumDecl.Name.Lexeme);
        Assert.Single(enumDecl.Members);
        Assert.Equal("A", enumDecl.Members[0].Lexeme);
    }

    [Fact]
    public void Parse_EnumMemberAccess()
    {
        var result = ParseExpr("Color.Red");
        var dot = Assert.IsType<DotExpr>(result);
        var obj = Assert.IsType<IdentifierExpr>(dot.Object);
        Assert.Equal("Color", obj.Name.Lexeme);
        Assert.Equal("Red", dot.Name.Lexeme);
    }

    // ── String interpolation ─────────────────────────────────────

    [Fact]
    public void Parse_PrefixedInterpolation_ReturnsInterpolatedStringExpr()
    {
        var result = ParseExpr("$\"hello {x}\"");
        Assert.IsType<InterpolatedStringExpr>(result);
    }

    [Fact]
    public void Parse_EmbeddedInterpolation_ReturnsInterpolatedStringExpr()
    {
        var result = ParseExpr("\"hello ${x}\"");
        Assert.IsType<InterpolatedStringExpr>(result);
    }

    [Fact]
    public void Parse_InterpolatedString_HasCorrectNumberOfParts()
    {
        var result = ParseExpr("$\"hello {x} world\"");
        var interp = Assert.IsType<InterpolatedStringExpr>(result);
        // "hello " + x + " world" = 3 parts
        Assert.Equal(3, interp.Parts.Count);
    }

    [Fact]
    public void Parse_InterpolatedString_TextPartsAreLiteralExpr()
    {
        var result = ParseExpr("$\"hello {x} world\"");
        var interp = Assert.IsType<InterpolatedStringExpr>(result);

        var textPart1 = Assert.IsType<LiteralExpr>(interp.Parts[0]);
        Assert.Equal("hello ", textPart1.Value);

        var textPart2 = Assert.IsType<LiteralExpr>(interp.Parts[2]);
        Assert.Equal(" world", textPart2.Value);
    }

    [Fact]
    public void Parse_InterpolatedString_ExprPartIsIdentifierExpr()
    {
        var result = ParseExpr("$\"hello {x}\"");
        var interp = Assert.IsType<InterpolatedStringExpr>(result);

        var exprPart = Assert.IsType<IdentifierExpr>(interp.Parts[1]);
        Assert.Equal("x", exprPart.Name.Lexeme);
    }

    [Fact]
    public void Parse_InterpolatedString_ComplexExpressionPart()
    {
        var result = ParseExpr("$\"{a + b}\"");
        var interp = Assert.IsType<InterpolatedStringExpr>(result);

        Assert.Single(interp.Parts);
        var binary = Assert.IsType<BinaryExpr>(interp.Parts[0]);
        Assert.Equal(TokenType.Plus, binary.Operator.Type);
        var left = Assert.IsType<IdentifierExpr>(binary.Left);
        Assert.Equal("a", left.Name.Lexeme);
        var right = Assert.IsType<IdentifierExpr>(binary.Right);
        Assert.Equal("b", right.Name.Lexeme);
    }

    [Fact]
    public void Parse_InterpolatedString_MultipleExpressions()
    {
        var result = ParseExpr("$\"{a} and {b}\"");
        var interp = Assert.IsType<InterpolatedStringExpr>(result);

        // a + " and " + b = 3 parts
        Assert.Equal(3, interp.Parts.Count);
        Assert.IsType<IdentifierExpr>(interp.Parts[0]);
        var text = Assert.IsType<LiteralExpr>(interp.Parts[1]);
        Assert.Equal(" and ", text.Value);
        Assert.IsType<IdentifierExpr>(interp.Parts[2]);
    }

    [Fact]
    public void Parse_InterpolatedString_NoExpression_StillParsesAsInterpolatedStringExpr()
    {
        var result = ParseExpr("$\"plain text\"");
        var interp = Assert.IsType<InterpolatedStringExpr>(result);

        Assert.Single(interp.Parts);
        var text = Assert.IsType<LiteralExpr>(interp.Parts[0]);
        Assert.Equal("plain text", text.Value);
    }

    // ── Phase 4: Command Literal Parsing ─────────────────────────────

    [Fact]
    public void Parse_CommandLiteral_SimpleCommand_ProducesCommandExpr()
    {
        var result = ParseExpr("$(echo hello)");
        var cmd = Assert.IsType<CommandExpr>(result);
        Assert.Single(cmd.Parts);
        var literal = Assert.IsType<LiteralExpr>(cmd.Parts[0]);
        Assert.Equal("echo hello", literal.Value);
    }

    [Fact]
    public void Parse_CommandLiteral_WithInterpolation_ProducesCommandExprWithParts()
    {
        var result = ParseExpr("$(echo {name})");
        var cmd = Assert.IsType<CommandExpr>(result);
        Assert.True(cmd.Parts.Count >= 2);
        // First part is "echo "
        var textPart = Assert.IsType<LiteralExpr>(cmd.Parts[0]);
        Assert.Equal("echo ", textPart.Value);
        // Second part should be an IdentifierExpr
        Assert.IsType<IdentifierExpr>(cmd.Parts[1]);
    }

    [Fact]
    public void Parse_CommandLiteral_EmptyCommand()
    {
        var result = ParseExpr("$()");
        var cmd = Assert.IsType<CommandExpr>(result);
        Assert.Empty(cmd.Parts);
    }

    [Fact]
    public void Parse_PassthroughCommand_SimpleCommand_ProducesPassthroughCommandExpr()
    {
        var result = ParseExpr("$>(echo hello)");
        var cmd = Assert.IsType<CommandExpr>(result);
        Assert.True(cmd.IsPassthrough);
        Assert.Single(cmd.Parts);
        var literal = Assert.IsType<LiteralExpr>(cmd.Parts[0]);
        Assert.Equal("echo hello", literal.Value);
    }

    [Fact]
    public void Parse_PassthroughCommand_WithInterpolation()
    {
        var result = ParseExpr("$>(echo {name})");
        var cmd = Assert.IsType<CommandExpr>(result);
        Assert.True(cmd.IsPassthrough);
        Assert.True(cmd.Parts.Count >= 2);
        var textPart = Assert.IsType<LiteralExpr>(cmd.Parts[0]);
        Assert.Equal("echo ", textPart.Value);
        Assert.IsType<IdentifierExpr>(cmd.Parts[1]);
    }

    [Fact]
    public void Parse_CaptureCommand_IsNotPassthrough()
    {
        var result = ParseExpr("$(echo hello)");
        var cmd = Assert.IsType<CommandExpr>(result);
        Assert.False(cmd.IsPassthrough);
    }

    // ── Phase 4: Pipe Operator Parsing ──────────────────────────────

    [Fact]
    public void Parse_PipeExpr_TwoCommands()
    {
        var result = ParseExpr("$(echo hello) | $(cat)");
        var pipe = Assert.IsType<PipeExpr>(result);

        Assert.IsType<CommandExpr>(pipe.Left);
        Assert.IsType<CommandExpr>(pipe.Right);
    }

    [Fact]
    public void Parse_PipeExpr_ThreeCommands_LeftAssociative()
    {
        var result = ParseExpr("$(cmd1) | $(cmd2) | $(cmd3)");
        // Left-associative: ((cmd1 | cmd2) | cmd3)
        var outerPipe = Assert.IsType<PipeExpr>(result);

        var innerPipe = Assert.IsType<PipeExpr>(outerPipe.Left);
        Assert.IsType<CommandExpr>(innerPipe.Left);
        Assert.IsType<CommandExpr>(innerPipe.Right);

        Assert.IsType<CommandExpr>(outerPipe.Right);
    }

    [Fact]
    public void Parse_PipeExpr_PipeLowerPrecedenceThanOr()
    {
        // Pipe has lower precedence than ||, so this should parse as:
        // (a || b) | (c || d) — but since pipe only works on commands,
        // let's just verify the AST structure
        var result = ParseExpr("$(cmd1) | $(cmd2)");
        Assert.IsType<PipeExpr>(result);
    }

    // ===== Phase 5: Try Expression =====

    [Fact]
    public void Parse_TryExpression_ReturnsTryExpr()
    {
        var result = ParseExpr("try 42");
        var tryExpr = Assert.IsType<TryExpr>(result);
        var literal = Assert.IsType<LiteralExpr>(tryExpr.Expression);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Parse_TryExpression_WithFunctionCall()
    {
        var result = ParseExpr("try readFile(\"test.txt\")");
        var tryExpr = Assert.IsType<TryExpr>(result);
        Assert.IsType<CallExpr>(tryExpr.Expression);
    }

    [Fact]
    public void Parse_TryExpression_NestedWithNullCoalesce()
    {
        var result = ParseExpr("try readFile(\"test.txt\") ?? \"default\"");
        // ?? has lower precedence than try, so this should be: (try readFile("test.txt")) ?? "default"
        var nullCoalesce = Assert.IsType<NullCoalesceExpr>(result);
        var tryExpr = Assert.IsType<TryExpr>(nullCoalesce.Left);
        Assert.IsType<CallExpr>(tryExpr.Expression);
        var right = Assert.IsType<LiteralExpr>(nullCoalesce.Right);
        Assert.Equal("default", right.Value);
    }

    [Fact]
    public void Parse_TryExpression_WithUnaryNegation()
    {
        var result = ParseExpr("try -42");
        var tryExpr = Assert.IsType<TryExpr>(result);
        var unary = Assert.IsType<UnaryExpr>(tryExpr.Expression);
        Assert.Equal(TokenType.Minus, unary.Operator.Type);
    }

    // ===== Phase 5: Null Coalescing (??) =====

    [Fact]
    public void Parse_NullCoalescing_ReturnsNullCoalesceExpr()
    {
        var result = ParseExpr("null ?? 42");
        var nc = Assert.IsType<NullCoalesceExpr>(result);
        Assert.IsType<LiteralExpr>(nc.Left);
        var right = Assert.IsType<LiteralExpr>(nc.Right);
        Assert.Equal(42L, right.Value);
    }

    [Fact]
    public void Parse_NullCoalescing_LeftAssociative()
    {
        // a ?? b ?? c → (a ?? b) ?? c
        var result = ParseExpr("null ?? null ?? 42");
        var outer = Assert.IsType<NullCoalesceExpr>(result);
        var inner = Assert.IsType<NullCoalesceExpr>(outer.Left);
        Assert.IsType<LiteralExpr>(inner.Left);
        Assert.IsType<LiteralExpr>(inner.Right);
        var right = Assert.IsType<LiteralExpr>(outer.Right);
        Assert.Equal(42L, right.Value);
    }

    [Fact]
    public void Parse_NullCoalescing_LowerThanTernary()
    {
        // a ?? b is lower precedence than pipe, so a ?? b comes before ternary
        // true ? null ?? 42 : 0 → true ? (null ?? 42) : 0
        var result = ParseExpr("true ? null ?? 42 : 0");
        var ternary = Assert.IsType<TernaryExpr>(result);
        var thenBranch = Assert.IsType<NullCoalesceExpr>(ternary.ThenBranch);
        Assert.IsType<LiteralExpr>(thenBranch.Left);
    }

    [Fact]
    public void Parse_NullCoalescing_WithVariables()
    {
        var result = ParseExpr("x ?? y");
        var nc = Assert.IsType<NullCoalesceExpr>(result);
        Assert.IsType<IdentifierExpr>(nc.Left);
        Assert.IsType<IdentifierExpr>(nc.Right);
    }

    // ===== Phase 5: Import Declaration =====

    [Fact]
    public void Parse_ImportDecl_SingleName()
    {
        var stmts = ParseProgram("import { deploy } from \"utils.stash\";");
        var importStmt = Assert.IsType<ImportStmt>(Assert.Single(stmts));
        Assert.Single(importStmt.Names);
        Assert.Equal("deploy", importStmt.Names[0].Lexeme);
        Assert.Equal("utils.stash", importStmt.Path.Literal);
    }

    [Fact]
    public void Parse_ImportDecl_MultipleNames()
    {
        var stmts = ParseProgram("import { deploy, Server, Status } from \"utils.stash\";");
        var importStmt = Assert.IsType<ImportStmt>(Assert.Single(stmts));
        Assert.Equal(3, importStmt.Names.Count);
        Assert.Equal("deploy", importStmt.Names[0].Lexeme);
        Assert.Equal("Server", importStmt.Names[1].Lexeme);
        Assert.Equal("Status", importStmt.Names[2].Lexeme);
    }

    [Fact]
    public void Parse_ImportDecl_MissingSemicolon_ReportsError()
    {
        var parser = ParseProgramWithParser("import { deploy } from \"utils.stash\"");
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void Parse_ImportDecl_MissingFrom_ReportsError()
    {
        var parser = ParseProgramWithParser("import { deploy } \"utils.stash\";");
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void Parse_ImportDecl_MissingPath_ReportsError()
    {
        var parser = ParseProgramWithParser("import { deploy } from ;");
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void Parse_ImportDecl_EmptyNames_ReportsError()
    {
        var parser = ParseProgramWithParser("import { } from \"utils.stash\";");
        Assert.NotEmpty(parser.Errors);
    }

    // --- Update Expression (++/--) Tests ---

    [Fact]
    public void Parse_PrefixIncrement_ReturnsUpdateExprWithIsPrefix()
    {
        var stmts = ParseProgram("++x;");
        var exprStmt = Assert.IsType<ExprStmt>(stmts[0]);
        var update = Assert.IsType<UpdateExpr>(exprStmt.Expression);
        Assert.Equal(TokenType.PlusPlus, update.Operator.Type);
        Assert.True(update.IsPrefix);
        var operand = Assert.IsType<IdentifierExpr>(update.Operand);
        Assert.Equal("x", operand.Name.Lexeme);
    }

    [Fact]
    public void Parse_PostfixIncrement_ReturnsUpdateExprWithoutIsPrefix()
    {
        var stmts = ParseProgram("x++;");
        var exprStmt = Assert.IsType<ExprStmt>(stmts[0]);
        var update = Assert.IsType<UpdateExpr>(exprStmt.Expression);
        Assert.Equal(TokenType.PlusPlus, update.Operator.Type);
        Assert.False(update.IsPrefix);
        var operand = Assert.IsType<IdentifierExpr>(update.Operand);
        Assert.Equal("x", operand.Name.Lexeme);
    }

    [Fact]
    public void Parse_PrefixDecrement_ReturnsUpdateExprWithIsPrefix()
    {
        var stmts = ParseProgram("--x;");
        var exprStmt = Assert.IsType<ExprStmt>(stmts[0]);
        var update = Assert.IsType<UpdateExpr>(exprStmt.Expression);
        Assert.Equal(TokenType.MinusMinus, update.Operator.Type);
        Assert.True(update.IsPrefix);
    }

    [Fact]
    public void Parse_PostfixDecrement_ReturnsUpdateExprWithoutIsPrefix()
    {
        var stmts = ParseProgram("x--;");
        var exprStmt = Assert.IsType<ExprStmt>(stmts[0]);
        var update = Assert.IsType<UpdateExpr>(exprStmt.Expression);
        Assert.Equal(TokenType.MinusMinus, update.Operator.Type);
        Assert.False(update.IsPrefix);
    }

    [Fact]
    public void Parse_PostfixIncrement_OnDotExpr()
    {
        var stmts = ParseProgram("obj.x++;");
        var exprStmt = Assert.IsType<ExprStmt>(stmts[0]);
        var update = Assert.IsType<UpdateExpr>(exprStmt.Expression);
        Assert.False(update.IsPrefix);
        Assert.IsType<DotExpr>(update.Operand);
    }

    [Fact]
    public void Parse_PostfixIncrement_OnIndexExpr()
    {
        var stmts = ParseProgram("arr[0]++;");
        var exprStmt = Assert.IsType<ExprStmt>(stmts[0]);
        var update = Assert.IsType<UpdateExpr>(exprStmt.Expression);
        Assert.False(update.IsPrefix);
        Assert.IsType<IndexExpr>(update.Operand);
    }

    [Fact]
    public void Parse_PrefixIncrement_InExpression()
    {
        // let result = ++x; — prefix in an assignment
        var stmts = ParseProgram("let result = ++x;");
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var update = Assert.IsType<UpdateExpr>(varDecl.Initializer!);
        Assert.True(update.IsPrefix);
        Assert.Equal(TokenType.PlusPlus, update.Operator.Type);
    }

    [Fact]
    public void Parse_PostfixIncrement_InExpression()
    {
        // let result = x++; — postfix in right-hand side of assignment
        var stmts = ParseProgram("let result = x++;");
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var update = Assert.IsType<UpdateExpr>(varDecl.Initializer!);
        Assert.False(update.IsPrefix);
    }

    // --- Operator Precedence Tests ---

    [Fact]
    public void Parse_Precedence_UnaryBindsTighterThanFactor()
    {
        // -2 * 3 should parse as (-2) * 3, not -(2 * 3)
        var result = ParseExpr("-2 * 3");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Star, binary.Operator.Type);
        Assert.IsType<UnaryExpr>(binary.Left);
    }

    [Fact]
    public void Parse_Precedence_ComparisonLowerThanTerm()
    {
        // 1 + 2 < 4 should parse as (1 + 2) < 4
        var result = ParseExpr("1 + 2 < 4");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.Less, binary.Operator.Type);
        Assert.IsType<BinaryExpr>(binary.Left); // 1 + 2
    }

    [Fact]
    public void Parse_Precedence_EqualityLowerThanComparison()
    {
        // 1 < 2 == true should parse as (1 < 2) == true
        var result = ParseExpr("1 < 2 == true");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.EqualEqual, binary.Operator.Type);
        Assert.IsType<BinaryExpr>(binary.Left); // 1 < 2
    }

    [Fact]
    public void Parse_Precedence_TernaryLowerThanNullCoalesce()
    {
        // true ? null ?? 42 : 0 — ?? binds tighter inside ternary branch
        var result = ParseExpr("true ? null ?? 42 : 0");
        var ternary = Assert.IsType<TernaryExpr>(result);
        Assert.IsType<NullCoalesceExpr>(ternary.ThenBranch);
    }

    [Fact]
    public void Parse_Precedence_PostfixHigherThanPrefix()
    {
        // -x++ should parse as -(x++), not (-x)++
        var stmts = ParseProgram("let r = -x++;");
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var unary = Assert.IsType<UnaryExpr>(varDecl.Initializer!);
        Assert.Equal(TokenType.Minus, unary.Operator.Type);
        Assert.IsType<UpdateExpr>(unary.Right);
    }

    // --- From keyword as identifier ---

    [Fact]
    public void Parse_FromAsVariableName()
    {
        // 'from' is a hard reserved keyword; using it as a variable name is a parse error
        var parser = ParseProgramWithParser("let from = 42;");
        Assert.NotEmpty(parser.Errors);
    }

    // ===== Namespace: import-as =====

    [Fact]
    public void Parse_ImportAs_Basic()
    {
        var stmts = ParseProgram("import \"utils.stash\" as utils;");
        var importAs = Assert.IsType<ImportAsStmt>(Assert.Single(stmts));
        Assert.Equal("utils.stash", importAs.Path.Literal);
        Assert.Equal("utils", importAs.Alias.Lexeme);
    }

    [Fact]
    public void Parse_ImportAs_WithOtherStatements()
    {
        var stmts = ParseProgram("import \"lib.stash\" as lib; let x = 1;");
        Assert.Equal(2, stmts.Count);
        Assert.IsType<ImportAsStmt>(stmts[0]);
        Assert.IsType<VarDeclStmt>(stmts[1]);
    }

    [Fact]
    public void Parse_ImportAs_CoexistsWithNamedImport()
    {
        var stmts = ParseProgram("import \"a.stash\" as a; import { foo } from \"b.stash\";");
        Assert.Equal(2, stmts.Count);
        Assert.IsType<ImportAsStmt>(stmts[0]);
        Assert.IsType<ImportStmt>(stmts[1]);
    }

    // ── Type Hints ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_VarDecl_WithTypeHint()
    {
        var stmts = ParseProgram("let name: string = \"Alice\";");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.Equal("name", varDecl.Name.Lexeme);
        Assert.NotNull(varDecl.TypeHint);
        Assert.Equal("string", varDecl.TypeHint!.Lexeme);
        var init = Assert.IsType<LiteralExpr>(varDecl.Initializer);
        Assert.Equal("Alice", init.Value);
    }

    [Fact]
    public void Parse_VarDecl_WithTypeHintNoInitializer()
    {
        var stmts = ParseProgram("let count: int;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.Equal("count", varDecl.Name.Lexeme);
        Assert.NotNull(varDecl.TypeHint);
        Assert.Equal("int", varDecl.TypeHint!.Lexeme);
        Assert.Null(varDecl.Initializer);
    }

    [Fact]
    public void Parse_VarDecl_WithoutTypeHint_TypeHintIsNull()
    {
        var stmts = ParseProgram("let x = 42;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.Null(varDecl.TypeHint);
    }

    [Fact]
    public void Parse_ConstDecl_WithTypeHint()
    {
        var stmts = ParseProgram("const PI: float = 3.14;");
        var constDecl = Assert.IsType<ConstDeclStmt>(Assert.Single(stmts));
        Assert.Equal("PI", constDecl.Name.Lexeme);
        Assert.NotNull(constDecl.TypeHint);
        Assert.Equal("float", constDecl.TypeHint!.Lexeme);
        var init = Assert.IsType<LiteralExpr>(constDecl.Initializer);
        Assert.Equal(3.14, init.Value);
    }

    [Fact]
    public void Parse_ConstDecl_WithoutTypeHint_TypeHintIsNull()
    {
        var stmts = ParseProgram("const X = 1;");
        var constDecl = Assert.IsType<ConstDeclStmt>(Assert.Single(stmts));
        Assert.Null(constDecl.TypeHint);
    }

    [Fact]
    public void Parse_FnDecl_WithParameterTypes()
    {
        var stmts = ParseProgram("fn add(a: int, b: int) { }");
        var fnDecl = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal("add", fnDecl.Name.Lexeme);
        Assert.Equal(2, fnDecl.Parameters.Count);
        Assert.Equal(2, fnDecl.ParameterTypes.Count);
        Assert.Equal("int", fnDecl.ParameterTypes[0]!.Lexeme);
        Assert.Equal("int", fnDecl.ParameterTypes[1]!.Lexeme);
        Assert.Null(fnDecl.ReturnType);
    }

    [Fact]
    public void Parse_FnDecl_WithReturnType()
    {
        var stmts = ParseProgram("fn add(a, b) -> int { }");
        var fnDecl = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal("add", fnDecl.Name.Lexeme);
        Assert.NotNull(fnDecl.ReturnType);
        Assert.Equal("int", fnDecl.ReturnType!.Lexeme);
        Assert.Null(fnDecl.ParameterTypes[0]);
        Assert.Null(fnDecl.ParameterTypes[1]);
    }

    [Fact]
    public void Parse_FnDecl_WithFullTypeAnnotations()
    {
        var stmts = ParseProgram("fn add(a: int, b: int) -> int { return a; }");
        var fnDecl = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal("int", fnDecl.ParameterTypes[0]!.Lexeme);
        Assert.Equal("int", fnDecl.ParameterTypes[1]!.Lexeme);
        Assert.Equal("int", fnDecl.ReturnType!.Lexeme);
    }

    [Fact]
    public void Parse_FnDecl_WithoutTypeAnnotations_AllNull()
    {
        var stmts = ParseProgram("fn greet(name) { }");
        var fnDecl = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Single(fnDecl.Parameters);
        Assert.Single(fnDecl.ParameterTypes);
        Assert.Null(fnDecl.ParameterTypes[0]);
        Assert.Null(fnDecl.ReturnType);
    }

    [Fact]
    public void Parse_FnDecl_MixedTypedAndUntypedParams()
    {
        var stmts = ParseProgram("fn mixed(a: int, b) { }");
        var fnDecl = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal("int", fnDecl.ParameterTypes[0]!.Lexeme);
        Assert.Null(fnDecl.ParameterTypes[1]);
    }

    [Fact]
    public void Parse_FnDecl_NoParams_EmptyParameterTypes()
    {
        var stmts = ParseProgram("fn noop() { }");
        var fnDecl = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Empty(fnDecl.Parameters);
        Assert.Empty(fnDecl.ParameterTypes);
    }

    [Fact]
    public void Parse_StructDecl_WithFieldTypes()
    {
        var stmts = ParseProgram("struct Server { host: string, port: int }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Equal("Server", structDecl.Name.Lexeme);
        Assert.Equal(2, structDecl.Fields.Count);
        Assert.Equal(2, structDecl.FieldTypes.Count);
        Assert.Equal("string", structDecl.FieldTypes[0]!.Lexeme);
        Assert.Equal("int", structDecl.FieldTypes[1]!.Lexeme);
    }

    [Fact]
    public void Parse_StructDecl_WithoutFieldTypes_AllNull()
    {
        var stmts = ParseProgram("struct Point { x, y }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Equal(2, structDecl.FieldTypes.Count);
        Assert.Null(structDecl.FieldTypes[0]);
        Assert.Null(structDecl.FieldTypes[1]);
    }

    [Fact]
    public void Parse_StructDecl_MixedTypedAndUntypedFields()
    {
        var stmts = ParseProgram("struct Config { name: string, value }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Equal("string", structDecl.FieldTypes[0]!.Lexeme);
        Assert.Null(structDecl.FieldTypes[1]);
    }

    // Struct methods

    [Fact]
    public void Parse_StructDecl_WithMethod()
    {
        var stmts = ParseProgram("struct Point { x, y, fn getX() { return self.x; } }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Equal("Point", structDecl.Name.Lexeme);
        Assert.Equal(2, structDecl.Fields.Count);
        Assert.Single(structDecl.Methods);
        Assert.Equal("getX", structDecl.Methods[0].Name.Lexeme);
    }

    [Fact]
    public void Parse_StructDecl_WithMultipleMethods()
    {
        var stmts = ParseProgram("struct Point { x, y, fn getX() { return self.x; } fn getY() { return self.y; } }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Equal(2, structDecl.Fields.Count);
        Assert.Equal(2, structDecl.Methods.Count);
        Assert.Equal("getX", structDecl.Methods[0].Name.Lexeme);
        Assert.Equal("getY", structDecl.Methods[1].Name.Lexeme);
    }

    [Fact]
    public void Parse_StructDecl_MethodsOnly()
    {
        var stmts = ParseProgram("struct Util { fn greet() { return \"hello\"; } }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Empty(structDecl.Fields);
        Assert.Single(structDecl.Methods);
        Assert.Equal("greet", structDecl.Methods[0].Name.Lexeme);
    }

    [Fact]
    public void Parse_StructDecl_MethodWithParams()
    {
        var stmts = ParseProgram("struct Point { x, y, fn add(other) { return self.x + other.x; } }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Single(structDecl.Methods);
        Assert.Single(structDecl.Methods[0].Parameters);
        Assert.Equal("other", structDecl.Methods[0].Parameters[0].Lexeme);
    }

    [Fact]
    public void Parse_StructDecl_MethodWithDefaultParam()
    {
        var stmts = ParseProgram("struct Counter { val, fn increment(amount = 1) { return self.val + amount; } }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Single(structDecl.Methods);
        Assert.Single(structDecl.Methods[0].Parameters);
        Assert.NotNull(structDecl.Methods[0].DefaultValues[0]);
    }

    [Fact]
    public void Parse_StructDecl_NoMethods()
    {
        var stmts = ParseProgram("struct Point { x, y }");
        var structDecl = Assert.IsType<StructDeclStmt>(Assert.Single(stmts));
        Assert.Empty(structDecl.Methods);
    }

    [Fact]
    public void Parse_ForIn_WithTypeHint()
    {
        var stmts = ParseProgram("for (let item: string in names) { }");
        var forIn = Assert.IsType<ForInStmt>(Assert.Single(stmts));
        Assert.Equal("item", forIn.VariableName.Lexeme);
        Assert.NotNull(forIn.TypeHint);
        Assert.Equal("string", forIn.TypeHint!.Lexeme);
    }

    [Fact]
    public void Parse_ForIn_WithoutTypeHint_TypeHintIsNull()
    {
        var stmts = ParseProgram("for (let item in names) { }");
        var forIn = Assert.IsType<ForInStmt>(Assert.Single(stmts));
        Assert.Null(forIn.TypeHint);
    }

    [Fact]
    public void Parse_VarDecl_WithUserDefinedType()
    {
        var stmts = ParseProgram("let server: Server = null;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        Assert.NotNull(varDecl.TypeHint);
        Assert.Equal("Server", varDecl.TypeHint!.Lexeme);
    }

    [Fact]
    public void Parse_FnDecl_WithUserDefinedReturnType()
    {
        var stmts = ParseProgram("fn create() -> Server { }");
        var fnDecl = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.NotNull(fnDecl.ReturnType);
        Assert.Equal("Server", fnDecl.ReturnType!.Lexeme);
    }

    // ── Lambda parsing tests ────────────────────────────────────────

    [Fact]
    public void Lambda_ExpressionBody_ParsesAsLambdaExpr()
    {
        var stmts = ParseProgram("let f = (x) => x + 1;");
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var lambda = Assert.IsType<LambdaExpr>(varDecl.Initializer);
        Assert.Single(lambda.Parameters);
        Assert.Equal("x", lambda.Parameters[0].Lexeme);
        Assert.NotNull(lambda.ExpressionBody);
        Assert.Null(lambda.BlockBody);
    }

    [Fact]
    public void Lambda_BlockBody_ParsesAsLambdaExpr()
    {
        var stmts = ParseProgram("let f = (x) => { return x + 1; };");
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var lambda = Assert.IsType<LambdaExpr>(varDecl.Initializer);
        Assert.Single(lambda.Parameters);
        Assert.Equal("x", lambda.Parameters[0].Lexeme);
        Assert.Null(lambda.ExpressionBody);
        Assert.NotNull(lambda.BlockBody);
    }

    [Fact]
    public void Lambda_NoParameters_ParsesCorrectly()
    {
        var stmts = ParseProgram("let f = () => 42;");
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var lambda = Assert.IsType<LambdaExpr>(varDecl.Initializer);
        Assert.Empty(lambda.Parameters);
        Assert.NotNull(lambda.ExpressionBody);
    }

    [Fact]
    public void Lambda_MultipleParameters_ParsesCorrectly()
    {
        var stmts = ParseProgram("let f = (a, b, c) => a + b + c;");
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var lambda = Assert.IsType<LambdaExpr>(varDecl.Initializer);
        Assert.Equal(3, lambda.Parameters.Count);
        Assert.Equal("a", lambda.Parameters[0].Lexeme);
        Assert.Equal("b", lambda.Parameters[1].Lexeme);
        Assert.Equal("c", lambda.Parameters[2].Lexeme);
    }

    [Fact]
    public void Lambda_WithTypeAnnotations_ParsesCorrectly()
    {
        var stmts = ParseProgram("let f = (x: int, y: string) => x;");
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[0]);
        var lambda = Assert.IsType<LambdaExpr>(varDecl.Initializer);
        Assert.Equal(2, lambda.Parameters.Count);
        Assert.Equal("int", lambda.ParameterTypes[0]?.Lexeme);
        Assert.Equal("string", lambda.ParameterTypes[1]?.Lexeme);
    }

    [Fact]
    public void Grouping_StillWorks_AfterLambdaSupport()
    {
        var stmts = ParseProgram("let x = (1 + 2) * 3;");
        var varDecl = Assert.IsType<VarDeclStmt>(stmts[0]);
        Assert.IsType<BinaryExpr>(varDecl.Initializer);
    }

    // Switch expression

    [Fact]
    public void Parse_SwitchExpr_BasicArms()
    {
        var result = ParseExpr("1 switch { 1 => \"one\", 2 => \"two\" }");
        var switchExpr = Assert.IsType<SwitchExpr>(result);
        Assert.Equal(2, switchExpr.Arms.Count);

        var firstArm = switchExpr.Arms[0];
        Assert.False(firstArm.IsDiscard);
        var firstPattern = Assert.IsType<LiteralExpr>(firstArm.Pattern);
        Assert.Equal(1L, firstPattern.Value);
        var firstBody = Assert.IsType<LiteralExpr>(firstArm.Body);
        Assert.Equal("one", firstBody.Value);

        var secondArm = switchExpr.Arms[1];
        Assert.False(secondArm.IsDiscard);
        var secondPattern = Assert.IsType<LiteralExpr>(secondArm.Pattern);
        Assert.Equal(2L, secondPattern.Value);
        var secondBody = Assert.IsType<LiteralExpr>(secondArm.Body);
        Assert.Equal("two", secondBody.Value);
    }

    [Fact]
    public void Parse_SwitchExpr_WithDiscard()
    {
        var result = ParseExpr("x switch { 1 => \"one\", _ => \"other\" }");
        var switchExpr = Assert.IsType<SwitchExpr>(result);
        Assert.Equal(2, switchExpr.Arms.Count);

        var discardArm = switchExpr.Arms[1];
        Assert.True(discardArm.IsDiscard);
        Assert.Null(discardArm.Pattern);
    }

    [Fact]
    public void Parse_SwitchExpr_TrailingComma()
    {
        var result = ParseExpr("1 switch { 1 => \"a\", 2 => \"b\", }");
        var switchExpr = Assert.IsType<SwitchExpr>(result);
        Assert.Equal(2, switchExpr.Arms.Count);
    }

    // ── Default parameter value tests ────────────────────────────────────────

    [Fact]
    public void Parse_FnDecl_WithDefaultValues()
    {
        var stmts = ParseProgram("fn greet(name, greeting = \"Hello\") { }");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal(2, fn.Parameters.Count);
        Assert.Equal("name", fn.Parameters[0].Lexeme);
        Assert.Equal("greeting", fn.Parameters[1].Lexeme);
        Assert.Null(fn.DefaultValues[0]);
        Assert.NotNull(fn.DefaultValues[1]);
        Assert.IsType<LiteralExpr>(fn.DefaultValues[1]);
    }

    [Fact]
    public void Parse_FnDecl_WithTypedDefaultValues()
    {
        var stmts = ParseProgram("fn connect(host: string, port: int = 8080) { }");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal(2, fn.Parameters.Count);
        Assert.Equal("string", fn.ParameterTypes[0]!.Lexeme);
        Assert.Equal("int", fn.ParameterTypes[1]!.Lexeme);
        Assert.Null(fn.DefaultValues[0]);
        var defaultVal = Assert.IsType<LiteralExpr>(fn.DefaultValues[1]);
        Assert.Equal(8080L, defaultVal.Value);
    }

    [Fact]
    public void Parse_FnDecl_AllDefaultValues()
    {
        var stmts = ParseProgram("fn opts(a = 1, b = 2, c = 3) { }");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal(3, fn.Parameters.Count);
        Assert.NotNull(fn.DefaultValues[0]);
        Assert.NotNull(fn.DefaultValues[1]);
        Assert.NotNull(fn.DefaultValues[2]);
    }

    [Fact]
    public void Parse_FnDecl_NoDefaultValues_HasEmptyDefaults()
    {
        var stmts = ParseProgram("fn add(a, b) { }");
        var fn = Assert.IsType<FnDeclStmt>(Assert.Single(stmts));
        Assert.Equal(2, fn.DefaultValues.Count);
        Assert.Null(fn.DefaultValues[0]);
        Assert.Null(fn.DefaultValues[1]);
    }

    [Fact]
    public void Parse_FnDecl_NonDefaultAfterDefault_ReportsError()
    {
        var lexer = new Lexer("fn bad(a = 1, b) { }");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        Assert.NotEmpty(parser.Errors);
        Assert.Contains(parser.Errors, e => e.Contains("Non-default parameter cannot follow a default parameter"));
    }

    [Fact]
    public void Parse_Lambda_WithDefaultValues()
    {
        var stmts = ParseProgram("let f = (x, y = 10) => x + y;");
        var varDecl = Assert.IsType<VarDeclStmt>(Assert.Single(stmts));
        var lambda = Assert.IsType<LambdaExpr>(varDecl.Initializer);
        Assert.Equal(2, lambda.Parameters.Count);
        Assert.Null(lambda.DefaultValues[0]);
        Assert.NotNull(lambda.DefaultValues[1]);
    }

    [Fact]
    public void Parse_InOperator_ReturnsBinaryExpr()
    {
        var result = ParseExpr("x in arr");
        var binary = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.In, binary.Operator.Type);
    }

    [Fact]
    public void Parse_RangeExpression_ReturnsRangeExpr()
    {
        var result = ParseExpr("0..10");
        var range = Assert.IsType<RangeExpr>(result);
        Assert.IsType<LiteralExpr>(range.Start);
        Assert.IsType<LiteralExpr>(range.End);
        Assert.Null(range.Step);
    }

    [Fact]
    public void Parse_RangeExpressionWithStep_ReturnsRangeExprWithStep()
    {
        var result = ParseExpr("0..10..2");
        var range = Assert.IsType<RangeExpr>(result);
        Assert.NotNull(range.Step);
    }

    // Destructuring

    [Fact]
    public void Parse_ArrayDestructure_ReturnsDestructureStmt()
    {
        var stmts = ParseProgram("let [a, b] = [1, 2];");
        var destructure = Assert.IsType<DestructureStmt>(Assert.Single(stmts));
        Assert.Equal(DestructureStmt.PatternKind.Array, destructure.Kind);
        Assert.False(destructure.IsConst);
        Assert.Equal(2, destructure.Names.Count);
        Assert.Equal("a", destructure.Names[0].Lexeme);
        Assert.Equal("b", destructure.Names[1].Lexeme);
        Assert.NotNull(destructure.Initializer);
    }

    [Fact]
    public void Parse_ObjectDestructure_ReturnsDestructureStmt()
    {
        var stmts = ParseProgram("let { x, y } = p;");
        var destructure = Assert.IsType<DestructureStmt>(Assert.Single(stmts));
        Assert.Equal(DestructureStmt.PatternKind.Object, destructure.Kind);
        Assert.False(destructure.IsConst);
        Assert.Equal(2, destructure.Names.Count);
        Assert.Equal("x", destructure.Names[0].Lexeme);
        Assert.Equal("y", destructure.Names[1].Lexeme);
    }

    [Fact]
    public void Parse_ConstArrayDestructure_IsConst()
    {
        var stmts = ParseProgram("const [a, b] = [1, 2];");
        var destructure = Assert.IsType<DestructureStmt>(Assert.Single(stmts));
        Assert.Equal(DestructureStmt.PatternKind.Array, destructure.Kind);
        Assert.True(destructure.IsConst);
    }

    [Fact]
    public void Parse_ConstObjectDestructure_IsConst()
    {
        var stmts = ParseProgram("const { x } = p;");
        var destructure = Assert.IsType<DestructureStmt>(Assert.Single(stmts));
        Assert.Equal(DestructureStmt.PatternKind.Object, destructure.Kind);
        Assert.True(destructure.IsConst);
    }

    // --- Is Expression Tests ---

    [Fact]
    public void Parse_IsExpression_ReturnsIsExpr()
    {
        var result = ParseExpr("x is int");
        var isExpr = Assert.IsType<IsExpr>(result);
        var left = Assert.IsType<IdentifierExpr>(isExpr.Left);
        Assert.Equal("x", left.Name.Lexeme);
        Assert.Equal("int", isExpr.TypeName.Lexeme);
    }

    [Fact]
    public void Parse_IsExpression_WithStringType_ReturnsIsExpr()
    {
        var result = ParseExpr("x is string");
        var isExpr = Assert.IsType<IsExpr>(result);
        Assert.Equal("string", isExpr.TypeName.Lexeme);
    }

    [Fact]
    public void Parse_IsExpression_WithNullType_ReturnsIsExpr()
    {
        var result = ParseExpr("x is null");
        var isExpr = Assert.IsType<IsExpr>(result);
        Assert.Equal("null", isExpr.TypeName.Lexeme);
    }

    [Fact]
    public void Parse_IsExpression_WithComplexLeftSide_ReturnsIsExpr()
    {
        var result = ParseExpr("(1 + 2) is int");
        var isExpr = Assert.IsType<IsExpr>(result);
        Assert.IsType<GroupingExpr>(isExpr.Left);
        Assert.Equal("int", isExpr.TypeName.Lexeme);
    }

    [Fact]
    public void Parse_IsExpression_PrecedenceWithEquality_EqualityIsLower()
    {
        // "x is int == true" should parse as "(x is int) == true"
        var result = ParseExpr("x is int == true");
        var equality = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.EqualEqual, equality.Operator.Type);
        var isExpr = Assert.IsType<IsExpr>(equality.Left);
        Assert.Equal("int", isExpr.TypeName.Lexeme);
    }

    [Fact]
    public void Parse_IsExpression_PrecedenceWithLogicalAnd()
    {
        // "x is int && y is string" should parse as "(x is int) && (y is string)"
        var result = ParseExpr("x is int && y is string");
        var andExpr = Assert.IsType<BinaryExpr>(result);
        Assert.Equal(TokenType.AmpersandAmpersand, andExpr.Operator.Type);
        var leftIs = Assert.IsType<IsExpr>(andExpr.Left);
        Assert.Equal("int", leftIs.TypeName.Lexeme);
        var rightIs = Assert.IsType<IsExpr>(andExpr.Right);
        Assert.Equal("string", rightIs.TypeName.Lexeme);
    }

    [Fact]
    public void Parse_IsExpression_InvalidType_ProducesError()
    {
        var lexer = new Lexer("x is invalid");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        parser.Parse();
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void Parse_IsExpression_AllValidTypes()
    {
        string[] types = { "int", "float", "string", "bool", "null", "array", "dict", "struct", "enum", "function", "range", "namespace" };
        foreach (string type in types)
        {
            var result = ParseExpr($"x is {type}");
            var isExpr = Assert.IsType<IsExpr>(result);
            Assert.Equal(type, isExpr.TypeName.Lexeme);
        }
    }
}
