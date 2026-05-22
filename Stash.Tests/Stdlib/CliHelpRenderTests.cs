namespace Stash.Tests.Stdlib;

using System.IO;
using System.Reflection;
using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for P6 of the cli-arg-parsing feature: cli.help and cli.printHelp.
/// </summary>
public class CliHelpRenderTests : StashTestBase
{
    // =========================================================================
    // Helper: load a snapshot fixture from the embedded resources.
    // =========================================================================

    private static string LoadFixture(string name)
    {
        Assembly asm = typeof(CliHelpRenderTests).Assembly;
        string resourceName = $"Stash.Tests.Stdlib.Fixtures.{name}";
        using Stream? stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new FileNotFoundException(
                $"Embedded fixture '{resourceName}' not found. " +
                $"Available: {string.Join(", ", asm.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n', '\r');
    }

    // =========================================================================
    // cli.help — basic structure
    // =========================================================================

    [Fact]
    public void Help_ProgramNameAndUsageLine_PresentInOutput()
    {
        var result = Run("""
            let schema = cli.schema({
                input: cli.positional("string")
            }, { programName: "myprog" });
            let result = cli.help(schema);
        """);
        Assert.NotNull(result);
        string text = (string)result!;
        Assert.Contains("Usage: myprog", text);
        Assert.Contains("<input>", text);
    }

    [Fact]
    public void Help_OptionTable_ContainsLongShortMetavarHelpDefault()
    {
        var result = Run("""
            let schema = cli.schema({
                output: cli.option("string", { short: "o", defaultVal: "./out", help: "Output path." })
            }, { programName: "myprog" });
            let result = cli.help(schema);
        """);
        string text = (string)result!;
        Assert.Contains("--output, -o", text);
        Assert.Contains("STRING", text);
        Assert.Contains("Output path.", text);
        Assert.Contains("(default: ./out)", text);
    }

    [Fact]
    public void Help_FlagDefault_FalseNotAnnotated_TrueAnnotated()
    {
        var result = Run("""
            let schema = cli.schema({
                verbose: cli.flag({ short: "v", help: "Verbose." }),
                debug: cli.flag({ short: "d", defaultVal: true, help: "Debug." })
            }, { programName: "myprog" });
            let result = cli.help(schema);
        """);
        string text = (string)result!;
        // default:false flags should NOT have a "(default: false)" annotation
        Assert.DoesNotContain("(default: false)", text);
        // default:true flags SHOULD have a "(default: true)" annotation
        Assert.Contains("(default: true)", text);
    }

    [Fact]
    public void Help_ImplicitHelpFlag_PresentInOptionTable()
    {
        var result = Run("""
            let schema = cli.schema({
                x: cli.flag({ help: "Something." })
            }, { programName: "myprog" });
            let result = cli.help(schema);
        """);
        string text = (string)result!;
        Assert.Contains("--help, -h", text);
        Assert.Contains("Show this help message.", text);
    }

    [Fact]
    public void Help_HelpFlagDisabled_HelpFlagAbsentFromOptionTable()
    {
        var result = Run("""
            let schema = cli.schema({
                x: cli.flag({ help: "Something." })
            }, { programName: "myprog", helpFlag: false });
            let result = cli.help(schema);
        """);
        string text = (string)result!;
        Assert.DoesNotContain("--help, -h", text);
    }

    [Fact]
    public void Help_SubcommandList_PresentInOutput()
    {
        var result = Run("""
            let addSchema = cli.schema({}, { programName: "myprog add" });
            let removeSchema = cli.schema({}, { programName: "myprog remove" });
            let schema = cli.schema({
                sub: cli.command({ add: addSchema, remove: removeSchema })
            }, { programName: "myprog" });
            let result = cli.help(schema);
        """);
        string text = (string)result!;
        Assert.Contains("Commands:", text);
        Assert.Contains("add", text);
        Assert.Contains("remove", text);
    }

    [Fact]
    public void Help_CommandOption_RoutesToSubcommandSchema()
    {
        var result = Run("""
            let addSchema = cli.schema({
                url: cli.option("string", { help: "Repository URL." })
            }, { programName: "myprog add", description: "Add a remote." });
            let schema = cli.schema({
                sub: cli.command({ add: addSchema })
            }, { programName: "myprog" });
            let result = cli.help(schema, { command: "add" });
        """);
        string text = (string)result!;
        Assert.Contains("--url", text);
        Assert.Contains("Repository URL.", text);
    }

    [Fact]
    public void Help_WidthOption_WrapsHelpTextAtSpecifiedColumn()
    {
        // Use a short width to force wrapping
        var result = Run("""
            let schema = cli.schema({
                output: cli.option("string", { help: "A very long help text that will definitely need to be wrapped because it exceeds the narrow column width we are testing here." })
            }, { programName: "myprog" });
            let result = cli.help(schema, { width: 50 });
        """);
        string text = (string)result!;
        // Every line in the result must be <= 50 characters
        foreach (string line in text.Split('\n'))
            Assert.True(line.Length <= 50, $"Line too long ({line.Length} > 50): '{line}'");
    }

    // =========================================================================
    // cli.printHelp
    // =========================================================================

    [Fact]
    public void PrintHelp_WritesToStdout()
    {
        string output = RunCapturingOutput("""
            let schema = cli.schema({
                input: cli.positional("string", { help: "Input file." })
            }, { programName: "myprog" });
            cli.printHelp(schema);
        """);
        Assert.Contains("Usage: myprog", output);
        Assert.Contains("<input>", output);
    }

    [Fact]
    public void PrintHelp_OutputMatchesHelpString()
    {
        // printHelp output (minus the trailing newline added by println) should match cli.help
        string helpStr = (string)Run("""
            let schema = cli.schema({
                x: cli.option("string", { help: "Something." })
            }, { programName: "myprog" });
            let result = cli.help(schema);
        """)!;

        string printHelpOutput = RunCapturingOutput("""
            let schema = cli.schema({
                x: cli.option("string", { help: "Something." })
            }, { programName: "myprog" });
            cli.printHelp(schema);
        """);

        // RunCapturingOutput includes the trailing newline added by ctx.Output.WriteLine
        Assert.Equal(helpStr, printHelpOutput.TrimEnd('\n', '\r'));
    }

    // =========================================================================
    // Snapshot tests
    // =========================================================================

    [Fact]
    public void Help_FlatSchema_MatchesSnapshot()
    {
        var result = Run("""
            let schema = cli.schema({
                input: cli.positional("string", { help: "Input file to process." }),
                output: cli.option("string", { short: "o", defaultVal: "./out", help: "Output destination." }),
                verbose: cli.flag({ short: "v", help: "Enable verbose output." }),
                retries: cli.option("int", { defaultVal: 3, help: "Number of retries." })
            }, { programName: "mytool", description: "A test tool for processing files." });
            let result = cli.help(schema, { width: 80 });
        """);
        string actual = (string)result!;
        string expected = LoadFixture("cli_help_flat.txt");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Help_SubcommandSchema_MatchesSnapshot()
    {
        var result = Run("""
            let addSchema = cli.schema({
                url: cli.option("string", { short: "u", help: "Repository URL to add." })
            }, { programName: "mytool", description: "Add a remote." });
            let removeSchema = cli.schema({
                name: cli.positional("string", { help: "Remote name to remove." })
            }, { programName: "mytool", description: "Remove a remote." });
            let schema = cli.schema({
                sub: cli.command({ add: addSchema, remove: removeSchema })
            }, { programName: "mytool", description: "Manage remote repositories." });
            let result = cli.help(schema, { width: 80 });
        """);
        string actual = (string)result!;
        string expected = LoadFixture("cli_help_subcommands.txt");
        Assert.Equal(expected, actual);
    }
}
