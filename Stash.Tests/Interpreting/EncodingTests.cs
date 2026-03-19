using System;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Interpreting.Types;

namespace Stash.Tests.Interpreting;

public class EncodingTests
{
    private static object? Run(string source)
    {
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
        return interpreter.Interpret(resultExpr);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

    // ── Base64 Encode ─────────────────────────────────────────────────────────

    [Fact]
    public void Base64Encode_EmptyString()
    {
        var result = Run(@"let result = encoding.base64Encode("""");");
        Assert.Equal("", result);
    }

    [Fact]
    public void Base64Encode_Hello()
    {
        var result = Run(@"let result = encoding.base64Encode(""hello"");");
        Assert.Equal("aGVsbG8=", result);
    }

    [Fact]
    public void Base64Encode_HelloWorld()
    {
        var result = Run(@"let result = encoding.base64Encode(""Hello, World!"");");
        Assert.Equal("SGVsbG8sIFdvcmxkIQ==", result);
    }

    [Fact]
    public void Base64Encode_Unicode()
    {
        // "café" in UTF-8: bytes [0x63, 0x61, 0x66, 0xc3, 0xa9] → base64 "Y2Fmw6k="
        var result = Run("let result = encoding.base64Encode(\"caf\u00e9\");");
        Assert.Equal("Y2Fmw6k=", result);
    }

    [Fact]
    public void Base64Encode_BinaryLikeContent_Roundtrips()
    {
        var result = Run(@"let result = encoding.base64Decode(encoding.base64Encode(""abc123!@#""));");
        Assert.Equal("abc123!@#", result);
    }

    // ── Base64 Decode ─────────────────────────────────────────────────────────

    [Fact]
    public void Base64Decode_EmptyString()
    {
        var result = Run(@"let result = encoding.base64Decode("""");");
        Assert.Equal("", result);
    }

    [Fact]
    public void Base64Decode_Hello()
    {
        var result = Run(@"let result = encoding.base64Decode(""aGVsbG8="");");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Base64Decode_HelloWorld()
    {
        var result = Run(@"let result = encoding.base64Decode(""SGVsbG8sIFdvcmxkIQ=="");");
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void Base64Decode_InvalidInput_Throws()
    {
        RunExpectingError(@"encoding.base64Decode(""not valid base64!!!"");");
    }

    [Fact]
    public void Base64_Roundtrip()
    {
        var result = Run(@"let result = encoding.base64Decode(encoding.base64Encode(""test data round trip!""));");
        Assert.Equal("test data round trip!", result);
    }

    // ── URL Encode ────────────────────────────────────────────────────────────

    [Fact]
    public void UrlEncode_SimpleString()
    {
        var result = Run(@"let result = encoding.urlEncode(""hello"");");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void UrlEncode_Spaces()
    {
        var result = Run(@"let result = encoding.urlEncode(""hello world"");");
        Assert.Equal("hello%20world", result);
    }

    [Fact]
    public void UrlEncode_SpecialChars()
    {
        var result = Run(@"let result = encoding.urlEncode(""key=value&foo=bar"");");
        Assert.Equal("key%3Dvalue%26foo%3Dbar", result);
    }

    [Fact]
    public void UrlEncode_Unicode()
    {
        // "café": 'é' (U+00E9) encodes to UTF-8 bytes 0xC3 0xA9 → "%C3%A9"
        var result = Run("let result = encoding.urlEncode(\"caf\u00e9\");");
        Assert.Equal("caf%C3%A9", result);
    }

    [Fact]
    public void UrlEncode_AlreadyEncoded_DoublyEncodes()
    {
        // '%' is a special char and gets percent-encoded to "%25"
        var result = Run(@"let result = encoding.urlEncode(""hello%20world"");");
        Assert.Equal("hello%2520world", result);
    }

    // ── URL Decode ────────────────────────────────────────────────────────────

    [Fact]
    public void UrlDecode_SimpleString()
    {
        var result = Run(@"let result = encoding.urlDecode(""hello"");");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void UrlDecode_Spaces()
    {
        var result = Run(@"let result = encoding.urlDecode(""hello%20world"");");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void UrlDecode_SpecialChars()
    {
        var result = Run(@"let result = encoding.urlDecode(""key%3Dvalue%26foo%3Dbar"");");
        Assert.Equal("key=value&foo=bar", result);
    }

    [Fact]
    public void UrlEncodeDecode_Roundtrip()
    {
        var result = Run(@"let result = encoding.urlDecode(encoding.urlEncode(""hello world & more!""));");
        Assert.Equal("hello world & more!", result);
    }

    // ── Hex Encode ────────────────────────────────────────────────────────────

    [Fact]
    public void HexEncode_EmptyString()
    {
        var result = Run(@"let result = encoding.hexEncode("""");");
        Assert.Equal("", result);
    }

    [Fact]
    public void HexEncode_Hello()
    {
        var result = Run(@"let result = encoding.hexEncode(""hello"");");
        Assert.Equal("68656c6c6f", result);
    }

    [Fact]
    public void HexEncode_ABC()
    {
        var result = Run(@"let result = encoding.hexEncode(""ABC"");");
        Assert.Equal("414243", result);
    }

    [Fact]
    public void HexEncode_ReturnsLowercase()
    {
        var result = Run(@"let result = encoding.hexEncode(""hello"");") as string;
        Assert.NotNull(result);
        Assert.Equal(result, result!.ToLowerInvariant());
    }

    // ── Hex Decode ────────────────────────────────────────────────────────────

    [Fact]
    public void HexDecode_EmptyString()
    {
        var result = Run(@"let result = encoding.hexDecode("""");");
        Assert.Equal("", result);
    }

    [Fact]
    public void HexDecode_Hello()
    {
        var result = Run(@"let result = encoding.hexDecode(""68656c6c6f"");");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void HexDecode_UppercaseInput()
    {
        var result = Run(@"let result = encoding.hexDecode(""414243"");");
        Assert.Equal("ABC", result);
    }

    [Fact]
    public void HexDecode_InvalidInput_Throws()
    {
        RunExpectingError(@"encoding.hexDecode(""xyz"");");
    }

    [Fact]
    public void HexDecode_OddLengthInput_Throws()
    {
        RunExpectingError(@"encoding.hexDecode(""abc"");");
    }

    [Fact]
    public void HexEncodeDecode_Roundtrip()
    {
        var result = Run(@"let result = encoding.hexDecode(encoding.hexEncode(""round trip test!""));");
        Assert.Equal("round trip test!", result);
    }

    // ── Type Validation ───────────────────────────────────────────────────────

    [Fact]
    public void Base64Encode_NonString_Throws()
    {
        RunExpectingError(@"encoding.base64Encode(42);");
    }

    [Fact]
    public void UrlEncode_NonString_Throws()
    {
        RunExpectingError(@"encoding.urlEncode(true);");
    }

    [Fact]
    public void HexEncode_NonString_Throws()
    {
        RunExpectingError(@"encoding.hexEncode(null);");
    }
}
