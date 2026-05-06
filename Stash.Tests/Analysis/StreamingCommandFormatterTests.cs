using Stash.Analysis;

namespace Stash.Tests.Analysis;

public class StreamingCommandFormatterTests
{
    private static string Format(string source, int indentSize = 2) =>
        new StashFormatter(indentSize, useTabs: false).Format(source);

    [Fact]
    public void Format_StreamingCommand_RoundTripsExactly()
    {
        var result = Format("let s = $<(echo hi);");
        Assert.Contains("$<(echo hi)", result);
    }

    [Fact]
    public void Format_StrictStreamingCommand_RoundTripsExactly()
    {
        var result = Format("let s = $!<(echo hi);");
        Assert.Contains("$!<(echo hi)", result);
    }

    [Fact]
    public void Format_ForInStreamingCommand_DoesNotThrow()
    {
        // Regression: EmitToken() threw IndexOutOfRangeException when formatting
        // files containing streaming for-in loops followed by further statements.
        var source = "for (let line in $<(seq 1 10)) {\n  io.println(line);\n}\nio.println(\"done\");";
        var result = Format(source);
        Assert.Contains("$<(seq 1 10)", result);
        Assert.Contains("io.println(\"done\")", result);
    }

    [Fact]
    public void Format_ForInStreamingCommand_DualVariable_DoesNotThrow()
    {
        var source = "for (let out, err in $<(sh -c \"echo hi\")) {\n  io.println(out);\n}";
        var result = Format(source);
        Assert.Contains("$<(sh -c \"echo hi\")", result);
        Assert.Contains("out, err", result);
    }

    // Regression tests for the optional-semicolon crash in PrintExprStmt.
    // timeout and retry block expressions omit the trailing ';' in source;
    // the formatter must not consume the next statement's token as a semicolon.

    [Fact]
    public void Format_TimeoutAsStatement_NoSemicolon_DoesNotThrow()
    {
        var source = "timeout 1s {\n  io.println(\"hi\");\n}\nio.println(\"after\");";
        var result = Format(source);
        Assert.Contains("timeout 1s", result);
        Assert.Contains("io.println(\"after\")", result);
    }

    [Fact]
    public void Format_TimeoutInsideTryCatch_NoSemicolon_DoesNotThrow()
    {
        // This is the exact pattern from examples/streaming.stash that triggered the crash.
        var source = "try {\n  timeout 1s { for (let line in killed) {} }\n} catch (e) {\n  io.println(e.type);\n}";
        var result = Format(source);
        Assert.Contains("timeout 1s", result);
        Assert.Contains("catch", result);
        Assert.Contains("io.println(e.type)", result);
    }

    [Fact]
    public void Format_RetryAsStatement_NoSemicolon_DoesNotThrow()
    {
        var source = "retry(3) {\n  io.println(\"attempt\");\n}\nio.println(\"after\");";
        var result = Format(source);
        Assert.Contains("retry", result);
        Assert.Contains("io.println(\"after\")", result);
    }

    [Fact]
    public void Format_TimeoutAsStatement_WithSemicolon_DoesNotThrow()
    {
        // Ensure we still handle the semicolon variant correctly.
        var source = "timeout 1s {\n  io.println(\"hi\");\n};\nio.println(\"after\");";
        var result = Format(source);
        Assert.Contains("timeout 1s", result);
        Assert.Contains("io.println(\"after\")", result);
    }
}
