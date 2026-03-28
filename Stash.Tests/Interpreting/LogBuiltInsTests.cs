using System.IO;
using System.Reflection;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Stdlib.BuiltIns;
using Stash.Interpreting.Types;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class LogBuiltInsTests : IDisposable
{
    // Reset static log state after each test to prevent interference.
    public void Dispose()
    {
        var levelField = typeof(LogBuiltIns).GetField("_level", BindingFlags.NonPublic | BindingFlags.Static);
        levelField?.SetValue(null, "info");

        var fmtField = typeof(LogBuiltIns).GetField("_format", BindingFlags.NonPublic | BindingFlags.Static);
        fmtField?.SetValue(null, "[{time}] [{level}] {msg}");

        var fwField = typeof(LogBuiltIns).GetField("_fileWriter", BindingFlags.NonPublic | BindingFlags.Static);
        if (fwField?.GetValue(null) is IDisposable d)
        {
            d.Dispose();
        }

        fwField?.SetValue(null, null);
    }

    private static (string stderr, object? result) RunCapturingStderr(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var errSw = new StringWriter();
        interpreter.ErrorOutput = errSw;
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        var result = interpreter.Interpret(resultExpr);
        return (errSw.ToString(), result);
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

    // ── log.info ──────────────────────────────────────────────────────────

    [Fact]
    public void Info_WritesToStderr()
    {
        var (stderr, _) = RunCapturingStderr("let result = log.info(\"hello\");");
        Assert.Contains("[INFO]", stderr);
        Assert.Contains("hello", stderr);
    }

    [Fact]
    public void Info_ReturnsNull()
    {
        var (_, result) = RunCapturingStderr("let result = log.info(\"hello\");");
        Assert.Null(result);
    }

    // ── log.debug ─────────────────────────────────────────────────────────

    [Fact]
    public void Debug_FilteredByDefault()
    {
        // Default level is "info", so debug should be filtered
        var (stderr, _) = RunCapturingStderr("let result = log.debug(\"secret\");");
        Assert.DoesNotContain("secret", stderr);
    }

    [Fact]
    public void Debug_ShownWhenLevelIsDebug()
    {
        var (stderr, _) = RunCapturingStderr(@"
            log.setLevel(""debug"");
            log.debug(""visible"");
            let result = null;
        ");
        Assert.Contains("[DEBUG]", stderr);
        Assert.Contains("visible", stderr);
    }

    // ── log.warn ──────────────────────────────────────────────────────────

    [Fact]
    public void Warn_WritesToStderr()
    {
        var (stderr, _) = RunCapturingStderr("let result = log.warn(\"caution\");");
        Assert.Contains("[WARN]", stderr);
        Assert.Contains("caution", stderr);
    }

    // ── log.error ─────────────────────────────────────────────────────────

    [Fact]
    public void Error_WritesToStderr()
    {
        var (stderr, _) = RunCapturingStderr("let result = log.error(\"failure\");");
        Assert.Contains("[ERROR]", stderr);
        Assert.Contains("failure", stderr);
    }

    // ── log.setLevel ──────────────────────────────────────────────────────

    [Fact]
    public void SetLevel_FiltersLowerLevels()
    {
        var (stderr, _) = RunCapturingStderr(@"
            log.setLevel(""error"");
            log.info(""should not appear"");
            log.warn(""should not appear"");
            log.error(""should appear"");
            let result = null;
        ");
        Assert.DoesNotContain("should not appear", stderr);
        Assert.Contains("should appear", stderr);
    }

    [Fact]
    public void SetLevel_OffSuppressesAll()
    {
        var (stderr, _) = RunCapturingStderr(@"
            log.setLevel(""off"");
            log.error(""hidden"");
            let result = null;
        ");
        Assert.DoesNotContain("hidden", stderr);
    }

    [Fact]
    public void SetLevel_InvalidLevelThrows()
    {
        RunExpectingError("log.setLevel(\"invalid\");");
    }

    [Fact]
    public void SetLevel_NonStringThrows()
    {
        RunExpectingError("log.setLevel(42);");
    }

    // ── log.setFormat ─────────────────────────────────────────────────────

    [Fact]
    public void SetFormat_CustomFormat()
    {
        var (stderr, _) = RunCapturingStderr(@"
            log.setFormat(""{level}: {msg}"");
            log.info(""custom"");
            let result = null;
        ");
        Assert.Contains("INFO: custom", stderr);
    }

    [Fact]
    public void SetFormat_NonStringThrows()
    {
        RunExpectingError("log.setFormat(42);");
    }

    // ── log.toFile ────────────────────────────────────────────────────────

    [Fact]
    public void ToFile_WritesToFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var lexer = new Lexer($"log.toFile(\"{tempFile.Replace("\\", "\\\\")}\");\nlog.info(\"file log\");");
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var statements = parser.ParseProgram();
            var interpreter = new Interpreter();
            var errSw = new StringWriter();
            interpreter.ErrorOutput = errSw;
            interpreter.Interpret(statements);

            var content = File.ReadAllText(tempFile);
            Assert.Contains("file log", content);
            Assert.Contains("[INFO]", content);
            // Should NOT appear on stderr when file is set
            Assert.DoesNotContain("file log", errSw.ToString());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ToFile_NonStringThrows()
    {
        RunExpectingError("log.toFile(42);");
    }
}
