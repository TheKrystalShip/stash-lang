namespace Stash.Tests.Analysis;

using System.Collections.Generic;
using Stash.Analysis.Cli;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Xunit;

/// <summary>
/// Tests for the P10 strict literal-ness predicate in <see cref="LiteralSchemaBuilder.IsLiteralExpr"/>.
/// </summary>
public class LiteralSchemaPredicateTests
{
    // ── Helper: parse a single expression ───────────────────────────────────

    private static Expr ParseExpr(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    private static bool IsLiteral(string exprSource)
        => LiteralSchemaBuilder.IsLiteralExpr(ParseExpr(exprSource));

    // ── Scalar literals ──────────────────────────────────────────────────────

    [Fact]
    public void IsLiteralExpr_IntegerLiteral_ReturnsTrue()
        => Assert.True(IsLiteral("42"));

    [Fact]
    public void IsLiteralExpr_FloatLiteral_ReturnsTrue()
        => Assert.True(IsLiteral("3.14"));

    [Fact]
    public void IsLiteralExpr_StringLiteral_ReturnsTrue()
        => Assert.True(IsLiteral("\"hello\""));

    [Fact]
    public void IsLiteralExpr_BoolTrueLiteral_ReturnsTrue()
        => Assert.True(IsLiteral("true"));

    [Fact]
    public void IsLiteralExpr_BoolFalseLiteral_ReturnsTrue()
        => Assert.True(IsLiteral("false"));

    [Fact]
    public void IsLiteralExpr_NullLiteral_ReturnsTrue()
        => Assert.True(IsLiteral("null"));

    // ── Domain-specific literals ─────────────────────────────────────────────

    [Fact]
    public void IsLiteralExpr_DurationLiteral_ReturnsTrue()
        => Assert.True(IsLiteral("30s"));

    [Fact]
    public void IsLiteralExpr_ByteSizeLiteral_ReturnsTrue()
        => Assert.True(IsLiteral("100MB"));

    // ── Negative number literals (UnaryExpr(Minus, NumericLiteral)) ──────────

    [Fact]
    public void IsLiteralExpr_UnaryMinusInteger_ReturnsTrue()
        => Assert.True(IsLiteral("-1"));

    [Fact]
    public void IsLiteralExpr_UnaryMinusFloat_ReturnsTrue()
        => Assert.True(IsLiteral("-1.5"));

    // ── Non-literal unary forms ──────────────────────────────────────────────

    [Fact]
    public void IsLiteralExpr_BangTrue_ReturnsFalse()
        => Assert.False(IsLiteral("!true"));

    [Fact]
    public void IsLiteralExpr_UnaryMinusIdentifier_ReturnsFalse()
        => Assert.False(IsLiteral("-x"));

    [Fact]
    public void IsLiteralExpr_DoubleUnaryMinus_ReturnsFalse()
        => Assert.False(IsLiteral("--1"));

    // ── Array literals ───────────────────────────────────────────────────────

    [Fact]
    public void IsLiteralExpr_EmptyArray_ReturnsTrue()
        => Assert.True(IsLiteral("[]"));

    [Fact]
    public void IsLiteralExpr_ArrayOfLiterals_ReturnsTrue()
        => Assert.True(IsLiteral("[1, 2, 3]"));

    [Fact]
    public void IsLiteralExpr_ArrayWithNonLiteralElement_ReturnsFalse()
        => Assert.False(IsLiteral("[1, x, 3]"));

    [Fact]
    public void IsLiteralExpr_NestedArrayLiterals_ReturnsTrue()
        => Assert.True(IsLiteral("[[1, 2], [3, 4]]"));

    // ── Dict literals ────────────────────────────────────────────────────────

    [Fact]
    public void IsLiteralExpr_EmptyDict_ReturnsTrue()
        => Assert.True(IsLiteral("{}"));

    [Fact]
    public void IsLiteralExpr_DictOfLiteralValues_ReturnsTrue()
        => Assert.True(IsLiteral("{ a: 1, b: \"two\", c: true }"));

    [Fact]
    public void IsLiteralExpr_DictWithNegativeDefault_ReturnsTrue()
        => Assert.True(IsLiteral("{ default: -1 }"));

    [Fact]
    public void IsLiteralExpr_DictWithNegativeFloatDefault_ReturnsTrue()
        => Assert.True(IsLiteral("{ default: -1.5 }"));

    [Fact]
    public void IsLiteralExpr_DictWithNonLiteralValue_ReturnsFalse()
        => Assert.False(IsLiteral("{ a: someVar }"));

    [Fact]
    public void IsLiteralExpr_DictWithBangTrueValue_ReturnsFalse()
        => Assert.False(IsLiteral("{ a: !true }"));

    // ── Identifiers and expressions ──────────────────────────────────────────

    [Fact]
    public void IsLiteralExpr_Identifier_ReturnsFalse()
        => Assert.False(IsLiteral("someVar"));

    [Fact]
    public void IsLiteralExpr_BinaryExpression_ReturnsFalse()
        => Assert.False(IsLiteral("1 + 2"));

    [Fact]
    public void IsLiteralExpr_CallExpression_ReturnsFalse()
        => Assert.False(IsLiteral("foo()"));

    // ── TryBuild integration ─────────────────────────────────────────────────

    [Fact]
    public void TryBuild_SimpleSchema_ReturnsTrueAndNotNull()
    {
        string source = """
            let schema = cli.schema({
                retries: cli.option("int", { default: 3 })
            });
            """;
        bool result = LiteralSchemaBuilder.TryBuild(source, "<test>", out var schema);
        Assert.True(result, "TryBuild should return true for a simple literal schema");
        Assert.NotNull(schema);
    }
}
