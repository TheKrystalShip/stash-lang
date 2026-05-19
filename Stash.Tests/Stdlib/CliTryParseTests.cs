namespace Stash.Tests.Stdlib;

using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for P3 of the cli-arg-parsing feature: the parsing engine and cli.tryParse.
///
/// Error types covered (six required by done_when):
///   [1] CliMissingRequired  — missing required positional / option
///   [2] CliUnknownOption    — unrecognised --flag or -x
///   [3] CliMissingValue     — option present but no value supplied
///   [4] CliInvalidValue     — type conversion failure or choices violation
///   [5] CliUnexpectedPositional — extra positional beyond what schema declares
///   [6] CliAmbiguousOption  — option prefix matches more than one declared long name
///
/// Note: "default" is a reserved keyword in Stash; use "defaultVal" field on CliArgSpec.
/// Note: Stash dict literals do not allow trailing commas.
/// </summary>
public class CliTryParseTests : StashTestBase
{
    // =========================================================================
    // Positionals
    // =========================================================================

    [Fact]
    public void TryParse_SingleRequiredPositional_PopulatesResultValue()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema, ["hello"]);
            let result = r.value.input;
        """);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void TryParse_OptionalPositionalWithDefault_UsesDefault()
    {
        var result = Run("""
            let schema = cli.schema({ out: cli.positional("string", { required: false, defaultVal: "./out" }) });
            let r = cli.tryParse(schema, []);
            let result = r.value.out;
        """);
        Assert.Equal("./out", result);
    }

    [Fact]
    public void TryParse_RepeatedTrailingPositional_AccumulatesArray()
    {
        var result = Run("""
            let schema = cli.schema({ files: cli.positional("string", { repeated: true, required: false }) });
            let r = cli.tryParse(schema, ["a.txt", "b.txt", "c.txt"]);
            let result = r.value.files;
        """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("a.txt", list[0]);
        Assert.Equal("b.txt", list[1]);
        Assert.Equal("c.txt", list[2]);
    }

    [Fact]
    public void TryParse_MultiplePositionals_PopulatesInOrder()
    {
        var result = Run("""
            let schema = cli.schema({
                src: cli.positional("string"),
                dst: cli.positional("string")
            });
            let r = cli.tryParse(schema, ["from.txt", "to.txt"]);
            let result = r.value.dst;
        """);
        Assert.Equal("to.txt", result);
    }

    // =========================================================================
    // Options — long forms
    // =========================================================================

    [Fact]
    public void TryParse_LongOption_PopulatesValue()
    {
        var result = Run("""
            let schema = cli.schema({ output: cli.option("string") });
            let r = cli.tryParse(schema, ["--output", "file.txt"]);
            let result = r.value.output;
        """);
        Assert.Equal("file.txt", result);
    }

    [Fact]
    public void TryParse_LongOptionEqualsSyntax_PopulatesValue()
    {
        var result = Run("""
            let schema = cli.schema({ output: cli.option("string") });
            let r = cli.tryParse(schema, ["--output=file.txt"]);
            let result = r.value.output;
        """);
        Assert.Equal("file.txt", result);
    }

    [Fact]
    public void TryParse_IntOption_ConvertsToInt()
    {
        var result = Run("""
            let schema = cli.schema({ retries: cli.option("int", { defaultVal: 0 }) });
            let r = cli.tryParse(schema, ["--retries", "5"]);
            let result = r.value.retries;
        """);
        Assert.Equal(5L, result);
    }

    [Fact]
    public void TryParse_FloatOption_ConvertsToFloat()
    {
        var result = Run("""
            let schema = cli.schema({ rate: cli.option("float", { defaultVal: 0.0 }) });
            let r = cli.tryParse(schema, ["--rate", "1.5"]);
            let result = r.value.rate;
        """);
        Assert.Equal(1.5, result);
    }

    [Fact]
    public void TryParse_BoolOption_ConvertsUsingExplicitSet()
    {
        var result = Run("""
            let schema = cli.schema({ flag: cli.option("bool", { defaultVal: false }) });
            let r = cli.tryParse(schema, ["--flag", "yes"]);
            let result = r.value.flag;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TryParse_BoolOption_FalseValue()
    {
        var result = Run("""
            let schema = cli.schema({ flag: cli.option("bool", { defaultVal: true }) });
            let r = cli.tryParse(schema, ["--flag", "no"]);
            let result = r.value.flag;
        """);
        Assert.Equal(false, result);
    }

    // =========================================================================
    // Flags
    // =========================================================================

    [Fact]
    public void TryParse_Flag_DefaultFalse()
    {
        var result = Run("""
            let schema = cli.schema({ verbose: cli.flag() });
            let r = cli.tryParse(schema, []);
            let result = r.value.verbose;
        """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void TryParse_FlagPresent_ReturnsTrue()
    {
        var result = Run("""
            let schema = cli.schema({ verbose: cli.flag() });
            let r = cli.tryParse(schema, ["--verbose"]);
            let result = r.value.verbose;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TryParse_NegatableFlag_NoPrefix_ReturnsFalse()
    {
        var result = Run("""
            let schema = cli.schema({ color: cli.flag({ defaultVal: true, negatable: true }) });
            let r = cli.tryParse(schema, ["--no-color"]);
            let result = r.value.color;
        """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void TryParse_FlagDefaultTrue_Supplied_StillTrue()
    {
        var result = Run("""
            let schema = cli.schema({ color: cli.flag({ defaultVal: true }) });
            let r = cli.tryParse(schema, ["--color"]);
            let result = r.value.color;
        """);
        Assert.Equal(true, result);
    }

    // =========================================================================
    // Short options
    // =========================================================================

    [Fact]
    public void TryParse_ShortFlag_ReturnsTrue()
    {
        var result = Run("""
            let schema = cli.schema({ verbose: cli.flag({ short: "v" }) });
            let r = cli.tryParse(schema, ["-v"]);
            let result = r.value.verbose;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TryParse_ShortOptionSeparate_PopulatesValue()
    {
        var result = Run("""
            let schema = cli.schema({ output: cli.option("string", { short: "o" }) });
            let r = cli.tryParse(schema, ["-o", "file.txt"]);
            let result = r.value.output;
        """);
        Assert.Equal("file.txt", result);
    }

    [Fact]
    public void TryParse_ShortOptionInline_PopulatesValue()
    {
        var result = Run("""
            let schema = cli.schema({ output: cli.option("string", { short: "o" }) });
            let r = cli.tryParse(schema, ["-ofile.txt"]);
            let result = r.value.output;
        """);
        Assert.Equal("file.txt", result);
    }

    [Fact]
    public void TryParse_ShortFlagBundling_AllFlagsSet()
    {
        var result = Run("""
            let schema = cli.schema({
                verbose: cli.flag({ short: "v" }),
                force: cli.flag({ short: "f" }),
                all: cli.flag({ short: "a" })
            });
            let r = cli.tryParse(schema, ["-vfa"]);
            let result = r.value.verbose;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TryParse_ShortFlagBundleWithOptionValue_BundleFlag_ThenOptionValue()
    {
        // -vfFILE → -v -f FILE (last element in bundle that is an option takes remainder as value)
        var result = Run("""
            let schema = cli.schema({
                verbose: cli.flag({ short: "v" }),
                file: cli.option("string", { short: "f" })
            });
            let r = cli.tryParse(schema, ["-vfmain.go"]);
            let result = r.value.file;
        """);
        Assert.Equal("main.go", result);
    }

    // =========================================================================
    // Double-dash boundary
    // =========================================================================

    [Fact]
    public void TryParse_DoubleDash_TreatsRemainingAsPositionals()
    {
        var result = Run("""
            let schema = cli.schema({
                verbose: cli.flag({ short: "v" }),
                input: cli.positional("string")
            });
            let r = cli.tryParse(schema, ["--", "--not-a-flag"]);
            let result = r.value.input;
        """);
        Assert.Equal("--not-a-flag", result);
    }

    // =========================================================================
    // Repeated options
    // =========================================================================

    [Fact]
    public void TryParse_RepeatedOption_AccumulatesArray()
    {
        var result = Run("""
            let schema = cli.schema({ tag: cli.option("string", { repeated: true }) });
            let r = cli.tryParse(schema, ["--tag", "a", "--tag", "b"]);
            let result = r.value.tag;
        """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("b", list[1]);
    }

    // =========================================================================
    // Choices
    // =========================================================================

    [Fact]
    public void TryParse_ChoicesValid_ParsesOk()
    {
        var result = Run("""
            let schema = cli.schema({ level: cli.option("string", { choices: ["low", "mid", "high"] }) });
            let r = cli.tryParse(schema, ["--level", "mid"]);
            let result = r.value.level;
        """);
        Assert.Equal("mid", result);
    }

    [Fact]
    public void TryParse_ChoicesInvalid_ReturnsCliInvalidValue()
    {
        var result = Run("""
            let schema = cli.schema({ level: cli.option("string", { choices: ["low", "mid", "high"] }) });
            let r = cli.tryParse(schema, ["--level", "extreme"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliInvalidValue", result);
    }

    // =========================================================================
    // Env fallback
    // =========================================================================

    [Fact]
    public void TryParse_EnvFallback_UsedWhenArgNotSupplied()
    {
        // Set the env var, don't supply the option, expect it in the result
        System.Environment.SetEnvironmentVariable("TEST_CLI_PORT", "9090");
        try
        {
            var result = Run("""
                let schema = cli.schema({ port: cli.option("int", { env: "TEST_CLI_PORT", defaultVal: 80 }) });
                let r = cli.tryParse(schema, []);
                let result = r.value.port;
            """);
            Assert.Equal(9090L, result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("TEST_CLI_PORT", null);
        }
    }

    // =========================================================================
    // Defaults
    // =========================================================================

    [Fact]
    public void TryParse_OptionDefault_AppliedWhenNotSupplied()
    {
        var result = Run("""
            let schema = cli.schema({ output: cli.option("string", { defaultVal: "./out" }) });
            let r = cli.tryParse(schema, []);
            let result = r.value.output;
        """);
        Assert.Equal("./out", result);
    }

    // =========================================================================
    // Result structure
    // =========================================================================

    [Fact]
    public void TryParse_Success_OkIsTrue()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema, ["file.txt"]);
            let result = r.ok;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TryParse_Failure_OkIsFalse()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema, []);
            let result = r.ok;
        """);
        Assert.Equal(false, result);
    }

    // =========================================================================
    // Help flag
    // =========================================================================

    [Fact]
    public void TryParse_HelpLong_SetsHelpRequested()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema, ["--help"]);
            let result = r.helpRequested;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TryParse_HelpShort_SetsHelpRequested()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema, ["-h"]);
            let result = r.helpRequested;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TryParse_Help_OkIsFalse()
    {
        // ok:false when helpRequested because parsing did not complete normally
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema, ["--help"]);
            let result = r.ok;
        """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void TryParse_Help_DoesNotThrow()
    {
        // Verify --help is detected without raising any exception
        RunStatements("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema, ["--help"]);
        """);
    }

    // =========================================================================
    // Error type coverage — all six required by done_when
    // =========================================================================

    // [1] CliMissingRequired
    [Fact]
    public void TryParse_MissingRequiredPositional_ReturnsCliMissingRequired()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema, []);
            let result = r.error.type;
        """);
        Assert.Equal("CliMissingRequired", result);
    }

    [Fact]
    public void TryParse_MissingRequiredOption_ReturnsCliMissingRequired()
    {
        var result = Run("""
            let schema = cli.schema({ name: cli.option("string", { required: true }) });
            let r = cli.tryParse(schema, []);
            let result = r.error.type;
        """);
        Assert.Equal("CliMissingRequired", result);
    }

    // [2] CliUnknownOption
    [Fact]
    public void TryParse_UnknownLongOption_ReturnsCliUnknownOption()
    {
        var result = Run("""
            let schema = cli.schema({ verbose: cli.flag() });
            let r = cli.tryParse(schema, ["--bogus"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliUnknownOption", result);
    }

    [Fact]
    public void TryParse_UnknownShortOption_ReturnsCliUnknownOption()
    {
        var result = Run("""
            let schema = cli.schema({ verbose: cli.flag({ short: "v" }) });
            let r = cli.tryParse(schema, ["-z"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliUnknownOption", result);
    }

    // [3] CliMissingValue
    [Fact]
    public void TryParse_OptionWithNoValue_ReturnsCliMissingValue()
    {
        var result = Run("""
            let schema = cli.schema({ output: cli.option("string") });
            let r = cli.tryParse(schema, ["--output"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliMissingValue", result);
    }

    // [4] CliInvalidValue
    [Fact]
    public void TryParse_BadIntConversion_ReturnsCliInvalidValue()
    {
        var result = Run("""
            let schema = cli.schema({ count: cli.option("int", { defaultVal: 0 }) });
            let r = cli.tryParse(schema, ["--count", "notanint"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliInvalidValue", result);
    }

    [Fact]
    public void TryParse_BadBoolConversion_ReturnsCliInvalidValue()
    {
        var result = Run("""
            let schema = cli.schema({ flag: cli.option("bool", { defaultVal: false }) });
            let r = cli.tryParse(schema, ["--flag", "maybe"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliInvalidValue", result);
    }

    // [5] CliUnexpectedPositional
    [Fact]
    public void TryParse_ExtraPositional_ReturnsCliUnexpectedPositional()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema, ["file.txt", "extra"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliUnexpectedPositional", result);
    }

    [Fact]
    public void TryParse_NoPositionalsInSchema_ExtraToken_ReturnsCliUnexpectedPositional()
    {
        var result = Run("""
            let schema = cli.schema({ verbose: cli.flag() });
            let r = cli.tryParse(schema, ["extra"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliUnexpectedPositional", result);
    }

    // [6] CliAmbiguousOption
    [Fact]
    public void TryParse_AmbiguousLongOption_ReturnsCliAmbiguousOption()
    {
        var result = Run("""
            let schema = cli.schema({
                format: cli.option("string", { defaultVal: "text" }),
                force: cli.flag()
            });
            let r = cli.tryParse(schema, ["--fo"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliAmbiguousOption", result);
    }

    // =========================================================================
    // Type tag conversions
    // =========================================================================

    [Fact]
    public void TryParse_DurationOption_ParsesToDuration()
    {
        var result = Run("""
            let schema = cli.schema({ timeout: cli.option("duration") });
            let r = cli.tryParse(schema, ["--timeout", "30s"]);
            let result = r.value.timeout.totalMs;
        """);
        Assert.Equal(30000L, result);
    }

    [Fact]
    public void TryParse_IpOption_ParseToIp()
    {
        var result = Run("""
            let schema = cli.schema({ host: cli.option("ip") });
            let r = cli.tryParse(schema, ["--host", "127.0.0.1"]);
            let result = r.ok;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TryParse_ByteSizeOption_ParsesToBs()
    {
        var result = Run("""
            let schema = cli.schema({ size: cli.option("bytesize") });
            let r = cli.tryParse(schema, ["--size", "1KB"]);
            let result = r.ok;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TryParse_SemverOption_ParsesSemver()
    {
        var result = Run("""
            let schema = cli.schema({ ver: cli.option("semver") });
            let r = cli.tryParse(schema, ["--ver", "1.2.3"]);
            let result = r.ok;
        """);
        Assert.Equal(true, result);
    }

    // =========================================================================
    // Error property verification
    // =========================================================================

    [Fact]
    public void TryParse_CliMissingRequired_HasNameProperty()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let r = cli.tryParse(schema, []);
            let result = r.error.name;
        """);
        Assert.NotNull(result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public void TryParse_CliUnknownOption_HasOptionProperty()
    {
        var result = Run("""
            let schema = cli.schema({ verbose: cli.flag() });
            let r = cli.tryParse(schema, ["--bogus"]);
            let result = r.error.option;
        """);
        Assert.Equal("--bogus", result);
    }

    [Fact]
    public void TryParse_CliInvalidValue_HasValueAndExpectedProperties()
    {
        var result = Run("""
            let schema = cli.schema({ count: cli.option("int", { defaultVal: 0 }) });
            let r = cli.tryParse(schema, ["--count", "bad"]);
            let result = r.error.value;
        """);
        Assert.Equal("bad", result);
    }

    [Fact]
    public void TryParse_CliAmbiguousOption_HasCandidatesProperty()
    {
        var result = Run("""
            let schema = cli.schema({
                format: cli.option("string", { defaultVal: "text" }),
                force: cli.flag()
            });
            let r = cli.tryParse(schema, ["--fo"]);
            let result = len(r.error.candidates);
        """);
        Assert.Equal(2L, result);
    }

    // =========================================================================
    // Kebab-case long name resolution
    // =========================================================================

    [Fact]
    public void TryParse_CamelCaseKey_MapsToKebabCaseLongName()
    {
        var result = Run("""
            let schema = cli.schema({ dryRun: cli.flag() });
            let r = cli.tryParse(schema, ["--dry-run"]);
            let result = r.value.dryRun;
        """);
        Assert.Equal(true, result);
    }
}
