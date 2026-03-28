using Environment = Stash.Interpreting.Environment;

namespace Stash.Tests.Interpreting;

public class EnvironmentDebugTests
{
    [Fact]
    public void TryGet_ExistingVariable_ReturnsTrue()
    {
        var env = new Environment();
        env.Define("x", 42L);

        bool found = env.TryGet("x", out object? value);

        Assert.True(found);
        Assert.Equal(42L, value);
    }

    [Fact]
    public void TryGet_MissingVariable_ReturnsFalse()
    {
        var env = new Environment();

        bool found = env.TryGet("x", out object? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGet_DoesNotWalkChain()
    {
        var parent = new Environment();
        parent.Define("x", 42L);
        var child = new Environment(parent);

        bool found = child.TryGet("x", out _);

        Assert.False(found); // TryGet only checks current scope
    }

    [Fact]
    public void Contains_ExistingVariable_ReturnsTrue()
    {
        var env = new Environment();
        env.Define("x", 42L);

        Assert.True(env.Contains("x"));
    }

    [Fact]
    public void Contains_MissingVariable_ReturnsFalse()
    {
        var env = new Environment();

        Assert.False(env.Contains("x"));
    }

    [Fact]
    public void Contains_DoesNotWalkChain()
    {
        var parent = new Environment();
        parent.Define("x", 42L);
        var child = new Environment(parent);

        Assert.False(child.Contains("x"));
    }

    [Fact]
    public void IsConstant_ConstantVariable_ReturnsTrue()
    {
        var env = new Environment();
        env.DefineConstant("PI", 3.14);

        Assert.True(env.IsConstant("PI"));
    }

    [Fact]
    public void IsConstant_MutableVariable_ReturnsFalse()
    {
        var env = new Environment();
        env.Define("x", 42L);

        Assert.False(env.IsConstant("x"));
    }

    [Fact]
    public void IsConstant_UndefinedVariable_ReturnsFalse()
    {
        var env = new Environment();

        Assert.False(env.IsConstant("x"));
    }

    [Fact]
    public void GetScopeChain_SingleScope_ReturnsOne()
    {
        var env = new Environment();
        env.Define("x", 1L);

        var chain = env.GetScopeChain().ToList();

        Assert.Single(chain);
        Assert.Same(env, chain[0]);
    }

    [Fact]
    public void GetScopeChain_NestedScopes_ReturnsAllInOrder()
    {
        var global = new Environment();
        global.Define("g", 1L);
        var middle = new Environment(global);
        middle.Define("m", 2L);
        var local = new Environment(middle);
        local.Define("l", 3L);

        var chain = local.GetScopeChain().ToList();

        Assert.Equal(3, chain.Count);
        Assert.Same(local, chain[0]);
        Assert.Same(middle, chain[1]);
        Assert.Same(global, chain[2]);
    }

    [Fact]
    public void GetScopeChain_CanEnumerateAllVariables()
    {
        var global = new Environment();
        global.Define("g", 1L);
        var local = new Environment(global);
        local.Define("l", 2L);

        var allVars = local.GetScopeChain()
            .SelectMany(scope => scope.GetAllBindings())
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(2, allVars.Count);
        Assert.Equal(2L, allVars["l"]);
        Assert.Equal(1L, allVars["g"]);
    }

    [Fact]
    public void TryGet_NullValue_ReturnsTrueWithNull()
    {
        var env = new Environment();
        env.Define("x", null);

        bool found = env.TryGet("x", out object? value);

        Assert.True(found);
        Assert.Null(value);
    }
}
