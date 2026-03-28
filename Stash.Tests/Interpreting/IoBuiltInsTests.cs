using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;

namespace Stash.Tests.Interpreting;

public class IoBuiltInsTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string CaptureStderr(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var sw = new StringWriter();
        interpreter.ErrorOutput = sw;
        interpreter.Interpret(statements);
        return sw.ToString();
    }

    private static (string stdout, string stderr) CaptureBoth(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var outSw = new StringWriter();
        var errSw = new StringWriter();
        interpreter.Output = outSw;
        interpreter.ErrorOutput = errSw;
        interpreter.Interpret(statements);
        return (outSw.ToString(), errSw.ToString());
    }

    private static object? RunReturningValue(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var sw = new StringWriter();
        interpreter.Output = sw;
        interpreter.ErrorOutput = sw;
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    // ── 1. io.eprintln writes to stderr ──────────────────────────────────────

    [Fact]
    public void Eprintln_WritesToStderr()
    {
        var stderr = CaptureStderr("io.eprintln(\"hello\");");
        Assert.Equal("hello" + System.Environment.NewLine, stderr);
    }

    // ── 2. io.eprint writes to stderr (no newline) ───────────────────────────

    [Fact]
    public void Eprint_WritesToStderr()
    {
        var stderr = CaptureStderr("io.eprint(\"hello\");");
        Assert.Equal("hello", stderr);
    }

    // ── 3. io.eprintln does not write to stdout ───────────────────────────────

    [Fact]
    public void Eprintln_DoesNotWriteToStdout()
    {
        var (stdout, _) = CaptureBoth("io.eprintln(\"x\");");
        Assert.Equal("", stdout);
    }

    // ── 4. io.eprint does not write to stdout ────────────────────────────────

    [Fact]
    public void Eprint_DoesNotWriteToStdout()
    {
        var (stdout, _) = CaptureBoth("io.eprint(\"x\");");
        Assert.Equal("", stdout);
    }

    // ── 5. io.eprintln stringifies non-string values ─────────────────────────

    [Fact]
    public void Eprintln_StringifiesValue()
    {
        var stderr = CaptureStderr("io.eprintln(42);");
        Assert.Equal("42" + System.Environment.NewLine, stderr);
    }

    // ── 6. io.eprint stringifies non-string values ───────────────────────────

    [Fact]
    public void Eprint_StringifiesValue()
    {
        var stderr = CaptureStderr("io.eprint(true);");
        Assert.Equal("true", stderr);
    }

    // ── 7. io.eprintln with null value ───────────────────────────────────────

    [Fact]
    public void Eprintln_NullValue()
    {
        var stderr = CaptureStderr("io.eprintln(null);");
        Assert.Equal("null" + System.Environment.NewLine, stderr);
    }

    // ── 8. Multiple io.eprintln calls append output ──────────────────────────

    [Fact]
    public void Eprintln_MultipleCallsAppend()
    {
        var stderr = CaptureStderr("io.eprintln(\"first\"); io.eprintln(\"second\");");
        Assert.Equal("first" + System.Environment.NewLine + "second" + System.Environment.NewLine, stderr);
    }

    // ── 9. io.eprintln returns null ───────────────────────────────────────────

    [Fact]
    public void Eprintln_ReturnsNull()
    {
        var result = RunReturningValue("let result = io.eprintln(\"x\");");
        Assert.Null(result);
    }

    // ── 10. io.eprint returns null ────────────────────────────────────────────

    [Fact]
    public void Eprint_ReturnsNull()
    {
        var result = RunReturningValue("let result = io.eprint(\"x\");");
        Assert.Null(result);
    }

    // ── io.confirm ────────────────────────────────────────────────────────

    [Fact]
    public void Confirm_YesReturnsTrue()
    {
        var interpreter = new Interpreter();
        var outSw = new System.IO.StringWriter();
        interpreter.Output = outSw;
        interpreter.Input = new System.IO.StringReader("y\n");

        var lexer = new Lexer("let result = io.confirm(\"Continue?\");");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        interpreter.Interpret(statements);

        var resultExpr = new Parser(new Lexer("result").ScanTokens()).Parse();
        var result = interpreter.Interpret(resultExpr);
        Assert.Equal(true, result);
        Assert.Contains("[y/N]", outSw.ToString());
    }

    [Fact]
    public void Confirm_NoReturnsFalse()
    {
        var interpreter = new Interpreter();
        var outSw = new System.IO.StringWriter();
        interpreter.Output = outSw;
        interpreter.Input = new System.IO.StringReader("n\n");

        var lexer = new Lexer("let result = io.confirm(\"Continue?\");");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        interpreter.Interpret(statements);

        var resultExpr = new Parser(new Lexer("result").ScanTokens()).Parse();
        var result = interpreter.Interpret(resultExpr);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Confirm_EmptyInputReturnsFalse()
    {
        var interpreter = new Interpreter();
        var outSw = new System.IO.StringWriter();
        interpreter.Output = outSw;
        interpreter.Input = new System.IO.StringReader("\n");

        var lexer = new Lexer("let result = io.confirm(\"Continue?\");");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        interpreter.Interpret(statements);

        var resultExpr = new Parser(new Lexer("result").ScanTokens()).Parse();
        var result = interpreter.Interpret(resultExpr);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Confirm_YesFullWordReturnsTrue()
    {
        var interpreter = new Interpreter();
        var outSw = new System.IO.StringWriter();
        interpreter.Output = outSw;
        interpreter.Input = new System.IO.StringReader("yes\n");

        var lexer = new Lexer("let result = io.confirm(\"Continue?\");");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        interpreter.Interpret(statements);

        var resultExpr = new Parser(new Lexer("result").ScanTokens()).Parse();
        var result = interpreter.Interpret(resultExpr);
        Assert.Equal(true, result);
    }
}
