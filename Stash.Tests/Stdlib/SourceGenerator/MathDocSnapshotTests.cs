namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Linq;
using Stash.Stdlib;
using Xunit;

public class MathDocSnapshotTests
{
    private static readonly Stash.Stdlib.Registration.NamespaceDefinition Math =
        StdlibDefinitions.Namespaces.First(n => n.Name == "math");

    [Fact]
    public void Abs_Documentation_MatchesPreMigration()
    {
        var fn = Math.Functions.First(f => f.Name == "abs");
        Assert.Equal("Returns the absolute value of a number.\n@param n The number\n@return The absolute value", fn.Documentation);
    }

    [Fact]
    public void Round_Documentation_MatchesPreMigration()
    {
        var fn = Math.Functions.First(f => f.Name == "round");
        Assert.Equal("Rounds a number to the nearest integer, or to a specified number of decimal places. Ties round away from zero.\n@param n The number to round\n@param precision (optional) Number of decimal places (positive) or significant digits to the left of the decimal (negative). Defaults to 0\n@return The rounded value", fn.Documentation);
    }

    [Fact]
    public void Min_Documentation_MatchesPreMigration()
    {
        var fn = Math.Functions.First(f => f.Name == "min");
        Assert.Equal("Returns the smallest of two or more numbers.\n@param a The first number\n@param b The second number\n@param args Additional numbers to compare\n@return The smallest value", fn.Documentation);
    }

    [Fact]
    public void PI_Documentation_MatchesPreMigration()
    {
        var c = Math.Constants.First(k => k.Name == "PI");
        Assert.Equal("The ratio of a circle's circumference to its diameter (π ≈ 3.14159).", c.Documentation);
        Assert.Equal("3.141592653589793", c.Value);
    }

    [Fact]
    public void Math_HasExpectedFunctionCount()
    {
        Assert.Equal(23, Math.Functions.Count);
    }
}
