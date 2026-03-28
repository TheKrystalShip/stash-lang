using Stash.Interpreting;
using Stash.Runtime;
using Stash.Runtime.Types;
using Environment = Stash.Interpreting.Environment;

namespace Stash.Tests.Interpreting;

public class EnvironmentTests
{
    // ─── Define and Get ───────────────────────────────────────────

    [Fact]
    public void Define_And_Get_ReturnsValue()
    {
        var env = new Environment();
        env.Define("x", 42L);
        Assert.Equal(42L, env.Get("x"));
    }

    [Fact]
    public void Define_MultipleVariables_AllRetrievable()
    {
        var env = new Environment();
        env.Define("a", 1L);
        env.Define("b", "hello");
        env.Define("c", true);

        Assert.Equal(1L, env.Get("a"));
        Assert.Equal("hello", env.Get("b"));
        Assert.Equal(true, env.Get("c"));
    }

    [Fact]
    public void Get_UndefinedVariable_ThrowsRuntimeError()
    {
        var env = new Environment();
        var ex = Assert.Throws<RuntimeError>(() => { env.Get("missing"); });
        Assert.Contains("Undefined variable 'missing'", ex.Message);
    }

    [Fact]
    public void Define_NullValue_CanBeRetrieved()
    {
        var env = new Environment();
        env.Define("n", null);
        Assert.Null(env.Get("n"));
    }

    // ─── Scope Chain ──────────────────────────────────────────────

    [Fact]
    public void ChildScope_CanSeeParentVariable()
    {
        var parent = new Environment();
        parent.Define("x", 10L);

        var child = new Environment(parent);
        Assert.Equal(10L, child.Get("x"));
    }

    [Fact]
    public void ChildScope_VariableDoesNotLeakToParent()
    {
        var parent = new Environment();
        var child = new Environment(parent);
        child.Define("local", 99L);

        Assert.Throws<RuntimeError>(() => { parent.Get("local"); });
    }

    [Fact]
    public void DeeplyNestedScopes_InnerCanSeeOuterVariable()
    {
        var global = new Environment();
        global.Define("g", "global");

        var level1 = new Environment(global);
        level1.Define("l1", "one");

        var level2 = new Environment(level1);
        level2.Define("l2", "two");

        var level3 = new Environment(level2);

        Assert.Equal("global", level3.Get("g"));
        Assert.Equal("one", level3.Get("l1"));
        Assert.Equal("two", level3.Get("l2"));
    }

    [Fact]
    public void Enclosing_IsNull_ForGlobalScope()
    {
        var env = new Environment();
        Assert.Null(env.Enclosing);
    }

    [Fact]
    public void Enclosing_ReferencesParent_ForChildScope()
    {
        var parent = new Environment();
        var child = new Environment(parent);
        Assert.Same(parent, child.Enclosing);
    }

    // ─── Assign ───────────────────────────────────────────────────

    [Fact]
    public void Assign_ExistingVariable_UpdatesValue()
    {
        var env = new Environment();
        env.Define("x", 1L);
        env.Assign("x", 2L);
        Assert.Equal(2L, env.Get("x"));
    }

    [Fact]
    public void Assign_UndefinedVariable_ThrowsRuntimeError()
    {
        var env = new Environment();
        var ex = Assert.Throws<RuntimeError>(() => { env.Assign("missing", 0L); });
        Assert.Contains("Undefined variable 'missing'", ex.Message);
    }

    [Fact]
    public void Assign_WalksUpScopeChain()
    {
        var parent = new Environment();
        parent.Define("x", 1L);

        var child = new Environment(parent);
        child.Assign("x", 99L);

        Assert.Equal(99L, parent.Get("x"));
        Assert.Equal(99L, child.Get("x"));
    }

    // ─── Constants ────────────────────────────────────────────────

    [Fact]
    public void DefineConstant_CanBeRetrieved()
    {
        var env = new Environment();
        env.DefineConstant("PI", 3.14);
        Assert.Equal(3.14, env.Get("PI"));
    }

    [Fact]
    public void Assign_ToConstant_ThrowsRuntimeError()
    {
        var env = new Environment();
        env.DefineConstant("PI", 3.14);
        var ex = Assert.Throws<RuntimeError>(() => { env.Assign("PI", 0.0); });
        Assert.Contains("Cannot reassign constant 'PI'", ex.Message);
    }

    [Fact]
    public void AssignAt_ToConstant_ThrowsRuntimeError()
    {
        var env = new Environment();
        env.DefineConstant("PI", 3.14);
        var ex = Assert.Throws<RuntimeError>(() => { env.AssignAt(0, "PI", 0.0); });
        Assert.Contains("Cannot reassign constant 'PI'", ex.Message);
    }

    // ─── GetAt / AssignAt ─────────────────────────────────────────

    [Fact]
    public void GetAt_Distance0_ReturnsCurrentScopeValue()
    {
        var env = new Environment();
        env.Define("x", 42L);
        Assert.Equal(42L, env.GetAt(0, "x"));
    }

    [Fact]
    public void GetAt_Distance1_ReturnsParentScopeValue()
    {
        var parent = new Environment();
        parent.Define("x", "parent");

        var child = new Environment(parent);
        child.Define("y", "child");

        Assert.Equal("parent", child.GetAt(1, "x"));
    }

    [Fact]
    public void AssignAt_Distance0_UpdatesCurrentScope()
    {
        var env = new Environment();
        env.Define("x", 1L);
        env.AssignAt(0, "x", 2L);
        Assert.Equal(2L, env.Get("x"));
    }

    [Fact]
    public void AssignAt_Distance1_UpdatesParentScope()
    {
        var parent = new Environment();
        parent.Define("x", 1L);

        var child = new Environment(parent);
        child.AssignAt(1, "x", 99L);

        Assert.Equal(99L, parent.Get("x"));
    }

    // ─── Shadowing ────────────────────────────────────────────────

    [Fact]
    public void Shadow_ChildDefineSameNameAsParent_ChildSeesOwnValue()
    {
        var parent = new Environment();
        parent.Define("x", "parent");

        var child = new Environment(parent);
        child.Define("x", "child");

        Assert.Equal("child", child.Get("x"));
    }

    [Fact]
    public void Shadow_ParentValueUnchanged()
    {
        var parent = new Environment();
        parent.Define("x", "parent");

        var child = new Environment(parent);
        child.Define("x", "child");

        Assert.Equal("parent", parent.Get("x"));
    }
}
