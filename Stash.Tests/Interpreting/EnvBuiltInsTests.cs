namespace Stash.Tests.Interpreting;

public class EnvBuiltInsTests : StashTestBase
{
    // ── env.get with default ──────────────────────────────────────────────────

    [Fact]
    public void Get_WithDefault_ReturnsDefaultWhenMissing()
    {
        Assert.Equal("fallback", Run("let result = env.get(\"STASH_TEST_NONEXISTENT_VAR_99\", \"fallback\");"));
    }

    [Fact]
    public void Get_WithDefault_ReturnsValueWhenExists()
    {
        Assert.Equal("hello", Run("env.set(\"STASH_TEST_DEFAULT_VAR\", \"hello\"); let result = env.get(\"STASH_TEST_DEFAULT_VAR\", \"fallback\");"));
    }

    [Fact]
    public void Get_NoDefault_ExistingBehavior()
    {
        Assert.Null(Run("let result = env.get(\"STASH_TEST_NONEXISTENT_VAR_99\");"));
    }

    // ── env.set ──────────────────────────────────────────────────────────────

    [Fact]
    public void Set_Variable_StoresValue()
    {
        var result = Run("env.set(\"STASH_SET_TEST\", \"stored\"); let result = env.get(\"STASH_SET_TEST\");");
        Assert.Equal("stored", result);
    }

    [Fact]
    public void Set_EmptyStringValue_CanBeRead()
    {
        var result = Run("env.set(\"STASH_EMPTY_VAL\", \"\"); let result = env.get(\"STASH_EMPTY_VAL\");");
        Assert.Equal("", result);
    }

    // ── env.has ──────────────────────────────────────────────────────────────

    [Fact]
    public void Has_ExistingVariable_ReturnsTrue()
    {
        var result = Run("env.set(\"STASH_HAS_TEST\", \"yes\"); let result = env.has(\"STASH_HAS_TEST\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Has_NonExistingVariable_ReturnsFalse()
    {
        var result = Run("let result = env.has(\"STASH_TEST_NONEXISTENT_VAR_42\");");
        Assert.Equal(false, result);
    }

    // ── env.remove ───────────────────────────────────────────────────────────

    [Fact]
    public void Remove_ExistingVariable_RemovesIt()
    {
        var result = Run(
            "env.set(\"STASH_REMOVE_TEST\", \"bye\");" +
            "env.remove(\"STASH_REMOVE_TEST\");" +
            "let result = env.has(\"STASH_REMOVE_TEST\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Remove_NonExistingVariable_DoesNotThrow()
    {
        RunStatements("env.remove(\"STASH_NEVER_SET_XYZ\");");
    }

    // ── env.all ──────────────────────────────────────────────────────────────

    [Fact]
    public void All_ReturnsDict()
    {
        var result = Run("let result = env.all();");
        Assert.IsType<Stash.Runtime.Types.StashDictionary>(result);
    }

    [Fact]
    public void All_ContainsSetVariable()
    {
        var result = Run(
            "env.set(\"STASH_ALL_TEST\", \"present\");" +
            "let all = env.all();" +
            "let result = all[\"STASH_ALL_TEST\"];");
        Assert.Equal("present", result);
    }

    // ── env.withPrefix ────────────────────────────────────────────────────────

    [Fact]
    public void WithPrefix_FiltersByPrefix()
    {
        var result = Run(
            "env.set(\"STASH_PFX_A\", \"1\");" +
            "env.set(\"STASH_PFX_B\", \"2\");" +
            "let d = env.withPrefix(\"STASH_PFX_\");" +
            "let result = len(d);");
        Assert.True((long)result! >= 2);
    }

    [Fact]
    public void WithPrefix_NoMatches_ReturnsEmptyDict()
    {
        var result = Run("let d = env.withPrefix(\"STASH_XYZABC_UNLIKELY_\"); let result = len(d);");
        Assert.Equal(0L, result);
    }

    // ── env.cwd / env.home / env.hostname / env.user ─────────────────────────

    [Fact]
    public void Cwd_ReturnsNonEmptyString()
    {
        var result = Run("let result = env.cwd();");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void Home_ReturnsNonEmptyString()
    {
        var result = Run("let result = env.home();");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void Hostname_ReturnsNonEmptyString()
    {
        var result = Run("let result = env.hostname();");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void User_ReturnsNonEmptyString()
    {
        var result = Run("let result = env.user();");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    // ── env.os / env.arch ────────────────────────────────────────────────────

    [Fact]
    public void Os_ReturnsKnownPlatformString()
    {
        var result = Run("let result = env.os();");
        Assert.IsType<string>(result);
        var os = (string)result!;
        Assert.Contains(os, new[] { "linux", "macos", "windows", "unknown" });
    }

    [Fact]
    public void Arch_ReturnsNonEmptyString()
    {
        var result = Run("let result = env.arch();");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    // ── env.get with PATH ─────────────────────────────────────────────────────

    [Fact]
    public void Get_PathVariable_ReturnsNonNull()
    {
        var result = Run("let result = env.get(\"PATH\");");
        // PATH should be set on all supported platforms
        Assert.NotNull(result);
    }
}
