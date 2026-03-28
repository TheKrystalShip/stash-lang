using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Interpreting.Types;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class PathBuiltInsTests
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

    // ── path.normalize ────────────────────────────────────────────────────

    [Fact]
    public void Normalize_ResolvesDotsToAbsolutePath()
    {
        var result = Run("let result = path.normalize(\"/foo/bar/../baz\");");
        Assert.Equal("/foo/baz", result);
    }

    [Fact]
    public void Normalize_NonStringThrows()
    {
        RunExpectingError("path.normalize(42);");
    }

    // ── path.isAbsolute ───────────────────────────────────────────────────

    [Fact]
    public void IsAbsolute_AbsolutePath()
    {
        var result = Run("let result = path.isAbsolute(\"/foo/bar\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IsAbsolute_RelativePath()
    {
        var result = Run("let result = path.isAbsolute(\"foo/bar\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IsAbsolute_NonStringThrows()
    {
        RunExpectingError("path.isAbsolute(42);");
    }

    // ── path.relative ─────────────────────────────────────────────────────

    [Fact]
    public void Relative_SiblingDirectories()
    {
        var result = Run("let result = path.relative(\"/foo/bar\", \"/foo/baz/file.txt\");");
        Assert.IsType<string>(result);
        Assert.Contains("baz", (string)result!);
    }

    [Fact]
    public void Relative_NonStringThrows()
    {
        RunExpectingError("path.relative(42, \"/foo\");");
    }

    // ── path.separator ────────────────────────────────────────────────────

    [Fact]
    public void Separator_ReturnsString()
    {
        var result = Run("let result = path.separator();");
        Assert.IsType<string>(result);
        var sep = (string)result!;
        Assert.True(sep == "/" || sep == "\\");
    }
}
