namespace Stash.Tests.Stdlib;

using Stash.Runtime;
using Stash.Stdlib.BuiltIns;
using Stash.Tests.Interpreting;

/// <summary>
/// Tests for the <c>alias</c> namespace — Phase A: stdlib core only.
/// Covers define, list, names, get, exists, remove, clear, exec, expand, save, load.
/// </summary>
public class AliasBuiltInsTests : StashTestBase
{
    // =========================================================================
    // alias.define + alias.list
    // =========================================================================

    [Fact]
    public void Define_Template_ListReturnsOneEntry()
    {
        var result = Run("""
            alias.define("g", "git \${args}");
            let list = alias.list();
            let result = len(list);
            """);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Define_Function_ListReturnsOneEntry()
    {
        var result = Run("""
            alias.define("myfn", () => null);
            let list = alias.list();
            let result = len(list);
            """);
        Assert.Equal(1L, result);
    }

    // =========================================================================
    // alias.get
    // =========================================================================

    [Fact]
    public void Get_ExistingTemplate_ReturnsAliasInfoWithCorrectFields()
    {
        RunStatements("""
            alias.define("g", "git \${args}");
            let info = alias.get("g");
            assert.equal(info.name, "g");
            assert.equal(info.kind, "template");
            assert.equal(info.body, "git \${args}");
            assert.equal(info.source, "repl");
            """);
    }

    [Fact]
    public void Get_ExistingFunction_ReturnsAliasInfoWithFunctionKind()
    {
        RunStatements("""
            alias.define("myfn", () => null);
            let info = alias.get("myfn");
            assert.equal(info.name, "myfn");
            assert.equal(info.kind, "function");
            """);
    }

    [Fact]
    public void Get_NonExistingName_ReturnsNull()
    {
        var result = Run("""
            let result = alias.get("doesnotexist");
            """);
        Assert.Null(result);
    }

    // =========================================================================
    // alias.names
    // =========================================================================

    [Fact]
    public void Names_AfterDefiningTwo_ReturnsBothSorted()
    {
        var result = Run("""
            alias.define("zz", "echo z");
            alias.define("aa", "echo a");
            let names = alias.names();
            let result = names[0];
            """);
        Assert.Equal("aa", result);
    }

    // =========================================================================
    // alias.exists
    // =========================================================================

    [Fact]
    public void Exists_AfterDefine_ReturnsTrue()
    {
        var result = Run("""
            alias.define("g", "git");
            let result = alias.exists("g");
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Exists_NonExisting_ReturnsFalse()
    {
        var result = Run("""
            let result = alias.exists("nothere");
            """);
        Assert.Equal(false, result);
    }

    // =========================================================================
    // alias.remove
    // =========================================================================

    [Fact]
    public void Remove_ExistingAlias_ReturnsTrue()
    {
        var result = Run("""
            alias.define("g", "git");
            let result = alias.remove("g");
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Remove_SecondRemove_ReturnsFalse()
    {
        var result = Run("""
            alias.define("g", "git");
            alias.remove("g");
            let result = alias.remove("g");
            """);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Remove_AfterRemove_ExistsReturnsFalse()
    {
        var result = Run("""
            alias.define("g", "git");
            alias.remove("g");
            let result = alias.exists("g");
            """);
        Assert.Equal(false, result);
    }

    // =========================================================================
    // alias.clear
    // =========================================================================

    [Fact]
    public void Clear_TwoAliasesDefined_ReturnsTwo()
    {
        var result = Run("""
            alias.define("a", "echo a");
            alias.define("b", "echo b");
            let result = alias.clear();
            """);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void Clear_AfterClear_ListIsEmpty()
    {
        var result = Run("""
            alias.define("a", "echo a");
            alias.define("b", "echo b");
            alias.clear();
            let list = alias.list();
            let result = len(list);
            """);
        Assert.Equal(0L, result);
    }

    // =========================================================================
    // last-wins override
    // =========================================================================

    [Fact]
    public void Define_SameName_OverridesPreviousDefinition()
    {
        var result = Run("""
            alias.define("g", "git");
            alias.define("g", "gh");
            let info = alias.get("g");
            let result = info.body;
            """);
        Assert.Equal("gh", result);
    }

    // =========================================================================
    // name validation
    // =========================================================================

    [Fact]
    public void Define_InvalidName_DotInName_ThrowsAliasError()
    {
        var err = RunCapturingError("""
            alias.define("g.s", "git status");
            """);
        Assert.Equal(StashErrorTypes.AliasError, err.ErrorType);
    }

    [Fact]
    public void Define_EmptyName_ThrowsAliasError()
    {
        var err = RunCapturingError("""
            alias.define("", "git");
            """);
        Assert.Equal(StashErrorTypes.AliasError, err.ErrorType);
    }

    [Fact]
    public void Define_NameWithSpace_ThrowsAliasError()
    {
        var err = RunCapturingError("""
            alias.define("my alias", "git");
            """);
        Assert.Equal(StashErrorTypes.AliasError, err.ErrorType);
    }

    [Fact]
    public void Define_NameStartingWithDigit_ThrowsAliasError()
    {
        var err = RunCapturingError("""
            alias.define("1g", "git");
            """);
        Assert.Equal(StashErrorTypes.AliasError, err.ErrorType);
    }

    // =========================================================================
    // alias.expand — template substitution
    // =========================================================================

    [Fact]
    public void Expand_ArgsPlaceholder_ShellQuotesAndJoins()
    {
        var result = Run("""
            alias.define("g", "git \${args}");
            let result = alias.expand("g", ["foo bar", "baz"]);
            """);
        Assert.Equal("git 'foo bar' baz", result);
    }

    [Fact]
    public void Expand_ArgsPlaceholder_SafeArgs_NoQuotes()
    {
        var result = Run("""
            alias.define("g", "git \${args}");
            let result = alias.expand("g", ["status", "--short"]);
            """);
        Assert.Equal("git status --short", result);
    }

    [Fact]
    public void Expand_ArgsIndexPlaceholder_SubstitutesCorrectArg()
    {
        var result = Run("""
            alias.define("gco", "git checkout \${args[0]}");
            let result = alias.expand("gco", ["main"]);
            """);
        Assert.Equal("git checkout main", result);
    }

    [Fact]
    public void Expand_ArgsIndexOutOfRange_ThrowsAliasError()
    {
        var err = RunCapturingError("""
            alias.define("gco", "git checkout \${args[5]}");
            alias.expand("gco", []);
            """);
        Assert.Equal(StashErrorTypes.AliasError, err.ErrorType);
    }

    [Fact]
    public void Expand_ArgvPlaceholder_ProducesStashArrayLiteral()
    {
        var result = Run("""
            alias.define("g", "fn.call(\${argv})");
            let result = alias.expand("g", ["a", "b"]);
            """);
        Assert.Equal("""fn.call(["a", "b"])""", result);
    }

    [Fact]
    public void Expand_OtherInterpolations_PassedThrough()
    {
        var result = Run("""
            alias.define("g", "echo \${HOME} \${args}");
            let result = alias.expand("g", ["hi"]);
            """);
        Assert.Equal("echo ${HOME} hi", result);
    }

    [Fact]
    public void Expand_FunctionAlias_ReturnsPlaceholderString()
    {
        var result = Run("""
            alias.define("myfn", () => null);
            let result = alias.expand("myfn", []);
            """);
        Assert.Equal("<function alias `myfn`>", result);
    }

    [Fact]
    public void Expand_MissingAlias_ThrowsAliasError()
    {
        var err = RunCapturingError("""
            alias.expand("nonexistent", []);
            """);
        Assert.Equal(StashErrorTypes.AliasError, err.ErrorType);
    }

    // =========================================================================
    // alias.exec — function alias invocation
    // =========================================================================

    [Fact]
    public void Exec_FunctionAlias_InvokesTheLambda()
    {
        // The lambda sets a global that we read back to verify it ran.
        var result = Run("""
            let called = false;
            alias.define("setcalled", () => { called = true; });
            alias.exec("setcalled", []);
            let result = called;
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Exec_TemplateAlias_WhenExecutorNull_ThrowsAliasError()
    {
        // Explicitly null the executor so this test is order-independent.
        // Other test classes (AliasDispatchTests, AliasShellSugarTests) call
        // AliasDispatcher.Wire which sets the static AliasExecutor; we clear it
        // here to test the embedded-mode guard.
        var savedExecutor = AliasBuiltIns.AliasExecutor;
        try
        {
            AliasBuiltIns.AliasExecutor = null;

            // AliasExecutor is null — template exec must throw AliasError.
            var err = RunCapturingError("""
                alias.define("g", "git \${args}");
                alias.exec("g", ["status"]);
                """);
            Assert.Equal(StashErrorTypes.AliasError, err.ErrorType);
        }
        finally
        {
            AliasBuiltIns.AliasExecutor = savedExecutor;
        }
    }

    [Fact]
    public void Exec_MissingAlias_ThrowsAliasError()
    {
        var err = RunCapturingError("""
            alias.exec("notdefined", []);
            """);
        Assert.Equal(StashErrorTypes.AliasError, err.ErrorType);
    }

    // =========================================================================
    // alias.save / alias.load — Phase F stubs
    // =========================================================================

    [Fact]
    public void Save_ThrowsNotSupportedError()
    {
        var err = RunCapturingError("""
            alias.save();
            """);
        Assert.Equal(StashErrorTypes.NotSupportedError, err.ErrorType);
    }

    [Fact]
    public void Load_ThrowsNotSupportedError()
    {
        var err = RunCapturingError("""
            alias.load();
            """);
        Assert.Equal(StashErrorTypes.NotSupportedError, err.ErrorType);
    }
}
