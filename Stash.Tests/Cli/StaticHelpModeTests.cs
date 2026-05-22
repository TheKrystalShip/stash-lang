namespace Stash.Tests.Cli;

using System.IO;
using Stash.Cli.Modes;
using Stash.Analysis.Cli;
using Xunit;

/// <summary>
/// Tests for P10: <c>stash --help script.stash</c> static discovery mode.
/// Covers StaticHelpMode and the LiteralSchemaBuilder end-to-end.
/// </summary>
public class StaticHelpModeTests
{
    // ── Helper ───────────────────────────────────────────────────────────────

    private static (string output, int exitCode) RunOnSource(string stashSource)
    {
        // Write the source to a temp file so StaticHelpMode.Run can read it.
        string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".stash");
        try
        {
            File.WriteAllText(tempPath, stashSource);
            var sw = new StringWriter();
            int code = StaticHelpMode.Run(tempPath, sw);
            return (sw.ToString().Replace("\r\n", "\n").TrimEnd('\n', '\r'), code);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    // ── Fully-literal schema fixture ─────────────────────────────────────────

    [Fact]
    public void Run_FullyLiteralSchema_PrintsRenderedHelp_ExitsZero()
    {
        // Note: Stash dict literals do not support trailing commas.
        string source =
            "// Simple fixture\n" +
            "let schema = cli.schema({\n" +
            "    input: cli.positional(\"string\", { help: \"Input path.\" }),\n" +
            "    verbose: cli.flag({ short: \"v\", help: \"Enable verbose mode.\" }),\n" +
            "    retries: cli.option(\"int\", { default: 3 })\n" +
            "}, { programName: \"myprog\", description: \"My tool.\" });\n";

        var (output, exitCode) = RunOnSource(source);

        Assert.Equal(0, exitCode);
        // Help text must include usage and declared options.
        Assert.Contains("Usage: myprog", output);
        Assert.Contains("--retries", output);
        Assert.Contains("--verbose", output);
        Assert.Contains("-v", output);
        Assert.Contains("My tool.", output);
    }

    [Fact]
    public void Run_FullyLiteralSchema_ExitCode_IsZero()
    {
        string source =
            "let schema = cli.schema({\n" +
            "    input: cli.positional(\"string\")\n" +
            "});\n";

        var (_, exitCode) = RunOnSource(source);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Run_FullyLiteralSchema_DoesNotContainFallback()
    {
        string source =
            "let schema = cli.schema({\n" +
            "    input: cli.positional(\"string\")\n" +
            "});\n";

        var (output, _) = RunOnSource(source);
        Assert.DoesNotContain(StaticHelpMode.FallbackMessage, output);
    }

    // ── Non-literal initializer → fallback ──────────────────────────────────

    [Fact]
    public void Run_NonLiteralInitializer_PrintsFallback_ExitsZero()
    {
        // The opts dict is a variable reference — non-literal.
        string source =
            "let schemaOpts = { required: true };\n" +
            "let schema = cli.schema({\n" +
            "    input: cli.positional(\"string\", schemaOpts)\n" +
            "});\n";

        var (output, exitCode) = RunOnSource(source);

        Assert.Equal(0, exitCode);
        Assert.Contains(StaticHelpMode.FallbackMessage, output);
    }

    // ── No schema binding → fallback ─────────────────────────────────────────

    [Fact]
    public void Run_NoSchemaBinding_PrintsFallback_ExitsZero()
    {
        string source =
            "let x = 42;\n" +
            "io.println(\"hello\");\n";

        var (output, exitCode) = RunOnSource(source);

        Assert.Equal(0, exitCode);
        Assert.Contains(StaticHelpMode.FallbackMessage, output);
    }

    // ── Comment marker override ──────────────────────────────────────────────

    [Fact]
    public void Run_CommentMarkerOverride_UsesNamedBinding()
    {
        string source =
            "// @cli-schema-binding: mySchema\n" +
            "let mySchema = cli.schema({\n" +
            "    output: cli.option(\"string\", { short: \"o\" })\n" +
            "}, { programName: \"mytool\" });\n";

        var (output, exitCode) = RunOnSource(source);

        Assert.Equal(0, exitCode);
        Assert.Contains("mytool", output);
        Assert.Contains("--output", output);
    }

    [Fact]
    public void Run_CommentMarkerOverride_WrongDefaultName_UsesFallback()
    {
        // marker redirects to "mySchema" but the binding is named "schema"
        // so TryBuild won't find it and should fall back.
        string source =
            "// @cli-schema-binding: mySchema\n" +
            "let schema = cli.schema({\n" +
            "    input: cli.positional(\"string\")\n" +
            "});\n";

        var (output, _) = RunOnSource(source);
        Assert.Contains(StaticHelpMode.FallbackMessage, output);
    }

    // ── Default negative values (spec predicate tests via LiteralSchemaBuilder) ──

    [Fact]
    public void Run_NegativeIntDefault_IsLiteral_RendersHelp()
    {
        string source =
            "let schema = cli.schema({\n" +
            "    retries: cli.option(\"int\", { default: -1 })\n" +
            "});\n";

        var (output, exitCode) = RunOnSource(source);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(StaticHelpMode.FallbackMessage, output);
        Assert.Contains("--retries", output);
    }

    [Fact]
    public void Run_NegativeFloatDefault_IsLiteral_RendersHelp()
    {
        string source =
            "let schema = cli.schema({\n" +
            "    threshold: cli.option(\"float\", { default: -1.5 })\n" +
            "});\n";

        var (output, exitCode) = RunOnSource(source);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(StaticHelpMode.FallbackMessage, output);
        Assert.Contains("--threshold", output);
    }

    // ── Missing file → fallback ──────────────────────────────────────────────

    [Fact]
    public void Run_MissingFile_PrintsFallback_ExitsZero()
    {
        var sw = new StringWriter();
        int code = StaticHelpMode.Run("/nonexistent/path/fixture.stash", sw);
        string output = sw.ToString();

        Assert.Equal(0, code);
        Assert.Contains(StaticHelpMode.FallbackMessage, output);
    }

    // ── Fallback message exact content ───────────────────────────────────────

    [Fact]
    public void FallbackMessage_HasExpectedContent()
    {
        Assert.Contains("usage: stash <script> [args...]", StaticHelpMode.FallbackMessage);
        Assert.Contains("No statically discoverable CLI schema", StaticHelpMode.FallbackMessage);
    }

    // ── Comment marker resolver ──────────────────────────────────────────────

    [Fact]
    public void ResolveBindingName_NoMarker_ReturnsDefault()
    {
        string source = "let schema = cli.schema({ });";
        Assert.Equal(LiteralSchemaBuilder.DefaultBindingName, LiteralSchemaBuilder.ResolveBindingName(source));
    }

    [Fact]
    public void ResolveBindingName_WithMarker_ReturnsMarkerName()
    {
        string source = "// @cli-schema-binding: myCliSchema\nlet myCliSchema = cli.schema({ });";
        Assert.Equal("myCliSchema", LiteralSchemaBuilder.ResolveBindingName(source));
    }

    [Fact]
    public void ResolveBindingName_FirstMarkerWins()
    {
        string source = "// @cli-schema-binding: first\n// @cli-schema-binding: second\nlet first = cli.schema({ });";
        Assert.Equal("first", LiteralSchemaBuilder.ResolveBindingName(source));
    }
}
