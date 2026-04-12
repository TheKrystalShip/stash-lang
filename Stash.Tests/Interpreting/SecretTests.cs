using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class SecretTests : StashTestBase
{
    // ── secret() creation ────────────────────────────────────────────

    [Fact]
    public void Secret_WrapString_ReturnsStashSecret()
    {
        var result = Run("""let result = secret("hello");""");
        Assert.IsType<StashSecret>(result);
    }

    [Fact]
    public void Secret_WrapInt_ReturnsStashSecret()
    {
        var result = Run("let result = secret(42);");
        Assert.IsType<StashSecret>(result);
    }

    [Fact]
    public void Secret_WrapNull_ReturnsStashSecret()
    {
        var result = Run("let result = secret(null);");
        Assert.IsType<StashSecret>(result);
    }

    [Fact]
    public void Secret_WrapSecret_DoesNotNest()
    {
        // Wrapping a secret in another secret should produce a single-level secret
        var result = Run("let result = secret(secret(42));");
        var outer = Assert.IsType<StashSecret>(result);
        // Inner value should be the int 42, not another StashSecret
        Assert.Equal(42L, outer.Reveal().AsInt);
        Assert.False(outer.InnerValue.IsObj && outer.InnerValue.AsObj is StashSecret,
            "Inner value should not be a nested StashSecret");
    }

    // ── reveal() ────────────────────────────────────────────────────

    [Fact]
    public void Reveal_SecretString_ReturnsOriginalString()
    {
        var result = Run("""let result = reveal(secret("hello"));""");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Reveal_SecretInt_ReturnsOriginalInt()
    {
        var result = Run("let result = reveal(secret(42));");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Reveal_NonSecret_ThrowsError()
    {
        RunExpectingError("reveal(42);");
    }

    // ── typeof ───────────────────────────────────────────────────────

    [Fact]
    public void Typeof_Secret_ReturnsSecretString()
    {
        var result = Run("let result = typeof(secret(42));");
        Assert.Equal("secret", result);
    }

    // ── is type check ────────────────────────────────────────────────

    [Fact]
    public void Is_SecretValue_ReturnsTrue()
    {
        var result = Run("let result = secret(42) is secret;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Is_NonSecret_ReturnsFalse()
    {
        var result = Run("let result = 42 is secret;");
        Assert.Equal(false, result);
    }

    // ── Stringify / println redaction ────────────────────────────────

    [Fact]
    public void Println_Secret_PrintsRedacted()
    {
        string output = RunCapturingOutput("""io.println(secret("mypassword"));""");
        Assert.Contains(StashSecret.RedactedText, output);
        Assert.DoesNotContain("mypassword", output);
    }

    [Fact]
    public void Stringify_SecretInInterpolation_Redacts()
    {
        var result = Run("""let s = secret("abc"); let result = "Key: ${s}";""");
        Assert.Equal("Key: ******", result);
    }

    // ── String concatenation taint propagation ───────────────────────

    [Fact]
    public void StringConcat_SecretOnRight_ProducesSecret()
    {
        var result = Run("""let result = "token=" + secret("key123");""");
        Assert.IsType<StashSecret>(result);
    }

    [Fact]
    public void StringConcat_SecretOnLeft_ProducesSecret()
    {
        var result = Run("""let result = secret("key123") + "=value";""");
        Assert.IsType<StashSecret>(result);
    }

    [Fact]
    public void StringConcat_TaintedReveal_ShowsRealValue()
    {
        var result = Run("""let result = reveal("token=" + secret("key123"));""");
        Assert.Equal("token=key123", result);
    }

    // ── len() ────────────────────────────────────────────────────────

    [Fact]
    public void Len_SecretString_ReturnsLength()
    {
        var result = Run("""let result = len(secret("hello"));""");
        Assert.Equal(5L, result);
    }

    [Fact]
    public void Len_SecretArray_ReturnsLength()
    {
        var result = Run("let result = len(secret([1, 2, 3]));");
        Assert.Equal(3L, result);
    }

    // ── Equality ─────────────────────────────────────────────────────

    [Fact]
    public void Secret_SameInnerValue_AreEqual()
    {
        var result = Run("let s1 = secret(42); let s2 = secret(42); let result = s1 == s2;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Secret_DifferentInnerValue_AreNotEqual()
    {
        var result = Run("let s1 = secret(42); let s2 = secret(99); let result = s1 == s2;");
        Assert.Equal(false, result);
    }
}
