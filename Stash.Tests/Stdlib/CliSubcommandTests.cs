namespace Stash.Tests.Stdlib;

using System.Collections.Generic;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for P4 of the cli-arg-parsing feature: subcommand parsing.
///
/// Covers:
///   - Single-level subcommand dispatch (result.command.name, result.command.path, result.command.values)
///   - Two-level subcommand dispatch (path: ["remote", "add"])
///   - Global flags at root carry through when appearing before or after the subcommand selector
///   - CliUnknownCommand with name and candidates populated
///   - Help flag at the subcommand position is recognised (helpRequested: true)
/// </summary>
public class CliSubcommandTests : StashTestBase
{
    // =========================================================================
    // One-level subcommand — basic dispatch
    // =========================================================================

    [Fact]
    public void Subcommand_SingleLevel_CommandKeyPresent()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["add", "--url", "https://example.com"]);
            let result = r.ok;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Subcommand_SingleLevel_CommandName()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["add", "--url", "https://example.com"]);
            let result = r.value.command.name;
        """);
        Assert.Equal("add", result);
    }

    [Fact]
    public void Subcommand_SingleLevel_CommandPath()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["add", "--url", "https://example.com"]);
            let result = r.value.command.path;
        """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        Assert.Equal("add", list[0]);
    }

    [Fact]
    public void Subcommand_SingleLevel_LeafValuePopulated()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["add", "--url", "https://example.com"]);
            let result = r.value.command.values.url;
        """);
        Assert.Equal("https://example.com", result);
    }

    [Fact]
    public void Subcommand_SingleLevel_CommandIsCliCommandInstance()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["add"]);
            let result = r.value.command;
        """);
        var inst = Assert.IsType<StashInstance>(result);
        Assert.Equal("CliCommand", inst.TypeName);
    }

    [Fact]
    public void Subcommand_LeafPositional_PopulatesInValues()
    {
        var result = Run("""
            let removeSchema = cli.schema({ name: cli.positional("string") });
            let schema = cli.schema({ remote: cli.command({ remove: removeSchema }) });
            let r = cli.tryParse(schema, ["remove", "origin"]);
            let result = r.value.command.values.name;
        """);
        Assert.Equal("origin", result);
    }

    [Fact]
    public void Subcommand_MultipleSubcommands_CorrectOneSelected()
    {
        var result = Run("""
            let addSchema    = cli.schema({ url: cli.option("string") });
            let removeSchema = cli.schema({ name: cli.positional("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema, remove: removeSchema }) });
            let r = cli.tryParse(schema, ["remove", "upstream"]);
            let result = r.value.command.name;
        """);
        Assert.Equal("remove", result);
    }

    // =========================================================================
    // Two-level subcommand dispatch
    // =========================================================================

    [Fact]
    public void Subcommand_TwoLevel_PathContainsBothLevels()
    {
        var result = Run("""
            let addSchema    = cli.schema({ url: cli.option("string") });
            let remoteSchema = cli.schema({ add: cli.command({ add: addSchema }) });
            let schema       = cli.schema({ verbose: cli.flag({ short: "v" }), remote: cli.command({ remote: remoteSchema }) });
            let r = cli.tryParse(schema, ["remote", "add", "--url", "git@github.com:x/y"]);
            let result = r.value.command.path;
        """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal("remote", list[0]);
        Assert.Equal("add", list[1]);
    }

    [Fact]
    public void Subcommand_TwoLevel_LeafValuePopulated()
    {
        var result = Run("""
            let addSchema    = cli.schema({ url: cli.option("string") });
            let remoteSchema = cli.schema({ add: cli.command({ add: addSchema }) });
            let schema       = cli.schema({ verbose: cli.flag({ short: "v" }), remote: cli.command({ remote: remoteSchema }) });
            let r = cli.tryParse(schema, ["remote", "add", "--url", "git@github.com:x/y"]);
            let result = r.value.command.values.url;
        """);
        Assert.Equal("git@github.com:x/y", result);
    }

    [Fact]
    public void Subcommand_TwoLevel_CommandNameIsLeaf()
    {
        var result = Run("""
            let addSchema    = cli.schema({ url: cli.option("string") });
            let remoteSchema = cli.schema({ add: cli.command({ add: addSchema }) });
            let schema       = cli.schema({ verbose: cli.flag({ short: "v" }), remote: cli.command({ remote: remoteSchema }) });
            let r = cli.tryParse(schema, ["remote", "add", "--url", "git@github.com:x/y"]);
            let result = r.value.command.name;
        """);
        Assert.Equal("add", result);
    }

    // =========================================================================
    // Global flag passthrough
    // =========================================================================

    [Fact]
    public void Subcommand_GlobalFlagBeforeSubcommand_CarriesThrough()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ verbose: cli.flag({ short: "v" }), remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["--verbose", "add", "--url", "x"]);
            let result = r.value.verbose;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Subcommand_GlobalFlagShortBeforeSubcommand_CarriesThrough()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ verbose: cli.flag({ short: "v" }), remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["-v", "add"]);
            let result = r.value.verbose;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Subcommand_GlobalFlagDefault_AppliedWhenNotSupplied()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ verbose: cli.flag({ short: "v" }), remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["add"]);
            let result = r.value.verbose;
        """);
        Assert.Equal(false, result);
    }

    // =========================================================================
    // CliUnknownCommand — name and candidates
    // =========================================================================

    [Fact]
    public void Subcommand_UnknownSubcommand_ReturnsCliUnknownCommand()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["badcmd"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliUnknownCommand", result);
    }

    [Fact]
    public void Subcommand_UnknownSubcommand_NamePropertySet()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["badcmd"]);
            let result = r.error.name;
        """);
        Assert.Equal("badcmd", result);
    }

    [Fact]
    public void Subcommand_UnknownSubcommand_CandidatesPopulated()
    {
        var result = Run("""
            let addSchema    = cli.schema({ url: cli.option("string") });
            let removeSchema = cli.schema({ name: cli.positional("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema, remove: removeSchema }) });
            let r = cli.tryParse(schema, ["badcmd"]);
            let result = len(r.error.candidates);
        """);
        // Either all candidates or matched prefix — at least 1
        Assert.True(result is long l && l >= 1L);
    }

    [Fact]
    public void Subcommand_UnknownSubcommand_PrefixMatchedInCandidates()
    {
        var result = Run("""
            let addSchema    = cli.schema({ url: cli.option("string") });
            let removeSchema = cli.schema({ name: cli.positional("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema, remove: removeSchema }) });
            let r = cli.tryParse(schema, ["ad"]);
            let result = r.error.candidates;
        """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Contains("add", list);
    }

    [Fact]
    public void Subcommand_OkFalse_WhenUnknownSubcommand()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["badcmd"]);
            let result = r.ok;
        """);
        Assert.Equal(false, result);
    }

    // =========================================================================
    // Help flag at subcommand position
    // =========================================================================

    [Fact]
    public void Subcommand_HelpFlagAtSubcommandPosition_SetsHelpRequested()
    {
        // --help before the subcommand selector is recognised as a root-level help request
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["--help"]);
            let result = r.helpRequested;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Subcommand_HelpFlagAfterSubcommandSelector_SetsHelpRequested()
    {
        // --help after subcommand selector is the leaf-level help request
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["add", "--help"]);
            let result = r.helpRequested;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Subcommand_HelpFlag_OkIsFalse()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["add", "--help"]);
            let result = r.ok;
        """);
        Assert.Equal(false, result);
    }

    // =========================================================================
    // Missing subcommand
    // =========================================================================

    [Fact]
    public void Subcommand_NoSubcommandProvided_ReturnsCliMissingRequired()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string") });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, []);
            let result = r.error.type;
        """);
        Assert.Equal("CliMissingRequired", result);
    }

    // =========================================================================
    // Leaf schema validation (missing required in leaf)
    // =========================================================================

    [Fact]
    public void Subcommand_LeafMissingRequired_ReturnsCliMissingRequired()
    {
        var result = Run("""
            let addSchema = cli.schema({ url: cli.option("string", { required: true }) });
            let schema = cli.schema({ remote: cli.command({ add: addSchema }) });
            let r = cli.tryParse(schema, ["add"]);
            let result = r.error.type;
        """);
        Assert.Equal("CliMissingRequired", result);
    }
}
