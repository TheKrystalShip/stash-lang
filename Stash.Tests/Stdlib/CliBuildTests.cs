namespace Stash.Tests.Stdlib;

using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for cli.build — the inverse of cli.parse.
/// Covers: round-trip property, option emission, flag emission, positionals, and validation rejection.
/// </summary>
public class CliBuildTests : StashTestBase
{
    // =========================================================================
    // Round-trip: cli.parse(schema, cli.build(schema, values)) == values
    // =========================================================================

    [Fact]
    public void Build_RoundTrip_StringOption()
    {
        var result = Run("""
            let schema = cli.schema({ output: cli.option("string", { defaultVal: "./out" }) });
            let values = cli.tryParse(schema, ["--output=./result"]).value;
            let built = cli.build(schema, values);
            let reparsed = cli.tryParse(schema, built).value;
            let result = reparsed.output;
        """);
        Assert.Equal("./result", result);
    }

    [Fact]
    public void Build_RoundTrip_IntOption()
    {
        var result = Run("""
            let schema = cli.schema({ port: cli.option("int", { defaultVal: 80 }) });
            let values = cli.tryParse(schema, ["--port=9090"]).value;
            let built = cli.build(schema, values);
            let reparsed = cli.tryParse(schema, built).value;
            let result = reparsed.port;
        """);
        Assert.Equal(9090L, result);
    }

    [Fact]
    public void Build_RoundTrip_Flag()
    {
        var result = Run("""
            let schema = cli.schema({ verbose: cli.flag() });
            let values = cli.tryParse(schema, ["--verbose"]).value;
            let built = cli.build(schema, values);
            let reparsed = cli.tryParse(schema, built).value;
            let result = reparsed.verbose;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Build_RoundTrip_Positional()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let values = cli.tryParse(schema, ["hello"]).value;
            let built = cli.build(schema, values);
            let reparsed = cli.tryParse(schema, built).value;
            let result = reparsed.input;
        """);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Build_RoundTrip_MultipleArgs()
    {
        var result = Run("""
            let schema = cli.schema({
                input: cli.positional("string"),
                output: cli.option("string", { defaultVal: "./out" }),
                verbose: cli.flag()
            });
            let values = cli.tryParse(schema, ["myfile", "--output=result.txt", "--verbose"]).value;
            let built = cli.build(schema, values);
            let reparsed = cli.tryParse(schema, built).value;
            let result = reparsed.output;
        """);
        Assert.Equal("result.txt", result);
    }

    // =========================================================================
    // Option emission
    // =========================================================================

    [Fact]
    public void Build_StringOption_EmitsLongForm()
    {
        var result = Run("""
            let schema = cli.schema({ output: cli.option("string") });
            let built = cli.build(schema, { output: "file.txt" });
            let result = built[0];
        """);
        Assert.Equal("--output=file.txt", result);
    }

    [Fact]
    public void Build_IntOption_EmitsLongForm()
    {
        var result = Run("""
            let schema = cli.schema({ port: cli.option("int") });
            let built = cli.build(schema, { port: 8080 });
            let result = built[0];
        """);
        Assert.Equal("--port=8080", result);
    }

    [Fact]
    public void Build_KebabCasedLongName()
    {
        var result = Run("""
            let schema = cli.schema({ dryRun: cli.flag() });
            let built = cli.build(schema, { dryRun: true });
            let result = built[0];
        """);
        Assert.Equal("--dry-run", result);
    }

    [Fact]
    public void Build_OptionMissingFromValues_Skipped()
    {
        var result = Run("""
            let schema = cli.schema({ output: cli.option("string") });
            let built = cli.build(schema, {});
            let result = len(built);
        """);
        Assert.Equal(0L, result);
    }

    // =========================================================================
    // Flag emission
    // =========================================================================

    [Fact]
    public void Build_Flag_TrueWhenDefaultFalse_EmitsFlag()
    {
        var result = Run("""
            let schema = cli.schema({ verbose: cli.flag() });
            let built = cli.build(schema, { verbose: true });
            let result = built[0];
        """);
        Assert.Equal("--verbose", result);
    }

    [Fact]
    public void Build_Flag_FalseWhenDefaultFalse_Omitted()
    {
        var result = Run("""
            let schema = cli.schema({ verbose: cli.flag() });
            let built = cli.build(schema, { verbose: false });
            let result = len(built);
        """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Build_Flag_FalseWhenDefaultTrue_NegatedFlag()
    {
        var result = Run("""
            let schema = cli.schema({ color: cli.flag({ defaultVal: true, negatable: true }) });
            let built = cli.build(schema, { color: false });
            let result = built[0];
        """);
        Assert.Equal("--no-color", result);
    }

    [Fact]
    public void Build_Flag_FalseWhenDefaultTrue_NotNegatable_Throws()
    {
        RunExpectingError("""
            let schema = cli.schema({ color: cli.flag({ defaultVal: true }) });
            cli.build(schema, { color: false });
            let result = 0;
        """);
    }

    // =========================================================================
    // Positional emission
    // =========================================================================

    [Fact]
    public void Build_Positional_EmittedAfterOptions()
    {
        var result = Run("""
            let schema = cli.schema({
                output: cli.option("string"),
                input: cli.positional("string")
            });
            let built = cli.build(schema, { output: "out.txt", input: "in.txt" });
            let result = built[1];
        """);
        // First element is the option, second is the positional
        Assert.Equal("in.txt", result);
    }

    [Fact]
    public void Build_Positional_StartsWithDash_AddsSeparator()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let built = cli.build(schema, { input: "-weird" });
            let result = built[0];
        """);
        Assert.Equal("--", result);
    }

    // =========================================================================
    // Validation rejection
    // =========================================================================

    [Fact]
    public void Build_WrongType_Throws()
    {
        RunExpectingError("""
            let schema = cli.schema({ port: cli.option("int") });
            cli.build(schema, { port: "not-an-int" });
            let result = 0;
        """);
    }

    [Fact]
    public void Build_ChoicesViolation_Throws()
    {
        RunExpectingError("""
            let schema = cli.schema({ mode: cli.option("string", { choices: ["fast", "slow"] }) });
            cli.build(schema, { mode: "invalid" });
            let result = 0;
        """);
    }

    [Fact]
    public void Build_MinConstraintViolation_Throws()
    {
        RunExpectingError("""
            let schema = cli.schema({ port: cli.option("int", { min: 1024 }) });
            cli.build(schema, { port: 80 });
            let result = 0;
        """);
    }

    // =========================================================================
    // Type validation
    // =========================================================================

    [Fact]
    public void Build_NonSchemaFirstArg_Throws()
    {
        RunExpectingError("""
            cli.build("not a schema", {});
            let result = 0;
        """);
    }

    [Fact]
    public void Build_NonDictSecondArg_Throws()
    {
        RunExpectingError("""
            let schema = cli.schema({ port: cli.option("int") });
            cli.build(schema, "not a dict");
            let result = 0;
        """);
    }

    // =========================================================================
    // Repeated options
    // =========================================================================

    [Fact]
    public void Build_RepeatedOption_EmitsMultipleTokens()
    {
        var result = Run("""
            let schema = cli.schema({ tag: cli.option("string", { repeated: true }) });
            let values = cli.tryParse(schema, ["--tag=a", "--tag=b"]).value;
            let built = cli.build(schema, values);
            let result = len(built);
        """);
        Assert.Equal(2L, result);
    }
}
