using Stash.Lexing;

namespace Stash.Tests.Lexing;

public class LexerTests
{
    private static List<Token> Scan(string source) => new Lexer(source).ScanTokens();

    private static Lexer CreateLexer(string source) => new Lexer(source);

    // ── 1. Single-character tokens ──────────────────────────────────────

    [Theory]
    [InlineData("(", TokenType.LeftParen)]
    [InlineData(")", TokenType.RightParen)]
    [InlineData("{", TokenType.LeftBrace)]
    [InlineData("}", TokenType.RightBrace)]
    [InlineData("[", TokenType.LeftBracket)]
    [InlineData("]", TokenType.RightBracket)]
    [InlineData(",", TokenType.Comma)]
    [InlineData(".", TokenType.Dot)]
    [InlineData(";", TokenType.Semicolon)]
    [InlineData("+", TokenType.Plus)]
    [InlineData("-", TokenType.Minus)]
    [InlineData("*", TokenType.Star)]
    [InlineData("/", TokenType.Slash)]
    [InlineData("%", TokenType.Percent)]
    [InlineData("!", TokenType.Bang)]
    [InlineData("<", TokenType.Less)]
    [InlineData(">", TokenType.Greater)]
    [InlineData(":", TokenType.Colon)]
    [InlineData("?", TokenType.QuestionMark)]
    [InlineData("|", TokenType.Pipe)]
    [InlineData("$", TokenType.Dollar)]
    public void ScanTokens_SingleCharToken_ProducesCorrectType(string source, TokenType expected)
    {
        var tokens = Scan(source);

        Assert.Equal(2, tokens.Count);
        Assert.Equal(expected, tokens[0].Type);
        Assert.Equal(source, tokens[0].Lexeme);
        Assert.Equal(TokenType.Eof, tokens[1].Type);
    }

    // ── 2. Two-character tokens ─────────────────────────────────────────

    [Theory]
    [InlineData("==", TokenType.EqualEqual)]
    [InlineData("!=", TokenType.BangEqual)]
    [InlineData("<=", TokenType.LessEqual)]
    [InlineData(">=", TokenType.GreaterEqual)]
    [InlineData("&&", TokenType.AmpersandAmpersand)]
    [InlineData("||", TokenType.PipePipe)]
    [InlineData("??", TokenType.QuestionQuestion)]
    [InlineData("++", TokenType.PlusPlus)]
    [InlineData("--", TokenType.MinusMinus)]
    [InlineData("=", TokenType.Equal)]
    [InlineData("=>", TokenType.FatArrow)]
    [InlineData("+=", TokenType.PlusEqual)]
    [InlineData("-=", TokenType.MinusEqual)]
    [InlineData("*=", TokenType.StarEqual)]
    [InlineData("/=", TokenType.SlashEqual)]
    [InlineData("%=", TokenType.PercentEqual)]
    [InlineData("??=", TokenType.QuestionQuestionEqual)]
    public void ScanTokens_TwoCharOrAssignToken_ProducesCorrectType(string source, TokenType expected)
    {
        var tokens = Scan(source);

        Assert.Equal(2, tokens.Count);
        Assert.Equal(expected, tokens[0].Type);
        Assert.Equal(source, tokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_BangAlone_ProducesBang()
    {
        var tokens = Scan("!");
        Assert.Equal(TokenType.Bang, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_BangEqual_ProducesBangEqual()
    {
        var tokens = Scan("!=");
        Assert.Equal(TokenType.BangEqual, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_EqualAlone_ProducesEqual()
    {
        var tokens = Scan("=");
        Assert.Equal(TokenType.Equal, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_EqualEqual_ProducesEqualEqual()
    {
        var tokens = Scan("==");
        Assert.Equal(TokenType.EqualEqual, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_LessAlone_ProducesLess()
    {
        var tokens = Scan("<");
        Assert.Equal(TokenType.Less, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_LessEqual_ProducesLessEqual()
    {
        var tokens = Scan("<=");
        Assert.Equal(TokenType.LessEqual, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_GreaterAlone_ProducesGreater()
    {
        var tokens = Scan(">");
        Assert.Equal(TokenType.Greater, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_GreaterEqual_ProducesGreaterEqual()
    {
        var tokens = Scan(">=");
        Assert.Equal(TokenType.GreaterEqual, tokens[0].Type);
    }

    // ── 3. Integer literals ─────────────────────────────────────────────

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("0", 0L)]
    [InlineData("999999", 999999L)]
    public void ScanTokens_IntegerLiteral_ProducesLongValue(string source, long expected)
    {
        var tokens = Scan(source);

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
        Assert.Equal(expected, tokens[0].Literal);
        Assert.Equal(source, tokens[0].Lexeme);
    }

    // ── 4. Float literals ───────────────────────────────────────────────

    [Theory]
    [InlineData("3.14", 3.14)]
    [InlineData("0.5", 0.5)]
    [InlineData("100.0", 100.0)]
    public void ScanTokens_FloatLiteral_ProducesDoubleValue(string source, double expected)
    {
        var tokens = Scan(source);

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.FloatLiteral, tokens[0].Type);
        Assert.Equal(expected, tokens[0].Literal);
        Assert.Equal(source, tokens[0].Lexeme);
    }

    // ── 5. String literals ──────────────────────────────────────────────

    [Fact]
    public void ScanTokens_StringLiteral_ProducesStringValue()
    {
        var tokens = Scan("\"hello\"");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("hello", tokens[0].Literal);
        Assert.Equal("\"hello\"", tokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_EmptyString_ProducesEmptyStringLiteral()
    {
        var tokens = Scan("\"\"");

        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("", tokens[0].Literal);
    }

    // ── 6. String escape sequences ──────────────────────────────────────

    [Fact]
    public void ScanTokens_EscapeNewline_ProducesActualNewline()
    {
        var tokens = Scan("\"line\\nbreak\"");

        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("line\nbreak", tokens[0].Literal);
    }

    [Fact]
    public void ScanTokens_EscapeTab_ProducesActualTab()
    {
        var tokens = Scan("\"tab\\there\"");

        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("tab\there", tokens[0].Literal);
    }

    [Fact]
    public void ScanTokens_EscapeQuote_ProducesQuoteInString()
    {
        var tokens = Scan("\"quote\\\"inside\"");

        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("quote\"inside", tokens[0].Literal);
    }

    [Fact]
    public void ScanTokens_EscapeNullChar_ProducesNullChar()
    {
        var tokens = Scan("\"null\\0char\"");

        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("null\0char", tokens[0].Literal);
    }

    [Fact]
    public void ScanTokens_EscapeBackslash_ProducesBackslash()
    {
        var tokens = Scan("\"escape\\\\slash\"");

        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("escape\\slash", tokens[0].Literal);
    }

    // ── 7. Unterminated string ──────────────────────────────────────────

    [Fact]
    public void ScanTokens_UnterminatedString_AddsError()
    {
        var lexer = CreateLexer("\"missing end");
        lexer.ScanTokens();

        Assert.NotEmpty(lexer.Errors);
        Assert.Contains(lexer.Errors, e => e.Contains("Unterminated string"));
    }

    // ── 8. Keywords ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("let", TokenType.Let)]
    [InlineData("const", TokenType.Const)]
    [InlineData("fn", TokenType.Fn)]
    [InlineData("struct", TokenType.Struct)]
    [InlineData("enum", TokenType.Enum)]
    [InlineData("if", TokenType.If)]
    [InlineData("else", TokenType.Else)]
    [InlineData("for", TokenType.For)]
    [InlineData("in", TokenType.In)]
    [InlineData("while", TokenType.While)]
    [InlineData("return", TokenType.Return)]
    [InlineData("break", TokenType.Break)]
    [InlineData("continue", TokenType.Continue)]
    [InlineData("null", TokenType.Null)]
    [InlineData("try", TokenType.Try)]
    [InlineData("import", TokenType.Import)]
    [InlineData("from", TokenType.From)]
    public void ScanTokens_Keyword_ProducesCorrectType(string source, TokenType expected)
    {
        var tokens = Scan(source);

        Assert.Equal(2, tokens.Count);
        Assert.Equal(expected, tokens[0].Type);
        Assert.Equal(source, tokens[0].Lexeme);
        Assert.Null(tokens[0].Literal);
    }

    [Fact]
    public void ScanTokens_TrueKeyword_ProducesBooleanLiteral()
    {
        var tokens = Scan("true");

        Assert.Equal(TokenType.True, tokens[0].Type);
        Assert.Equal(true, tokens[0].Literal);
    }

    [Fact]
    public void ScanTokens_FalseKeyword_ProducesBooleanLiteral()
    {
        var tokens = Scan("false");

        Assert.Equal(TokenType.False, tokens[0].Type);
        Assert.Equal(false, tokens[0].Literal);
    }

    // ── 9. Identifiers ─────────────────────────────────────────────────

    [Theory]
    [InlineData("myVar")]
    [InlineData("_private")]
    [InlineData("camelCase123")]
    [InlineData("x")]
    [InlineData("_")]
    public void ScanTokens_Identifier_ProducesIdentifierType(string source)
    {
        var tokens = Scan(source);

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Identifier, tokens[0].Type);
        Assert.Equal(source, tokens[0].Lexeme);
    }

    // ── 10. Single-line comments ────────────────────────────────────────

    [Fact]
    public void ScanTokens_SingleLineComment_IsSkipped()
    {
        var tokens = Scan("// comment\n42");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
        Assert.Equal(42L, tokens[0].Literal);
        Assert.Equal(TokenType.Eof, tokens[1].Type);
    }

    [Fact]
    public void ScanTokens_SingleLineCommentAtEnd_ProducesOnlyEof()
    {
        var tokens = Scan("// just a comment");

        Assert.Single(tokens);
        Assert.Equal(TokenType.Eof, tokens[0].Type);
    }

    // ── 11. Block comments ──────────────────────────────────────────────

    [Fact]
    public void ScanTokens_BlockComment_IsSkipped()
    {
        var tokens = Scan("/* block */ 42");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
        Assert.Equal(42L, tokens[0].Literal);
    }

    // ── 12. Nested comments ─────────────────────────────────────────────

    [Fact]
    public void ScanTokens_NestedBlockComment_IsSkipped()
    {
        var tokens = Scan("/* outer /* inner */ still comment */ 42");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
        Assert.Equal(42L, tokens[0].Literal);
    }

    // ── 13. Unterminated block comment ──────────────────────────────────

    [Fact]
    public void ScanTokens_UnterminatedBlockComment_AddsError()
    {
        var lexer = CreateLexer("/* no end");
        lexer.ScanTokens();

        Assert.NotEmpty(lexer.Errors);
        Assert.Contains(lexer.Errors, e => e.Contains("Unterminated block comment"));
    }

    // ── 14. Shebang ────────────────────────────────────────────────────

    [Fact]
    public void ScanTokens_Shebang_IsSkipped()
    {
        var tokens = Scan("#!/usr/bin/env stash\n42");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
        Assert.Equal(42L, tokens[0].Literal);
    }

    // ── 15. Whitespace and newline handling ──────────────────────────────

    [Fact]
    public void ScanTokens_WhitespaceIsSkipped()
    {
        var tokens = Scan("   \t\r  42");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_NewlineIncrementsLine()
    {
        var tokens = Scan("\n42");

        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
        Assert.Equal(2, tokens[0].Span.StartLine);
    }

    // ── 16. Source span accuracy ────────────────────────────────────────

    [Fact]
    public void ScanTokens_FirstToken_StartsAtLine1Col1()
    {
        var tokens = Scan("42");

        var span = tokens[0].Span;
        Assert.Equal("<stdin>", span.File);
        Assert.Equal(1, span.StartLine);
        Assert.Equal(1, span.StartColumn);
        Assert.Equal(1, span.EndLine);
        Assert.Equal(2, span.EndColumn);
    }

    [Fact]
    public void ScanTokens_MultiLineTracking_SpansAreCorrect()
    {
        var tokens = Scan("a\nb");

        Assert.Equal(1, tokens[0].Span.StartLine);
        Assert.Equal(1, tokens[0].Span.StartColumn);

        Assert.Equal(2, tokens[1].Span.StartLine);
        Assert.Equal(1, tokens[1].Span.StartColumn);
    }

    [Fact]
    public void ScanTokens_CustomFileName_AppearsInSpan()
    {
        var lexer = new Lexer("42", "test.stash");
        var tokens = lexer.ScanTokens();

        Assert.Equal("test.stash", tokens[0].Span.File);
    }

    // ── 17. EOF ─────────────────────────────────────────────────────────

    [Fact]
    public void ScanTokens_AnyInput_AlwaysEndsWithEof()
    {
        var tokens = Scan("1 + 2");

        Assert.Equal(TokenType.Eof, tokens[^1].Type);
    }

    [Fact]
    public void ScanTokens_EofToken_HasEmptyLexeme()
    {
        var tokens = Scan("");

        Assert.Equal(TokenType.Eof, tokens[0].Type);
        Assert.Equal("", tokens[0].Lexeme);
        Assert.Null(tokens[0].Literal);
    }

    // ── 18. Unexpected character ────────────────────────────────────────

    [Fact]
    public void ScanTokens_UnexpectedCharacter_AddsErrorAndContinues()
    {
        var lexer = CreateLexer("@42");
        var tokens = lexer.ScanTokens();

        Assert.NotEmpty(lexer.Errors);
        Assert.Contains(lexer.Errors, e => e.Contains("Unexpected character '@'"));

        // Scanning continues past the bad character
        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
        Assert.Equal(42L, tokens[0].Literal);
    }

    // ── 19. Single & ────────────────────────────────────────────────────

    [Fact]
    public void ScanTokens_SingleAmpersand_ProducesError()
    {
        var lexer = CreateLexer("&");
        lexer.ScanTokens();

        Assert.NotEmpty(lexer.Errors);
        Assert.Contains(lexer.Errors, e => e.Contains("Unexpected character '&'"));
    }

    [Fact]
    public void ScanTokens_DoubleAmpersand_ProducesAmpersandAmpersand()
    {
        var tokens = Scan("&&");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.AmpersandAmpersand, tokens[0].Type);
    }

    // ── 20. Multiple tokens on one line ─────────────────────────────────

    [Fact]
    public void ScanTokens_MultipleTokensOneLine_CorrectTypesAndSpans()
    {
        var tokens = Scan("1 + 2");

        Assert.Equal(4, tokens.Count);

        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
        Assert.Equal(1L, tokens[0].Literal);
        Assert.Equal(1, tokens[0].Span.StartColumn);
        Assert.Equal(1, tokens[0].Span.EndColumn);

        Assert.Equal(TokenType.Plus, tokens[1].Type);
        Assert.Equal(3, tokens[1].Span.StartColumn);
        Assert.Equal(3, tokens[1].Span.EndColumn);

        Assert.Equal(TokenType.IntegerLiteral, tokens[2].Type);
        Assert.Equal(2L, tokens[2].Literal);
        Assert.Equal(5, tokens[2].Span.StartColumn);
        Assert.Equal(5, tokens[2].Span.EndColumn);

        Assert.Equal(TokenType.Eof, tokens[3].Type);
    }

    // ── 21. Empty input ─────────────────────────────────────────────────

    [Fact]
    public void ScanTokens_EmptyInput_ProducesOnlyEof()
    {
        var tokens = Scan("");

        Assert.Single(tokens);
        Assert.Equal(TokenType.Eof, tokens[0].Type);
    }

    // ── 22. String interpolation ────────────────────────────────────────

    [Fact]
    public void ScanTokens_PrefixedInterpolation_ProducesInterpolatedStringToken()
    {
        var tokens = Scan("$\"Hello {name}\"");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.InterpolatedString, tokens[0].Type);
        Assert.Equal(TokenType.Eof, tokens[1].Type);
    }

    [Fact]
    public void ScanTokens_EmbeddedInterpolation_ProducesInterpolatedStringToken()
    {
        var tokens = Scan("\"Hello ${name}\"");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.InterpolatedString, tokens[0].Type);
        Assert.Equal(TokenType.Eof, tokens[1].Type);
    }

    [Fact]
    public void ScanTokens_RegularStringWithoutInterpolation_ProducesStringLiteral()
    {
        var tokens = Scan("\"hello world\"");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("hello world", tokens[0].Literal);
    }

    [Fact]
    public void ScanTokens_DollarAlone_ProducesDollarToken()
    {
        var tokens = Scan("$ + 1");

        Assert.Equal(TokenType.Dollar, tokens[0].Type);
        Assert.Equal("$", tokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_PrefixedInterpolation_LiteralIsListWithCorrectParts()
    {
        var tokens = Scan("$\"Hello {name}\"");

        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Equal(2, parts.Count);
        Assert.Equal("Hello ", parts[0]);
        var exprTokens = Assert.IsType<List<Token>>(parts[1]);
        Assert.Single(exprTokens);
        Assert.Equal(TokenType.Identifier, exprTokens[0].Type);
        Assert.Equal("name", exprTokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_EmbeddedInterpolation_LiteralIsListWithCorrectParts()
    {
        var tokens = Scan("\"Hello ${name}\"");

        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Equal(2, parts.Count);
        Assert.Equal("Hello ", parts[0]);
        var exprTokens = Assert.IsType<List<Token>>(parts[1]);
        Assert.Single(exprTokens);
        Assert.Equal(TokenType.Identifier, exprTokens[0].Type);
        Assert.Equal("name", exprTokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_MultipleInterpolationExpressions()
    {
        var tokens = Scan("$\"{a} and {b}\"");

        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Equal(3, parts.Count);
        Assert.IsType<List<Token>>(parts[0]); // a
        Assert.Equal(" and ", parts[1]);
        Assert.IsType<List<Token>>(parts[2]); // b
    }

    [Fact]
    public void ScanTokens_EscapedBrace_DoesNotTriggerInterpolation()
    {
        var tokens = Scan("$\"Hello \\{name}\"");

        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        // \{ produces literal '{', so text is "Hello {name}"
        Assert.Single(parts);
        Assert.Equal("Hello {name}", parts[0]);
    }

    [Fact]
    public void ScanTokens_EscapedDollar_DoesNotTriggerInterpolation()
    {
        // \$ inside a regular string avoids triggering ${...}
        var tokens = Scan("\"Hello \\${name}\"");

        // \$ is treated as literal $, so no interpolation
        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_NestedStringLiteralInsideInterpolation()
    {
        var tokens = Scan("$\"{\"hi\"}\"");

        Assert.Equal(TokenType.InterpolatedString, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        var exprTokens = Assert.IsType<List<Token>>(parts[0]);
        Assert.Equal(TokenType.StringLiteral, exprTokens[0].Type);
        Assert.Equal("hi", exprTokens[0].Literal);
    }

    [Fact]
    public void ScanTokens_EmptyInterpolationExpression_ProducesError()
    {
        var lexer = CreateLexer("$\"Hello {}\"");
        lexer.ScanTokens();

        Assert.NotEmpty(lexer.Errors);
        Assert.Contains(lexer.Errors, e => e.Contains("Empty interpolation expression"));
    }

    [Fact]
    public void ScanTokens_UnterminatedInterpolationExpression_ProducesError()
    {
        var lexer = CreateLexer("$\"Hello {name");
        lexer.ScanTokens();

        Assert.NotEmpty(lexer.Errors);
    }

    [Fact]
    public void ScanTokens_AdjacentInterpolations()
    {
        var tokens = Scan("$\"{a}{b}\"");

        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Equal(2, parts.Count);
        Assert.IsType<List<Token>>(parts[0]); // a
        Assert.IsType<List<Token>>(parts[1]); // b
    }

    [Fact]
    public void ScanTokens_TextOnlyInterpolatedString_NoExpressions()
    {
        // $"plain text" should still be InterpolatedString with just a text part
        var tokens = Scan("$\"plain text\"");

        Assert.Equal(TokenType.InterpolatedString, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        Assert.Equal("plain text", parts[0]);
    }

    [Fact]
    public void ScanTokens_ExpressionOnlyInterpolatedString_NoText()
    {
        var tokens = Scan("$\"{x}\"");

        Assert.Equal(TokenType.InterpolatedString, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        var exprTokens = Assert.IsType<List<Token>>(parts[0]);
        Assert.Equal(TokenType.Identifier, exprTokens[0].Type);
        Assert.Equal("x", exprTokens[0].Lexeme);
    }

    // ── Phase 4: Command Literal Tokens ──────────────────────────────────

    [Fact]
    public void ScanTokens_CommandLiteral_SimpleCommand()
    {
        var tokens = Scan("$(echo hello)");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.CommandLiteral, tokens[0].Type);
        Assert.Equal("$(echo hello)", tokens[0].Lexeme);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        Assert.Equal("echo hello", parts[0]);
    }

    [Fact]
    public void ScanTokens_CommandLiteral_WithInterpolation()
    {
        var tokens = Scan("$(echo {name})");

        Assert.Equal(TokenType.CommandLiteral, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Equal(2, parts.Count);
        Assert.Equal("echo ", parts[0]);
        var exprTokens = Assert.IsType<List<Token>>(parts[1]);
        Assert.Equal(TokenType.Identifier, exprTokens[0].Type);
        Assert.Equal("name", exprTokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_CommandLiteral_MultipleInterpolations()
    {
        var tokens = Scan("$(grep {pattern} {file})");

        Assert.Equal(TokenType.CommandLiteral, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        // "grep " + pattern + " " + file + ""
        Assert.True(parts.Count >= 4);
        Assert.Equal("grep ", parts[0]);
    }

    [Fact]
    public void ScanTokens_CommandLiteral_NestedParens()
    {
        var tokens = Scan("$(echo (nested) text)");

        Assert.Equal(TokenType.CommandLiteral, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        Assert.Equal("echo (nested) text", parts[0]);
    }

    [Fact]
    public void ScanTokens_CommandLiteral_Unterminated_ReportsError()
    {
        var lexer = CreateLexer("$(echo hello");
        lexer.ScanTokens();
        Assert.NotEmpty(lexer.Errors);
        Assert.Contains("Unterminated command literal", lexer.Errors[0]);
    }

    [Fact]
    public void ScanTokens_CommandLiteral_Empty()
    {
        var tokens = Scan("$()");

        Assert.Equal(TokenType.CommandLiteral, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Empty(parts);
    }

    [Fact]
    public void ScanTokens_CommandLiteral_WithExpressionInterpolation()
    {
        var tokens = Scan("$(echo {a + b})");

        Assert.Equal(TokenType.CommandLiteral, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.True(parts.Count >= 2);
        // Should have expression tokens for "a + b"
        bool hasExprTokens = false;
        foreach (var part in parts)
        {
            if (part is List<Token> t && t.Count >= 3)
            {
                hasExprTokens = true;
            }
        }
        Assert.True(hasExprTokens);
    }

    [Fact]
    public void ScanTokens_DollarAlone_StillProducesDollarToken()
    {
        // Ensure $(...) doesn't break the plain $ token
        var tokens = Scan("$");
        Assert.Equal(TokenType.Dollar, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_DollarQuote_StillProducesInterpolatedString()
    {
        // Ensure $"..." still works (no regression)
        var tokens = Scan("$\"hello\"");
        Assert.Equal(TokenType.InterpolatedString, tokens[0].Type);
    }

    [Fact]
    public void ScanTokens_CommandLiteral_FollowedByPipe()
    {
        var tokens = Scan("$(echo hello) | $(cat)");

        Assert.Equal(TokenType.CommandLiteral, tokens[0].Type);
        Assert.Equal(TokenType.Pipe, tokens[1].Type);
        Assert.Equal(TokenType.CommandLiteral, tokens[2].Type);
    }

    [Fact]
    public void ScanTokens_CommandLiteral_MultiLineCommand()
    {
        var tokens = Scan("$(echo\nhello)");

        Assert.Equal(TokenType.CommandLiteral, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        Assert.Equal("echo\nhello", parts[0]);
    }

    [Fact]
    public void ScanTokens_CommandLiteral_QuotedParensDoNotAffectDepth()
    {
        var tokens = Scan("$(echo \"(hello)\")");

        Assert.Equal(TokenType.CommandLiteral, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        Assert.Equal("echo \"(hello)\"", parts[0]);
    }

    [Fact]
    public void ScanTokens_CommandLiteral_SingleQuotedParensDoNotAffectDepth()
    {
        var tokens = Scan("$(echo ')')");

        Assert.Equal(TokenType.CommandLiteral, tokens[0].Type);
        var parts = Assert.IsType<List<object>>(tokens[0].Literal);
        Assert.Single(parts);
        Assert.Equal("echo ')'", parts[0]);
    }

    [Fact]
    public void ScanTokens_AsKeyword_ProducesAsToken()
    {
        var tokens = Scan("as");
        Assert.Equal(2, tokens.Count); // As + EOF
        Assert.Equal(TokenType.As, tokens[0].Type);
        Assert.Equal("as", tokens[0].Lexeme);
    }

    // ── Arrow Token ────────────────────────────────────────────────────

    [Fact]
    public void ScanTokens_Arrow_ProducesArrowToken()
    {
        var tokens = Scan("->");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.Arrow, tokens[0].Type);
        Assert.Equal("->", tokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_MinusAlone_StillProducesMinus()
    {
        var tokens = Scan("- x");

        Assert.Equal(TokenType.Minus, tokens[0].Type);
        Assert.Equal("-", tokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_Decrement_StillProducesMinusMinus()
    {
        var tokens = Scan("--");

        Assert.Equal(TokenType.MinusMinus, tokens[0].Type);
        Assert.Equal("--", tokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_ArrowInContext_ProducesCorrectTokens()
    {
        var tokens = Scan("fn add(a, b) -> int { }");

        Assert.Equal(TokenType.Fn, tokens[0].Type);
        Assert.Equal(TokenType.Identifier, tokens[1].Type); // add
        Assert.Equal(TokenType.LeftParen, tokens[2].Type);
        Assert.Equal(TokenType.Identifier, tokens[3].Type); // a
        Assert.Equal(TokenType.Comma, tokens[4].Type);
        Assert.Equal(TokenType.Identifier, tokens[5].Type); // b
        Assert.Equal(TokenType.RightParen, tokens[6].Type);
        Assert.Equal(TokenType.Arrow, tokens[7].Type);
        Assert.Equal(TokenType.Identifier, tokens[8].Type); // int
        Assert.Equal(TokenType.LeftBrace, tokens[9].Type);
        Assert.Equal(TokenType.RightBrace, tokens[10].Type);
    }

    [Fact]
    public void ScanTokens_KeywordSwitch_ProducesKeywordToken()
    {
        var tokens = Scan("switch");
        Assert.Equal(2, tokens.Count); // switch + EOF
        Assert.Equal(TokenType.Switch, tokens[0].Type);
        Assert.Equal("switch", tokens[0].Lexeme);
    }

    [Fact]
    public void ScanTokens_LessThan_StillWorks()
    {
        // Ensure << detection doesn't break < and <=
        var tokens = Scan("1 < 2");
        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
        Assert.Equal(TokenType.Less, tokens[1].Type);
        Assert.Equal(TokenType.IntegerLiteral, tokens[2].Type);
    }

    [Fact]
    public void ScanTokens_LessEqual_StillWorks()
    {
        var tokens = Scan("1 <= 2");
        Assert.Equal(TokenType.IntegerLiteral, tokens[0].Type);
        Assert.Equal(TokenType.LessEqual, tokens[1].Type);
        Assert.Equal(TokenType.IntegerLiteral, tokens[2].Type);
    }
}
