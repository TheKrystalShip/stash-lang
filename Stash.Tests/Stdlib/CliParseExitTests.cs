namespace Stash.Tests.Stdlib;

using System.IO;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for P7 of the cli-arg-parsing feature: cli.parse — the exit/print wrapper.
///
/// All tests use EmbeddedMode = true so the VM throws ExitException instead of calling
/// System.Environment.Exit, making the exit code observable from C#.
///
/// done_when coverage:
///   1. cli.parse(schema) with no argv reads IInterpreterContext.ScriptArgs.
///   2. --help / -h → prints cli.help(schema) to stdout, exits 0.
///   3. Parse failure → prints short error + abbreviated usage to stderr, exits 2.
///   4. Success → returns the same dict shape cli.tryParse(...).value would produce.
/// </summary>
public class CliParseExitTests : StashTestBase
{
    // =========================================================================
    // VM builder — EmbeddedMode so ExitException is observable
    // =========================================================================

    private static (Chunk chunk, VirtualMachine vm) Build(
        string source,
        StringWriter? stdout = null,
        StringWriter? stderr = null,
        string[]? scriptArgs = null)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.EmbeddedMode = true;
        if (stdout is not null) vm.Output = stdout;
        if (stderr is not null) vm.ErrorOutput = stderr;
        if (scriptArgs is not null) vm.ScriptArgs = scriptArgs;
        return (chunk, vm);
    }

    // =========================================================================
    // 1. Success path — returns parsed dict, same shape as tryParse(..).value
    // =========================================================================

    [Fact]
    public void Parse_SuccessWithExplicitArgv_ReturnsParsedDict()
    {
        var (chunk, vm) = Build("""
            let schema = cli.schema({ input: cli.positional("string") });
            let parsed = cli.parse(schema, ["hello"]);
            let result = parsed.input;
            """);
        object? value = Normalize(vm.Execute(chunk));
        Assert.Equal("hello", value);
    }

    [Fact]
    public void Parse_SuccessWithOptions_ReturnsDictWithAllKeys()
    {
        var (chunk, vm) = Build("""
            let schema = cli.schema({
                verbose: cli.flag({}),
                output: cli.option("string", { defaultVal: "./out" })
            });
            let parsed = cli.parse(schema, ["--verbose"]);
            let result = parsed.verbose;
            """);
        object? value = Normalize(vm.Execute(chunk));
        Assert.Equal(true, value);
    }

    [Fact]
    public void Parse_SuccessShape_MatchesTryParseValueShape()
    {
        // cli.parse with same argv should yield the same .input as cli.tryParse(..).value.input
        var (chunk, vm) = Build("""
            let schema = cli.schema({ n: cli.option("int", { defaultVal: 0 }) });
            let via_parse     = cli.parse(schema, ["--n", "42"]);
            let via_try_parse = cli.tryParse(schema, ["--n", "42"]);
            let result = via_parse.n == via_try_parse.value.n;
            """);
        object? value = Normalize(vm.Execute(chunk));
        Assert.Equal(true, value);
    }

    // =========================================================================
    // 2. ScriptArgs default — no explicit argv reads ctx.ScriptArgs
    // =========================================================================

    [Fact]
    public void Parse_NoArgvArg_ReadsScriptArgs()
    {
        // When argv is omitted, cli.parse reads ctx.ScriptArgs (set via vm.ScriptArgs)
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ name: cli.positional("string") });
            let parsed = cli.parse(schema);
            let result = parsed.name;
            """,
            scriptArgs: ["world"]);
        object? value = Normalize(vm.Execute(chunk));
        Assert.Equal("world", value);
    }

    [Fact]
    public void Parse_EmptyScriptArgs_UsesDefaults()
    {
        // An optional positional with default should use the default when ScriptArgs is empty
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ out: cli.positional("string", { required: false, defaultVal: "./out" }) });
            let parsed = cli.parse(schema);
            let result = parsed.out;
            """,
            scriptArgs: []);
        object? value = Normalize(vm.Execute(chunk));
        Assert.Equal("./out", value);
    }

    // =========================================================================
    // 3. --help path — prints help to stdout, exits 0
    // =========================================================================

    [Fact]
    public void Parse_HelpFlag_ExitsZero()
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ input: cli.positional("string", { required: false }) }, { programName: "myprog" });
            let result = cli.parse(schema, ["--help"]);
            """,
            stdout: stdout);
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(0, ex.ExitCode);
    }

    [Fact]
    public void Parse_HelpFlag_WritesHelpTextToStdout()
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({
                input: cli.positional("string", { required: false })
            }, { programName: "myprog" });
            let result = cli.parse(schema, ["--help"]);
            """,
            stdout: stdout);
        Assert.Throws<ExitException>(() => vm.Execute(chunk));
        string output = stdout.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("myprog", output);
    }

    [Fact]
    public void Parse_ShortHelpFlag_ExitsZeroAndPrintsHelp()
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ val: cli.option("string", { defaultVal: "x" }) }, { programName: "prog" });
            let result = cli.parse(schema, ["-h"]);
            """,
            stdout: stdout);
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(0, ex.ExitCode);
        Assert.Contains("Usage:", stdout.ToString());
    }

    // =========================================================================
    // 4. Error path — prints error + abbreviated usage to stderr, exits 2
    // =========================================================================

    [Fact]
    public void Parse_MissingRequired_ExitsTwo()
    {
        var stderr = new StringWriter { NewLine = "\n" };
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ input: cli.positional("string") });
            let result = cli.parse(schema, []);
            """,
            stderr: stderr);
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(2, ex.ExitCode);
    }

    [Fact]
    public void Parse_MissingRequired_WritesErrorToStderr()
    {
        var stderr = new StringWriter { NewLine = "\n" };
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ input: cli.positional("string") });
            let result = cli.parse(schema, []);
            """,
            stderr: stderr);
        Assert.Throws<ExitException>(() => vm.Execute(chunk));
        string errText = stderr.ToString();
        // Should contain the error message
        Assert.NotEmpty(errText);
    }

    [Fact]
    public void Parse_MissingRequired_WritesAbbreviatedUsageToStderr()
    {
        var stderr = new StringWriter { NewLine = "\n" };
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ input: cli.positional("string") }, { programName: "myprog" });
            let result = cli.parse(schema, []);
            """,
            stderr: stderr);
        Assert.Throws<ExitException>(() => vm.Execute(chunk));
        string errText = stderr.ToString();
        // Abbreviated usage line should appear in stderr output
        Assert.Contains("Usage:", errText);
        Assert.Contains("myprog", errText);
    }

    [Fact]
    public void Parse_UnknownOption_ExitsTwo()
    {
        var stderr = new StringWriter { NewLine = "\n" };
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ verbose: cli.flag({}) });
            let result = cli.parse(schema, ["--nope"]);
            """,
            stderr: stderr);
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(2, ex.ExitCode);
    }

    [Fact]
    public void Parse_InvalidValue_ExitsTwo()
    {
        var stderr = new StringWriter { NewLine = "\n" };
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ count: cli.option("int", { defaultVal: 1 }) });
            let result = cli.parse(schema, ["--count", "notanumber"]);
            """,
            stderr: stderr);
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(2, ex.ExitCode);
    }

    [Fact]
    public void Parse_HelpDoesNotWriteToStderr()
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter { NewLine = "\n" };
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ v: cli.flag({}) });
            let result = cli.parse(schema, ["--help"]);
            """,
            stdout: stdout,
            stderr: stderr);
        Assert.Throws<ExitException>(() => vm.Execute(chunk));
        // Help output goes to stdout, not stderr
        Assert.NotEmpty(stdout.ToString());
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Parse_ErrorDoesNotWriteToStdout()
    {
        var stdout = new StringWriter { NewLine = "\n" };
        var stderr = new StringWriter { NewLine = "\n" };
        var (chunk, vm) = Build(
            """
            let schema = cli.schema({ input: cli.positional("string") });
            let result = cli.parse(schema, []);
            """,
            stdout: stdout,
            stderr: stderr);
        Assert.Throws<ExitException>(() => vm.Execute(chunk));
        // Error output goes to stderr, stdout should be empty
        Assert.Empty(stdout.ToString());
        Assert.NotEmpty(stderr.ToString());
    }
}
