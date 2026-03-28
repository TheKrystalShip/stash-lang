using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Interpreting.Types;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class JsonBuiltInsTests
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

    // ── json.valid ────────────────────────────────────────────────────────

    [Fact]
    public void Valid_ValidObject()
    {
        var result = Run("let result = json.valid(\"{}\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_ValidArray()
    {
        var result = Run("let result = json.valid(\"[1, 2, 3]\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_ValidString()
    {
        var result = Run("let result = json.valid(\"\\\"hello\\\"\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_ValidNumber()
    {
        var result = Run("let result = json.valid(\"42\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_InvalidJson()
    {
        var result = Run("let result = json.valid(\"not json at all\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_InvalidBraces()
    {
        var result = Run("let result = json.valid(\"{ bad }\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_EmptyString()
    {
        var result = Run("let result = json.valid(\"\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_NonStringThrows()
    {
        RunExpectingError("json.valid(42);");
    }
}
