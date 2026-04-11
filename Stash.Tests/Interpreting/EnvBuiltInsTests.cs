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
}
