namespace Stash.Tests.Stdlib;

using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;

/// <summary>
/// Tests for P1 of the cli-arg-parsing feature:
///   cli.positional, cli.option, cli.flag, cli.command, cli.schema.
///
/// All schema-time validation failures throw ValueError in P1 (rewired to CliSchemaError in P2).
///
/// Note: "default" is a reserved keyword in Stash; the CliArgSpec field is "defaultVal".
/// Note: Stash dict literals do not allow trailing commas; all test dicts use no trailing comma.
/// </summary>
public class CliSchemaBuilderTests : StashTestBase
{
    // =========================================================================
    // cli.positional
    // =========================================================================

    [Fact]
    public void CliPositional_StringType_ReturnsCliArgSpec()
    {
        var result = Run("""let result = cli.positional("string");""");
        Assert.NotNull(result);
        var inst = Assert.IsType<Stash.Runtime.Types.StashInstance>(result);
        Assert.Equal("CliArgSpec", inst.TypeName);
    }

    [Fact]
    public void CliPositional_AllTypeTagsAccepted()
    {
        // All eight v1 tags must succeed
        string[] tags = ["string", "int", "float", "bool", "duration", "ip", "bytesize", "semver"];
        foreach (string tag in tags)
        {
            var result = Run($"""let result = cli.positional("{tag}");""");
            Assert.NotNull(result);
        }
    }

    [Fact]
    public void CliPositional_UnknownTypeTag_ThrowsValueError()
    {
        var ex = RunCapturingError("""cli.positional("path");""");
        Assert.IsType<ValueError>(ex);
    }

    [Fact]
    public void CliPositional_WithOptions_SetsFields()
    {
        var result = Run("""let result = cli.positional("string", { required: false, help: "Input file" });""");
        var inst = Assert.IsType<Stash.Runtime.Types.StashInstance>(result);
        Assert.Equal("CliArgSpec", inst.TypeName);
    }

    [Fact]
    public void CliPositional_DefaultRequired_IsTrue()
    {
        var result = Run("""
            let spec = cli.positional("int");
            let result = spec.required;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void CliPositional_KindIsPositional()
    {
        var result = Run("""
            let spec = cli.positional("string");
            let result = spec.kind;
        """);
        Assert.Equal("positional", result);
    }

    // =========================================================================
    // cli.option
    // =========================================================================

    [Fact]
    public void CliOption_StringType_ReturnsCliArgSpec()
    {
        var result = Run("""let result = cli.option("string");""");
        Assert.NotNull(result);
        var inst = Assert.IsType<Stash.Runtime.Types.StashInstance>(result);
        Assert.Equal("CliArgSpec", inst.TypeName);
    }

    [Fact]
    public void CliOption_UnknownTypeTag_ThrowsValueError()
    {
        var ex = RunCapturingError("""cli.option("notatype");""");
        Assert.IsType<ValueError>(ex);
    }

    [Fact]
    public void CliOption_WithShort_SetsShortField()
    {
        var result = Run("""
            let spec = cli.option("string", { short: "o" });
            let result = spec.short;
        """);
        Assert.Equal("o", result);
    }

    [Fact]
    public void CliOption_DefaultRequired_IsFalse()
    {
        var result = Run("""
            let spec = cli.option("int");
            let result = spec.required;
        """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void CliOption_KindIsOption()
    {
        var result = Run("""
            let spec = cli.option("string");
            let result = spec.kind;
        """);
        Assert.Equal("option", result);
    }

    // =========================================================================
    // cli.flag
    // =========================================================================

    [Fact]
    public void CliFlag_NoArgs_ReturnsCliArgSpec()
    {
        var result = Run("""let result = cli.flag();""");
        Assert.NotNull(result);
        var inst = Assert.IsType<Stash.Runtime.Types.StashInstance>(result);
        Assert.Equal("CliArgSpec", inst.TypeName);
    }

    [Fact]
    public void CliFlag_KindIsFlag()
    {
        var result = Run("""
            let spec = cli.flag();
            let result = spec.kind;
        """);
        Assert.Equal("flag", result);
    }

    [Fact]
    public void CliFlag_DefaultIsFalse()
    {
        // Note: "default" is a reserved keyword in Stash; the field is named "defaultVal".
        var result = Run("""
            let spec = cli.flag();
            let result = spec.defaultVal;
        """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void CliFlag_WithShort_SetsShortField()
    {
        var result = Run("""
            let spec = cli.flag({ short: "v" });
            let result = spec.short;
        """);
        Assert.Equal("v", result);
    }

    // =========================================================================
    // cli.schema — success cases
    // =========================================================================

    [Fact]
    public void CliSchema_EmptyDefinition_ReturnsCliSchema()
    {
        var result = Run("""let result = cli.schema({});""");
        Assert.NotNull(result);
        var inst = Assert.IsType<Stash.Runtime.Types.StashInstance>(result);
        Assert.Equal("CliSchema", inst.TypeName);
    }

    [Fact]
    public void CliSchema_SingleOption_Succeeds()
    {
        var result = Run("""let result = cli.schema({ output: cli.option("string") });""");
        Assert.NotNull(result);
        var inst = Assert.IsType<Stash.Runtime.Types.StashInstance>(result);
        Assert.Equal("CliSchema", inst.TypeName);
    }

    [Fact]
    public void CliSchema_SinglePositional_Succeeds()
    {
        var result = Run("""let result = cli.schema({ input: cli.positional("string") });""");
        Assert.NotNull(result);
        var inst = Assert.IsType<Stash.Runtime.Types.StashInstance>(result);
        Assert.Equal("CliSchema", inst.TypeName);
    }

    [Fact]
    public void CliSchema_FlagOption_Succeeds()
    {
        var result = Run("""let result = cli.schema({ verbose: cli.flag() });""");
        Assert.NotNull(result);
    }

    [Fact]
    public void CliSchema_MixedArgs_Succeeds()
    {
        // No trailing commas in Stash dict literals
        var result = Run("""let result = cli.schema({ input: cli.positional("string"), output: cli.option("string", { short: "o" }), verbose: cli.flag({ short: "v" }), retries: cli.option("int") });""");
        Assert.NotNull(result);
        var inst = Assert.IsType<Stash.Runtime.Types.StashInstance>(result);
        Assert.Equal("CliSchema", inst.TypeName);
    }

    [Fact]
    public void CliSchema_KebabCaseNameFromPropertyName()
    {
        // dryRun → --dry-run; the schema stores the resolved long name on each spec
        var result = Run("""
            let schema = cli.schema({ dryRun: cli.flag() });
            let specs  = schema.options;
            let spec   = specs["dryRun"];
            let result = spec.name;
        """);
        Assert.Equal("dry-run", result);
    }

    [Fact]
    public void CliSchema_HelpFlagDefaultTrue()
    {
        var result = Run("""
            let schema = cli.schema({});
            let result = schema.helpFlag;
        """);
        Assert.Equal(true, result);
    }

    // =========================================================================
    // cli.schema — validation failures
    // =========================================================================

    [Fact]
    public void CliSchema_DuplicateShortOption_ThrowsValueError()
    {
        var ex = RunCapturingError("""cli.schema({ output: cli.option("string", { short: "o" }), other: cli.option("string", { short: "o" }) });""");
        Assert.IsType<ValueError>(ex);
        Assert.Contains("-o", ex.Message);
    }

    [Fact]
    public void CliSchema_DuplicateLongOption_ThrowsValueError()
    {
        var ex = RunCapturingError("""cli.schema({ output: cli.option("string", { name: "out" }), outputDir: cli.option("string", { name: "out" }) });""");
        Assert.IsType<ValueError>(ex);
        Assert.Contains("--out", ex.Message);
    }

    [Fact]
    public void CliSchema_RepeatedPositionalNotLast_ThrowsValueError()
    {
        var ex = RunCapturingError("""cli.schema({ files: cli.positional("string", { repeated: true }), output: cli.positional("string") });""");
        Assert.IsType<ValueError>(ex);
        Assert.Contains("repeated", ex.Message);
    }

    [Fact]
    public void CliSchema_RepeatedPositionalAsLast_Succeeds()
    {
        // repeated: true is allowed when it is the last positional
        var result = Run("""let result = cli.schema({ output: cli.positional("string"), files: cli.positional("string", { repeated: true }) });""");
        Assert.NotNull(result);
    }

    [Fact]
    public void CliSchema_UnknownTypeTagInOption_ThrowsValueError()
    {
        // The type-tag validation fires in cli.option, surfacing before cli.schema
        var ex = RunCapturingError("""cli.schema({ output: cli.option("notatype") });""");
        Assert.IsType<ValueError>(ex);
    }

    [Fact]
    public void CliSchema_DefaultFailsConversionForInt_ThrowsValueError()
    {
        // Pass "default" key in the options dict (dict key, not field accessor — valid in Stash)
        var ex = RunCapturingError("""cli.schema({ retries: cli.option("int", { default: "not-a-number" }) });""");
        Assert.IsType<ValueError>(ex);
    }

    [Fact]
    public void CliSchema_DefaultFailsConversionForBool_ThrowsValueError()
    {
        var ex = RunCapturingError("""cli.schema({ verbose: cli.flag({ default: "maybe" }) });""");
        Assert.IsType<ValueError>(ex);
    }

    [Fact]
    public void CliSchema_ValidDefaultForInt_Succeeds()
    {
        var result = Run("""let result = cli.schema({ retries: cli.option("int", { default: 3 }) });""");
        Assert.NotNull(result);
    }

    [Fact]
    public void CliSchema_ValidDefaultForString_Succeeds()
    {
        var result = Run("""let result = cli.schema({ output: cli.option("string", { default: "./out" }) });""");
        Assert.NotNull(result);
    }

    [Fact]
    public void CliSchema_ShadowHelpLong_ThrowsValueError()
    {
        var ex = RunCapturingError("""cli.schema({ help: cli.flag() });""");
        Assert.IsType<ValueError>(ex);
        Assert.Contains("--help", ex.Message);
    }

    [Fact]
    public void CliSchema_ShadowHelpShort_ThrowsValueError()
    {
        var ex = RunCapturingError("""cli.schema({ something: cli.flag({ short: "h" }) });""");
        Assert.IsType<ValueError>(ex);
        Assert.Contains("-h", ex.Message);
    }

    [Fact]
    public void CliSchema_HelpFlagDisabled_AllowsShadowing()
    {
        // When helpFlag is false, --help and -h are not reserved
        var result = Run("""let result = cli.schema({ something: cli.flag({ short: "h" }) }, { helpFlag: false });""");
        Assert.NotNull(result);
    }

    // =========================================================================
    // cli.command
    // =========================================================================

    [Fact]
    public void CliCommand_ValidSubcommands_ReturnsCliCommandSpec()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let result = cli.command({ add: addSchema });
        """);
        Assert.NotNull(result);
        var inst = Assert.IsType<Stash.Runtime.Types.StashInstance>(result);
        Assert.Equal("CliCommandSpec", inst.TypeName);
    }

    [Fact]
    public void CliCommand_NonSchemaValue_ThrowsTypeError()
    {
        var ex = RunCapturingError("""cli.command({ add: "notaschema" });""");
        Assert.IsType<TypeError>(ex);
    }

    [Fact]
    public void CliSchema_WithCommand_Succeeds()
    {
        var result = Run("""
            let addSchema    = cli.schema({ url: cli.option("string") });
            let removeSchema = cli.schema({ name: cli.positional("string") });
            let result = cli.schema({ verbose: cli.flag({ short: "v" }), remote: cli.command({ add: addSchema, remove: removeSchema }) });
        """);
        Assert.NotNull(result);
        var inst = Assert.IsType<Stash.Runtime.Types.StashInstance>(result);
        Assert.Equal("CliSchema", inst.TypeName);
    }

    // =========================================================================
    // Struct field accessibility
    // =========================================================================

    [Fact]
    public void CliArgSpec_FieldsAccessible_AfterSchema()
    {
        var result = Run("""
            let schema = cli.schema({ output: cli.option("string", { short: "o", help: "Output path" }) });
            let spec = schema.options["output"];
            let result = spec.kind;
        """);
        Assert.Equal("option", result);
    }

    [Fact]
    public void CliSchema_PositionalsList_ContainsSpec()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string") });
            let result = len(schema.positionals);
        """);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void CliSchema_OptionsDict_ContainsSpec()
    {
        var result = Run("""
            let schema = cli.schema({ input: cli.positional("string"), output: cli.option("string") });
            let result = len(schema.options);
        """);
        Assert.Equal(1L, result);
    }
}
