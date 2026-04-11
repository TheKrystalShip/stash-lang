namespace Stash.Tests.Interpreting;

public class ConvBuiltInsTests : StashTestBase
{
    // ── conv.toInt with base ──────────────────────────────────────────────────

    [Fact]
    public void ToInt_Base16_ParsesHex()
    {
        Assert.Equal(255L, Run("let result = conv.toInt(\"ff\", 16);"));
    }

    [Fact]
    public void ToInt_Base16_WithPrefix()
    {
        Assert.Equal(255L, Run("let result = conv.toInt(\"0xff\", 16);"));
    }

    [Fact]
    public void ToInt_Base2_ParsesBinary()
    {
        Assert.Equal(5L, Run("let result = conv.toInt(\"101\", 2);"));
    }

    [Fact]
    public void ToInt_Base8_ParsesOctal()
    {
        Assert.Equal(63L, Run("let result = conv.toInt(\"77\", 8);"));
    }

    [Fact]
    public void ToInt_NoBase_ExistingBehavior()
    {
        Assert.Equal(42L, Run("let result = conv.toInt(\"42\");"));
    }

    // ── conv.toHex with padding ───────────────────────────────────────────────

    [Fact]
    public void ToHex_WithPadding4_ZeroPads()
    {
        Assert.Equal("00ff", Run("let result = conv.toHex(255, 4);"));
    }

    [Fact]
    public void ToHex_WithPadding8_ZeroPads()
    {
        Assert.Equal("000000ff", Run("let result = conv.toHex(255, 8);"));
    }

    [Fact]
    public void ToHex_NoPadding_ExistingBehavior()
    {
        Assert.Equal("ff", Run("let result = conv.toHex(255);"));
    }
}
