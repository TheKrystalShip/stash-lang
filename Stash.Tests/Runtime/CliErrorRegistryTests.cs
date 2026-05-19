namespace Stash.Tests.Runtime;

using System.Collections.Generic;
using Stash.Runtime.Errors;
using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for P2 of the cli-arg-parsing feature:
///   Nine [StashError] subclasses are registered in BuiltInErrorRegistry,
///   and CliSchemaError is thrown (with field/reason properties) when cli.schema() validation fails.
/// </summary>
public class CliErrorRegistryTests : StashTestBase
{
    // =========================================================================
    // BuiltInErrorRegistry registration — NameOf round-trips
    // =========================================================================

    [Fact]
    public void CliSchemaError_NameOf_ReturnsExpectedName()
    {
        var err = new CliSchemaError("test", "f", "r");
        Assert.Equal("CliSchemaError", BuiltInErrorRegistry.NameOf(err));
    }

    [Fact]
    public void CliMissingRequired_NameOf_ReturnsExpectedName()
    {
        var err = new CliMissingRequired("test", "name");
        Assert.Equal("CliMissingRequired", BuiltInErrorRegistry.NameOf(err));
    }

    [Fact]
    public void CliUnknownOption_NameOf_ReturnsExpectedName()
    {
        var err = new CliUnknownOption("test", "--foo");
        Assert.Equal("CliUnknownOption", BuiltInErrorRegistry.NameOf(err));
    }

    [Fact]
    public void CliMissingValue_NameOf_ReturnsExpectedName()
    {
        var err = new CliMissingValue("test", "--foo");
        Assert.Equal("CliMissingValue", BuiltInErrorRegistry.NameOf(err));
    }

    [Fact]
    public void CliInvalidValue_NameOf_ReturnsExpectedName()
    {
        var err = new CliInvalidValue("test", null, "bad", "int");
        Assert.Equal("CliInvalidValue", BuiltInErrorRegistry.NameOf(err));
    }

    [Fact]
    public void CliUnexpectedPositional_NameOf_ReturnsExpectedName()
    {
        var err = new CliUnexpectedPositional("test", "extra");
        Assert.Equal("CliUnexpectedPositional", BuiltInErrorRegistry.NameOf(err));
    }

    [Fact]
    public void CliAmbiguousOption_NameOf_ReturnsExpectedName()
    {
        var err = new CliAmbiguousOption("test", "--ver", new[] { "--verbose", "--version" });
        Assert.Equal("CliAmbiguousOption", BuiltInErrorRegistry.NameOf(err));
    }

    [Fact]
    public void CliValidationFailed_NameOf_ReturnsExpectedName()
    {
        var err = new CliValidationFailed("test", "--count", "must be positive");
        Assert.Equal("CliValidationFailed", BuiltInErrorRegistry.NameOf(err));
    }

    [Fact]
    public void CliUnknownCommand_NameOf_ReturnsExpectedName()
    {
        var err = new CliUnknownCommand("test", "foo", new[] { "add", "remove" });
        Assert.Equal("CliUnknownCommand", BuiltInErrorRegistry.NameOf(err));
    }

    // =========================================================================
    // CliSchemaError — end-to-end Stash catch: e.field / e.reason accessible
    // =========================================================================

    [Fact]
    public void CliSchemaError_DuplicateShort_CatchFieldProperty()
    {
        // Duplicate short option triggers CliSchemaError; e.field should be the offending property name.
        var result = Run("""
            let result = null;
            try {
                cli.schema({ output: cli.option("string", { short: "o" }), other: cli.option("string", { short: "o" }) });
            } catch (CliSchemaError e) {
                result = e.field;
            }
        """);
        Assert.NotNull(result);
        Assert.IsType<string>(result);
    }

    [Fact]
    public void CliSchemaError_DuplicateShort_CatchReasonProperty()
    {
        var result = Run("""
            let result = null;
            try {
                cli.schema({ output: cli.option("string", { short: "o" }), other: cli.option("string", { short: "o" }) });
            } catch (CliSchemaError e) {
                result = e.reason;
            }
        """);
        Assert.NotNull(result);
        Assert.IsType<string>(result);
        Assert.Contains("duplicate", (string)result!);
    }

    [Fact]
    public void CliSchemaError_ShadowHelp_TypeIsCliSchemaError()
    {
        var result = Run("""
            let result = null;
            try {
                cli.schema({ help: cli.flag() });
            } catch (CliSchemaError e) {
                result = e.type;
            }
        """);
        Assert.Equal("CliSchemaError", result);
    }

    [Fact]
    public void CliSchemaError_InvalidDefault_FieldAndReasonSet()
    {
        // Invalid default for int field
        var result = Run("""
            let capturedField = null;
            let capturedReason = null;
            try {
                cli.schema({ retries: cli.option("int", { default: "not-a-number" }) });
            } catch (CliSchemaError e) {
                capturedField = e.field;
                capturedReason = e.reason;
            }
            let result = capturedField;
        """);
        Assert.Equal("retries", result);
    }

    // =========================================================================
    // All nine errors are registered in BuiltInErrorRegistry
    // =========================================================================

    [Theory]
    [InlineData(typeof(CliSchemaError))]
    [InlineData(typeof(CliMissingRequired))]
    [InlineData(typeof(CliUnknownOption))]
    [InlineData(typeof(CliMissingValue))]
    [InlineData(typeof(CliInvalidValue))]
    [InlineData(typeof(CliUnexpectedPositional))]
    [InlineData(typeof(CliAmbiguousOption))]
    [InlineData(typeof(CliValidationFailed))]
    [InlineData(typeof(CliUnknownCommand))]
    public void AllNineCliErrors_RegisteredInBuiltInErrorRegistry(System.Type clrType)
    {
        bool found = BuiltInErrorRegistry.TryGetName(clrType, out string? name);
        Assert.True(found, $"{clrType.Name} not found in BuiltInErrorRegistry.");
        Assert.False(string.IsNullOrEmpty(name));
    }
}
