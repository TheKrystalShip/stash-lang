using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Interpreting.Types;

namespace Stash.Tests.Interpreting;

public class TermBuiltInsTests
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

    // ── term.color ────────────────────────────────────────────────────────────

    [Fact]
    public void Color_Red()
    {
        var result = Run(@"let result = term.color(""hello"", ""red"");");
        Assert.Equal("\x1b[31mhello\x1b[0m", result);
    }

    [Fact]
    public void Color_Green()
    {
        var result = Run(@"let result = term.color(""ok"", ""green"");");
        Assert.Equal("\x1b[32mok\x1b[0m", result);
    }

    [Fact]
    public void Color_Yellow()
    {
        var result = Run(@"let result = term.color(""warn"", ""yellow"");");
        Assert.Equal("\x1b[33mwarn\x1b[0m", result);
    }

    [Fact]
    public void Color_Gray()
    {
        var result = Run(@"let result = term.color(""dim"", ""gray"");");
        Assert.Equal("\x1b[90mdim\x1b[0m", result);
    }

    [Fact]
    public void Color_Grey_Alias()
    {
        var result = Run(@"let result = term.color(""dim"", ""grey"");");
        Assert.Equal("\x1b[90mdim\x1b[0m", result);
    }

    [Fact]
    public void Color_Unknown_Throws()
    {
        RunExpectingError(@"term.color(""x"", ""neon"");");
    }

    [Fact]
    public void Color_NonStringText_Throws()
    {
        RunExpectingError(@"term.color(42, ""red"");");
    }

    // ── term color constants ──────────────────────────────────────────────────

    [Fact]
    public void Color_Constant_RED()
    {
        var result = Run(@"let result = term.color(""hello"", term.RED);");
        Assert.Equal("\x1b[31mhello\x1b[0m", result);
    }

    [Fact]
    public void Color_Constant_GREEN()
    {
        var result = Run(@"let result = term.color(""ok"", term.GREEN);");
        Assert.Equal("\x1b[32mok\x1b[0m", result);
    }

    [Fact]
    public void Color_Constant_BLUE()
    {
        var result = Run(@"let result = term.color(""info"", term.BLUE);");
        Assert.Equal("\x1b[34minfo\x1b[0m", result);
    }

    [Fact]
    public void Color_Constant_GRAY()
    {
        var result = Run(@"let result = term.color(""dim"", term.GRAY);");
        Assert.Equal("\x1b[90mdim\x1b[0m", result);
    }

    [Fact]
    public void Color_Constants_AreStrings()
    {
        var result = Run(@"let result = term.RED;");
        Assert.Equal("red", result);
    }

    [Fact]
    public void Style_WithConstant()
    {
        var result = Run("let result = term.style(\"alert\", { color: term.RED, bold: true });");
        Assert.Equal("\x1b[1;31malert\x1b[0m", result);
    }

    // ── term.bold ─────────────────────────────────────────────────────────────

    [Fact]
    public void Bold()
    {
        var result = Run(@"let result = term.bold(""strong"");");
        Assert.Equal("\x1b[1mstrong\x1b[0m", result);
    }

    [Fact]
    public void Bold_NonStringThrows()
    {
        RunExpectingError(@"term.bold(99);");
    }

    // ── term.dim ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dim()
    {
        var result = Run(@"let result = term.dim(""faded"");");
        Assert.Equal("\x1b[2mfaded\x1b[0m", result);
    }

    // ── term.underline ────────────────────────────────────────────────────────

    [Fact]
    public void Underline()
    {
        var result = Run(@"let result = term.underline(""link"");");
        Assert.Equal("\x1b[4mlink\x1b[0m", result);
    }

    // ── term.style ────────────────────────────────────────────────────────────

    [Fact]
    public void Style_BoldAndColor()
    {
        var result = Run("let result = term.style(\"alert\", { bold: true, color: \"red\" });");
        Assert.Equal("\x1b[1;31malert\x1b[0m", result);
    }

    [Fact]
    public void Style_AllOptions()
    {
        var result = Run("let result = term.style(\"x\", { bold: true, dim: true, underline: true, color: \"blue\" });");
        Assert.Equal("\x1b[1;2;4;34mx\x1b[0m", result);
    }

    [Fact]
    public void Style_EmptyDict()
    {
        var result = Run(@"let result = term.style(""plain"", {});");
        Assert.Equal("plain", result);
    }

    [Fact]
    public void Style_NonDictThrows()
    {
        RunExpectingError(@"term.style(""text"", ""not-a-dict"");");
    }

    // ── term.strip ────────────────────────────────────────────────────────────

    [Fact]
    public void Strip_RemovesColor()
    {
        var result = Run(@"
let colored = term.color(""hello"", ""red"");
let result = term.strip(colored);
");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Strip_RemovesMultipleCodes()
    {
        var result = Run(@"
let inner = term.color(""x"", ""red"");
let outer = term.bold(inner);
let result = term.strip(outer);
");
        Assert.Equal("x", result);
    }

    [Fact]
    public void Strip_PlainText()
    {
        var result = Run(@"let result = term.strip(""no codes"");");
        Assert.Equal("no codes", result);
    }

    // ── term.width ────────────────────────────────────────────────────────────

    [Fact]
    public void Width_ReturnsPositiveInt()
    {
        var result = Run("let result = term.width();");
        var width = Assert.IsType<long>(result);
        Assert.True(width > 0);
    }

    // ── term.isInteractive ────────────────────────────────────────────────────

    [Fact]
    public void IsInteractive_ReturnsBool()
    {
        var result = Run("let result = term.isInteractive();");
        Assert.IsType<bool>(result);
    }

    // ── term.clear ────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_WritesEscapeCodes()
    {
        var lexer = new Lexer("term.clear();");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var sw = new System.IO.StringWriter();
        interpreter.Output = sw;
        interpreter.Interpret(statements);
        Assert.Contains("\x1b[2J\x1b[H", sw.ToString());
    }

    // ── term.table ────────────────────────────────────────────────────────────

    [Fact]
    public void Table_BasicTable()
    {
        var result = Run(@"
let result = term.table([[1, ""Alice"", 90], [2, ""Bob"", 85]], [""ID"", ""Name"", ""Score""]);
");
        var table = Assert.IsType<string>(result);
        Assert.Contains("+", table);
        Assert.Contains("|", table);
        Assert.Contains("Alice", table);
        Assert.Contains("Bob", table);
        Assert.Contains("ID", table);
        Assert.Contains("Name", table);
        Assert.Contains("Score", table);
    }

    [Fact]
    public void Table_NoHeaders()
    {
        var result = Run(@"let result = term.table([[1, 2], [3, 4]]);");
        var table = Assert.IsType<string>(result);
        Assert.Contains("+", table);
        Assert.Contains("|", table);
    }

    [Fact]
    public void Table_EmptyRows()
    {
        var result = Run(@"let result = term.table([]);");
        Assert.Equal("", result);
    }

    [Fact]
    public void Table_NonArrayThrows()
    {
        RunExpectingError(@"term.table(""not an array"");");
    }
}
