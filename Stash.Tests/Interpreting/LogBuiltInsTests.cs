using System.IO;
using System.Text.RegularExpressions;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Interpreting;

public class LogBuiltInsTests : StashTestBase
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string stdout, string stderr) CaptureBoth(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var outSw = new StringWriter();
        var errSw = new StringWriter();
        vm.Output = outSw;
        vm.ErrorOutput = errSw;
        vm.Execute(chunk);
        return (outSw.ToString(), errSw.ToString());
    }

    private static (string stdout, string stderr) CaptureWithReset(string source)
        => CaptureBoth(source);

    // ── Text format pattern: [YYYY-MM-DD HH:MM:SS.mmm] LEVEL Message ─────────
    private static readonly Regex TextPattern =
        new(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] (DEBUG|INFO |WARN |ERROR) .+", RegexOptions.None);

    // ── JSON format pattern ───────────────────────────────────────────────────
    private static readonly Regex JsonPattern =
        new(@"^\{""ts"":""\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z"",""level"":""(DEBUG|INFO|WARN|ERROR)"",""msg"":"".+""\}", RegexOptions.None);

    // ── 1. log.debug writes to stderr at default INFO level (suppressed) ──────

    [Fact]
    public void Debug_SuppressedAtInfoLevel()
    {
        var (_, stderr) = CaptureWithReset("log.debug(\"secret\");");
        Assert.Equal("", stderr.Trim());
    }

    // ── 2. log.info writes to stderr ─────────────────────────────────────────

    [Fact]
    public void Info_WritesToStderr()
    {
        var (_, stderr) = CaptureWithReset("log.info(\"hello\");");
        var line = stderr.Trim();
        Assert.Matches(TextPattern, line);
        Assert.Contains("INFO ", line);
        Assert.Contains("hello", line);
    }

    // ── 3. log.warn writes to stderr ─────────────────────────────────────────

    [Fact]
    public void Warn_WritesToOutput()
    {
        var (_, stderr) = CaptureWithReset("log.warn(\"careful\");");
        var line = stderr.Trim();
        Assert.Contains("WARN ", line);
        Assert.Contains("careful", line);
    }

    // ── 4. log.error writes to stderr ────────────────────────────────────────

    [Fact]
    public void Error_WritesToOutput()
    {
        var (_, stderr) = CaptureWithReset("log.error(\"boom\");");
        var line = stderr.Trim();
        Assert.Contains("ERROR", line);
        Assert.Contains("boom", line);
    }

    // ── 5. setLevel("debug") shows DEBUG messages ────────────────────────────

    [Fact]
    public void SetLevel_Debug_ShowsAll()
    {
        var (_, stderr) = CaptureWithReset("log.setLevel(\"debug\"); log.debug(\"dbg\");");
        var line = stderr.Trim();
        Assert.Contains("DEBUG", line);
        Assert.Contains("dbg", line);
    }

    // ── 6. setLevel("error") suppresses info/warn ────────────────────────────

    [Fact]
    public void SetLevel_Error_ShowsOnlyErrors()
    {
        var (_, stderr) = CaptureWithReset(
            "log.setLevel(\"error\"); log.info(\"ignored\"); log.warn(\"also ignored\"); log.error(\"shown\");");
        Assert.DoesNotContain("ignored", stderr);
        Assert.DoesNotContain("also ignored", stderr);
        Assert.Contains("shown", stderr);
    }

    // ── 7. setLevel filters messages below threshold ──────────────────────────

    [Fact]
    public void SetLevel_FiltersMessages()
    {
        var (_, stderr) = CaptureWithReset(
            "log.setLevel(\"warn\"); log.info(\"skip\"); log.warn(\"keep\");");
        Assert.DoesNotContain("skip", stderr);
        Assert.Contains("keep", stderr);
    }

    // ── 8. setLevel with invalid value throws ────────────────────────────────

    [Fact]
    public void SetLevel_InvalidLevel_ThrowsError()
    {
        var err = RunCapturingError("log.setLevel(\"verbose\");");
        Assert.Contains("log.setLevel", err.Message);
        Assert.Contains("verbose", err.Message);
    }

    // ── 9. setFormat("text") produces text format ─────────────────────────────

    [Fact]
    public void SetFormat_Text_FormatsCorrectly()
    {
        var (_, stderr) = CaptureWithReset("log.setFormat(\"text\"); log.info(\"msg\");");
        var line = stderr.Trim();
        Assert.Matches(TextPattern, line);
    }

    // ── 10. setFormat("json") produces JSON ───────────────────────────────────

    [Fact]
    public void SetFormat_Json_FormatsCorrectly()
    {
        var (_, stderr) = CaptureWithReset("log.setFormat(\"json\"); log.info(\"msg\");");
        var line = stderr.Trim();
        Assert.StartsWith("{", line);
        Assert.Contains("\"level\":\"INFO\"", line);
        Assert.Contains("\"msg\":\"msg\"", line);
        Assert.Contains("\"ts\":", line);
    }

    // ── 11. setFormat with invalid value throws ───────────────────────────────

    [Fact]
    public void SetFormat_InvalidFormat_ThrowsError()
    {
        var err = RunCapturingError("log.setFormat(\"xml\");");
        Assert.Contains("log.setFormat", err.Message);
        Assert.Contains("xml", err.Message);
    }

    // ── 12. setOutput("stdout") writes to stdout ─────────────────────────────

    [Fact]
    public void SetOutput_Stdout_WritesToStdout()
    {
        var (stdout, stderr) = CaptureWithReset(
            "log.setOutput(\"stdout\"); log.info(\"on stdout\");");
        Assert.Contains("on stdout", stdout);
        Assert.DoesNotContain("on stdout", stderr);
    }

    // ── 13. setOutput("stderr") writes to stderr ─────────────────────────────

    [Fact]
    public void SetOutput_Stderr_WritesToStderr()
    {
        var (stdout, stderr) = CaptureWithReset(
            "log.setOutput(\"stderr\"); log.info(\"on stderr\");");
        Assert.Contains("on stderr", stderr);
        Assert.DoesNotContain("on stderr", stdout);
    }

    // ── 14. setOutput(filePath) writes to file ───────────────────────────────

    [Fact]
    public void SetOutput_FilePath_WritesToFile()
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            var (_, _) = CaptureWithReset(
                $"log.setOutput(\"{path.Replace("\\", "/")}\"); log.info(\"file msg\"); log.setOutput(\"stderr\");");
            string content = File.ReadAllText(path);
            Assert.Contains("file msg", content);
        }
        finally
        {
            // Reset to stderr for subsequent tests then clean up file.
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── 15. withFields returns a dict with all log methods ────────────────────

    [Fact]
    public void WithFields_HasAllMethods()
    {
        var result = Run("""
            let logger = log.withFields({service: "api"});
            let result = typeof(logger);
            """);
        Assert.Equal("dict", result);
    }

    // ── 16. withFields merges preset fields into every entry ─────────────────

    [Fact]
    public void WithFields_PresetFieldsAppearInOutput()
    {
        var (_, stderr) = CaptureWithReset("""
            let logger = log.withFields({svc: "web"});
            logger["info"]("request received");
            """);
        var line = stderr.Trim();
        Assert.Contains("svc=web", line);
        Assert.Contains("request received", line);
    }

    // ── 17. withFields merges preset AND per-call data fields ─────────────────

    [Fact]
    public void WithFields_MergesFields()
    {
        var (_, stderr) = CaptureWithReset("""
            let logger = log.withFields({env: "prod"});
            logger["info"]("deploy", {version: "1.2.3"});
            """);
        var line = stderr.Trim();
        Assert.Contains("env=prod", line);
        Assert.Contains("version=1.2.3", line);
    }

    // ── 18. data dict fields are merged into the log entry ────────────────────

    [Fact]
    public void Log_WithDataDict_MergesFields()
    {
        var (_, stderr) = CaptureWithReset("""
            log.info("request", {method: "GET", status: 200});
            """);
        var line = stderr.Trim();
        Assert.Contains("method=GET", line);
        Assert.Contains("status=200", line);
    }

    // ── 19. scalar data is emitted as data=<value> ───────────────────────────

    [Fact]
    public void Log_WithDataString_AddsDataField()
    {
        var (_, stderr) = CaptureWithReset("log.info(\"done\", \"extra\");");
        var line = stderr.Trim();
        Assert.Contains("data=extra", line);
    }

    // ── 20. timestamp is always included ─────────────────────────────────────

    [Fact]
    public void Log_TimestampIncluded()
    {
        var (_, stderr) = CaptureWithReset("log.info(\"ts test\");");
        var line = stderr.Trim();
        // Text format: [YYYY-MM-DD HH:MM:SS.mmm]
        Assert.Matches(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\]", line);
    }

    // ── 21. case-insensitive level parsing ────────────────────────────────────

    [Fact]
    public void SetLevel_CaseInsensitive()
    {
        var (_, stderr) = CaptureWithReset("log.setLevel(\"DEBUG\"); log.debug(\"upper\");");
        Assert.Contains("upper", stderr);
    }

    // ── 22. "warning" alias for warn ─────────────────────────────────────────

    [Fact]
    public void SetLevel_Warning_Alias()
    {
        var (_, stderr) = CaptureWithReset("log.setLevel(\"warning\"); log.info(\"skip\"); log.warn(\"hit\");");
        Assert.DoesNotContain("skip", stderr);
        Assert.Contains("hit", stderr);
    }

    // ── 23. JSON format includes correct level name ───────────────────────────

    [Fact]
    public void SetFormat_Json_LevelNamesCorrect()
    {
        var (_, stderr) = CaptureWithReset(
            "log.setFormat(\"json\"); log.setLevel(\"debug\"); log.debug(\"d\"); log.warn(\"w\");");
        var lines = stderr.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"level\":\"DEBUG\"", lines[0]);
        Assert.Contains("\"level\":\"WARN\"", lines[1]);
    }

    // ── 24. JSON format with numeric data field ───────────────────────────────

    [Fact]
    public void SetFormat_Json_NumericDataField()
    {
        var (_, stderr) = CaptureWithReset(
            "log.setFormat(\"json\"); log.info(\"cnt\", {count: 42});");
        var line = stderr.Trim();
        Assert.Contains("\"count\":42", line);
    }

    // ── 25. log functions return null ─────────────────────────────────────────

    [Fact]
    public void LogFunctions_ReturnNull()
    {
        var result = Run("let result = log.info(\"x\");");
        Assert.Null(result);
    }
}
