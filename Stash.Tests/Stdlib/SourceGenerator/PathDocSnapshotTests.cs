namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Linq;
using Stash.Stdlib;
using Xunit;

public class PathDocSnapshotTests
{
    private static readonly Stash.Stdlib.Registration.NamespaceDefinition Path =
        StdlibDefinitions.Namespaces.First(n => n.Name == "path");

    [Fact]
    public void Abs_Documentation_MatchesPreMigration()
    {
        var fn = Path.Functions.First(f => f.Name == "abs");
        Assert.Equal("Returns the absolute path for the given path string.\n@param p The path\n@return The absolute path", fn.Documentation);
    }

    [Fact]
    public void Join_Documentation_MatchesPreMigration()
    {
        var fn = Path.Functions.First(f => f.Name == "join");
        Assert.Equal("Joins two or more path segments using the platform path separator.\n@param a The first path segment\n@param b The second path segment\n@return The combined path", fn.Documentation);
    }

    [Fact]
    public void IsAbsolute_Documentation_MatchesPreMigration()
    {
        var fn = Path.Functions.First(f => f.Name == "isAbsolute");
        Assert.Equal("Returns true if the path is absolute, false otherwise.\n@param p The path\n@return Whether the path is absolute", fn.Documentation);
    }

    [Fact]
    public void Separator_Documentation_MatchesPreMigration()
    {
        var fn = Path.Functions.First(f => f.Name == "separator");
        Assert.Equal("Returns the platform-specific path separator character.\n@return The path separator (e.g. '/' on Linux/macOS, '\\' on Windows)", fn.Documentation);
    }

    [Fact]
    public void Path_HasExpectedFunctionCount()
    {
        Assert.Equal(11, Path.Functions.Count);
    }
}
