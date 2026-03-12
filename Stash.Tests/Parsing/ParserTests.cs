using System.Collections.Generic;
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
}
