namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Linq;
using Stash.Stdlib;
using Xunit;

public class ConvDocSnapshotTests
{
    private static readonly Stash.Stdlib.Registration.NamespaceDefinition Conv =
        StdlibDefinitions.Namespaces.First(n => n.Name == "conv");

    [Fact]
    public void ToStr_Documentation_MatchesPreMigration()
    {
        var fn = Conv.Functions.First(f => f.Name == "toStr");
        Assert.Equal("Converts any value to its string representation.\n@param value The value to convert\n@return The string representation of the value", fn.Documentation);
    }

    [Fact]
    public void ToInt_Documentation_MatchesPreMigration()
    {
        var fn = Conv.Functions.First(f => f.Name == "toInt");
        Assert.Equal("Parses a string or converts a number to an integer. Supports optional base (2, 8, 10, 16). Handles \"0x\", \"0b\", \"0o\" prefixes automatically. Floats are truncated.\n@param value A string or number to convert\n@param base The numeric base (optional, default 10; must be 2, 8, 10, or 16)\n@return The integer value", fn.Documentation);
    }

    [Fact]
    public void ToHex_Documentation_MatchesPreMigration()
    {
        var fn = Conv.Functions.First(f => f.Name == "toHex");
        Assert.Equal("Converts an integer to its hexadecimal string representation with optional zero-padding.\n@param n The integer to convert\n@param padding Minimum number of characters (optional, zero-pads if needed)\n@return The hexadecimal string (e.g., \"ff\")", fn.Documentation);
    }

    [Fact]
    public void FromBin_Documentation_MatchesPreMigration()
    {
        var fn = Conv.Functions.First(f => f.Name == "fromBin");
        Assert.Equal("Parses a binary string to an integer. Supports optional \"0b\" prefix.\n@param s The binary string to parse\n@return The parsed integer value", fn.Documentation);
    }

    [Fact]
    public void CharCode_Documentation_AndDeprecation_MatchPreMigration()
    {
        var fn = Conv.Functions.First(f => f.Name == "charCode");
        Assert.Equal("Deprecated. Use `str.charCode`. Returns the Unicode code point of the first character in the string.\n@param s A non-empty string\n@return The Unicode code point as an integer", fn.Documentation);
        Assert.Equal("str.charCode", fn.Deprecation?.ReplacementQualifiedName);
    }

    [Fact]
    public void Conv_HasExpectedFunctionCount()
    {
        Assert.Equal(13, Conv.Functions.Count);
    }
}
