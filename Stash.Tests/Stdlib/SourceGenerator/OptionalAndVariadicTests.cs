namespace Stash.Tests.Stdlib.SourceGenerator;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using Stash.Tests.Stdlib.SourceGenerator.Fixtures;
using Xunit;

public class OptionalAndVariadicTests
{
    private static readonly NamespaceDefinition _defn = MarshalFixture.Define();

    private static BuiltInFunction Get(string name)
        => (BuiltInFunction)_defn.Namespace.GetAllMemberValues()[name].ToObject()!;

    [Fact]
    public void OptionalDefault_Missing_ReturnsDefault()
    {
        var r = Get("optionalDefault").CallDirect(null!, ReadOnlySpan<StashValue>.Empty);
        Assert.Equal(42L, r.AsInt);
    }

    [Fact]
    public void OptionalDefault_Provided_ReturnsValue()
    {
        var r = Get("optionalDefault").CallDirect(null!, new[] { StashValue.FromInt(7) });
        Assert.Equal(7L, r.AsInt);
    }

    [Fact]
    public void OptionalNullable_Missing_ReturnsDefaultText()
    {
        var r = Get("optionalNullable").CallDirect(null!, ReadOnlySpan<StashValue>.Empty);
        Assert.Equal("default", r.ToObject());
    }

    [Fact]
    public void OptionalNullable_Null_ReturnsDefaultText()
    {
        var r = Get("optionalNullable").CallDirect(null!, new[] { StashValue.Null });
        Assert.Equal("default", r.ToObject());
    }

    [Fact]
    public void OptionalNullable_Provided_ReturnsValue()
    {
        var r = Get("optionalNullable").CallDirect(null!, new[] { StashValue.FromObj("hi") });
        Assert.Equal("hi", r.ToObject());
    }

    [Fact]
    public void Variadic_Empty_ReturnsZero()
    {
        var r = Get("variadic").CallDirect(null!, ReadOnlySpan<StashValue>.Empty);
        Assert.Equal(0L, r.AsInt);
    }

    [Fact]
    public void Variadic_Three_ReturnsCount()
    {
        var r = Get("variadic").CallDirect(null!, new[]
        {
            StashValue.FromInt(1), StashValue.FromInt(2), StashValue.FromInt(3),
        });
        Assert.Equal(3L, r.AsInt);
    }

    [Fact]
    public void WithCtx_PassesContextAndArg()
    {
        var r = Get("withCtx").CallDirect(null!, new[] { StashValue.FromInt(10) });
        Assert.Equal(11L, r.AsInt);
    }

    [Fact]
    public void NamespaceMetadata_FunctionsRegistered()
    {
        // 16 fixture methods produce 16 namespace functions.
        Assert.Equal(16, _defn.Functions.Count);
    }

    [Fact]
    public void NamespaceMetadata_FunctionNamesAreCamelCase()
    {
        foreach (var fn in _defn.Functions)
        {
            Assert.False(string.IsNullOrEmpty(fn.Name));
            Assert.True(char.IsLower(fn.Name[0]), $"Function '{fn.Name}' should start with lowercase.");
        }
    }

    [Fact]
    public void NamespaceMetadata_NamespaceNameIsLowerCase()
    {
        Assert.Equal("marshalfixture", _defn.Name);
    }

    [Fact]
    public void NamespaceMetadata_VariadicFlagIsSet()
    {
        var variadic = Assert.Single(_defn.Functions, f => f.Name == "variadic");
        Assert.True(variadic.IsVariadic);
    }

    [Fact]
    public void NamespaceMetadata_NonVariadicFlagIsClear()
    {
        var fn = Assert.Single(_defn.Functions, f => f.Name == "longParam");
        Assert.False(fn.IsVariadic);
    }

    [Fact]
    public void NamespaceMetadata_DocumentationIsCaptured()
    {
        var fn = Assert.Single(_defn.Functions, f => f.Name == "longParam");
        Assert.NotNull(fn.Documentation);
        Assert.Contains("long it received", fn.Documentation!);
    }
}
