using Stash.Lexing;

namespace Stash.Tests.Lexing;

public class StreamingCommandLiteralTests
{
    private static List<Token> Scan(string source) => new Lexer(source).ScanTokens();

    [Fact]
    public void ScanTokens_StreamingCommandLiteral_Simple()
    {
        var tokens = Scan("$<(echo hi)");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.StreamingCommandLiteral, tokens[0].Type);
        Assert.Equal("$<(echo hi)", tokens[0].Lexeme);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        Assert.Equal("echo hi", parts[0]);
    }

    [Fact]
    public void ScanTokens_StrictStreamingCommandLiteral_Simple()
    {
        var tokens = Scan("$!<(echo hi)");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.StrictStreamingCommandLiteral, tokens[0].Type);
        Assert.Equal("$!<(echo hi)", tokens[0].Lexeme);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        Assert.Equal("echo hi", parts[0]);
    }

    [Fact]
    public void ScanTokens_StreamingCommandLiteral_WithInterpolation()
    {
        var tokens = Scan("$<(echo ${name})");

        Assert.Equal(TokenType.StreamingCommandLiteral, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Equal(2, parts.Count);
        Assert.Equal("echo ", parts[0]);
        var exprTokens = Assert.IsType<List<Token>>(parts[1]);
        Assert.Equal(TokenType.Identifier, exprTokens[0].Type);
        Assert.Equal("name", exprTokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_StreamingCommandLiteral_NestedParens()
    {
        var tokens = Scan("$<(echo (foo))");

        // Single token, depth tracking keeps inner () in the text.
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.StreamingCommandLiteral, tokens[0].Type);
        Assert.Equal("$<(echo (foo))", tokens[0].Lexeme);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        Assert.Equal("echo (foo)", parts[0]);
    }

    [Fact]
    public void ScanTokens_DollarLessAngle_NoParen_LexedSeparately()
    {
        // "$ <" with a space — the $ and < are unrelated tokens.
        var spaced = Scan("$ <");
        Assert.Equal(TokenType.Dollar, spaced[0].Type);
        Assert.Equal(TokenType.Less, spaced[1].Type);

        // "$<x" — $ followed by < but no '(' means we don't form a streaming literal.
        var noParen = Scan("$<x");
        Assert.Equal(TokenType.Dollar, noParen[0].Type);
        Assert.Equal(TokenType.Less, noParen[1].Type);
        Assert.Equal(TokenType.Identifier, noParen[2].Type);
    }

    [Fact]
    public void ScanTokens_StreamingCommandLiteral_WithPipes()
    {
        var tokens = Scan("$<(cat foo | grep bar)");

        // Should produce: StreamingCommandLiteral, Pipe, StreamingCommandLiteral, Eof
        Assert.Equal(4, tokens.Count);
        Assert.Equal(TokenType.StreamingCommandLiteral, tokens[0].Type);
        Assert.Equal(TokenType.Pipe, tokens[1].Type);
        Assert.Equal(TokenType.StreamingCommandLiteral, tokens[2].Type);
        Assert.Equal(TokenType.Eof, tokens[3].Type);
    }
}
