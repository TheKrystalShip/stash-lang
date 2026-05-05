using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

namespace Stash.Tests.Parsing;

public class StreamingCommandParserTests
{
    private static Expr ParseExpr(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    private static (Expr expr, Parser parser) ParseExprWithErrors(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return (parser.Parse(), parser);
    }

    [Fact]
    public void Parse_StreamingCommand_ProducesCommandExprWithStreamMode()
    {
        var result = ParseExpr("$<(echo hi)");
        var cmd = Assert.IsType<CommandExpr>(result);
        Assert.Equal(CommandMode.Stream, cmd.Mode);
        Assert.False(cmd.IsStrict);
        Assert.False(cmd.IsPassthrough);
        Assert.Single(cmd.Parts);
        var literal = Assert.IsType<LiteralExpr>(cmd.Parts[0]);
        Assert.Equal("echo hi", literal.Value);
    }

    [Fact]
    public void Parse_StrictStreamingCommand_ProducesStrictStreamMode()
    {
        var result = ParseExpr("$!<(echo hi)");
        var cmd = Assert.IsType<CommandExpr>(result);
        Assert.Equal(CommandMode.Stream, cmd.Mode);
        Assert.True(cmd.IsStrict);
        Assert.False(cmd.IsPassthrough);
    }

    [Fact]
    public void Parse_DollarLessGreater_IsParseError()
    {
        // $<>(...) lexes as Dollar Less Greater LeftParen ... — not a valid expression.
        var (_, parser) = ParseExprWithErrors("$<>(echo hi)");
        Assert.NotEmpty(parser.Errors);
    }
}
