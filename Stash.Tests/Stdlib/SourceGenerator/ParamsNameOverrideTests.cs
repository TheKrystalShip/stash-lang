namespace Stash.Tests.Stdlib.SourceGenerator;

using System.Linq;
using Stash.Stdlib.Registration;
using Stash.Tests.Stdlib.SourceGenerator.Fixtures;
using Xunit;

/// <summary>
/// Regression tests for [StashParam(Name=...)] on variadic (params StashValue[]) parameters.
/// Before the fix, the name override was silently ignored and the C# parameter name was used.
/// </summary>
public class ParamsNameOverrideTests
{
    private static readonly NamespaceDefinition _defn = ParamsNameFixture.Define();

    [Fact]
    public void NoOverride_VariadicParam_UsesCSParamName()
    {
        var fn = _defn.Functions.First(f => f.Name == "noOverride");
        Assert.True(fn.IsVariadic);
        Assert.Single(fn.Parameters);
        Assert.Equal("rest", fn.Parameters[0].Name);
    }

    [Fact]
    public void WithOverride_VariadicParam_UsesAttributeName()
    {
        var fn = _defn.Functions.First(f => f.Name == "withOverride");
        Assert.True(fn.IsVariadic);
        Assert.Single(fn.Parameters);
        Assert.Equal("paths", fn.Parameters[0].Name);
    }
}
