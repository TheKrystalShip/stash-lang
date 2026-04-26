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

    // ── conv.toFloat ──────────────────────────────────────────────────────────

    [Fact]
    public void ToFloat_IntToFloat_ReturnsFloat()
    {
        var result = Run("let result = conv.toFloat(5);");
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void ToFloat_StringToFloat_ReturnsFloat()
    {
        var result = Run("let result = conv.toFloat(\"3.14\");");
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ToFloat_InvalidString_ThrowsError()
    {
        RunExpectingError("conv.toFloat(\"abc\");");
    }

    // ── conv.toBool ───────────────────────────────────────────────────────────

    [Fact]
    public void ToBool_TruthyInt_ReturnsTrue()
    {
        Assert.Equal(true, Run("let result = conv.toBool(1);"));
    }

    [Fact]
    public void ToBool_ZeroInt_ReturnsFalse()
    {
        Assert.Equal(false, Run("let result = conv.toBool(0);"));
    }

    [Fact]
    public void ToBool_EmptyString_ReturnsFalse()
    {
        Assert.Equal(false, Run("let result = conv.toBool(\"\");"));
    }

    [Fact]
    public void ToBool_NullValue_ReturnsFalse()
    {
        Assert.Equal(false, Run("let result = conv.toBool(null);"));
    }

    [Fact]
    public void ToBool_NonEmptyString_ReturnsTrue()
    {
        Assert.Equal(true, Run("let result = conv.toBool(\"hello\");"));
    }

    // ── conv.toOct / conv.toBin ───────────────────────────────────────────────

    [Fact]
    public void ToOct_Int_ReturnsOctalString()
    {
        Assert.Equal("17", Run("let result = conv.toOct(15);"));
    }

    [Fact]
    public void ToBin_Int_ReturnsBinaryString()
    {
        Assert.Equal("1010", Run("let result = conv.toBin(10);"));
    }

    // ── conv.fromHex / conv.fromOct / conv.fromBin ────────────────────────────

    [Fact]
    public void FromHex_HexString_ParsesInt()
    {
        Assert.Equal(255L, Run("let result = conv.fromHex(\"ff\");"));
    }

    [Fact]
    public void FromHex_WithPrefix_ParsesInt()
    {
        Assert.Equal(255L, Run("let result = conv.fromHex(\"0xff\");"));
    }

    [Fact]
    public void FromBin_BinaryString_ParsesInt()
    {
        Assert.Equal(5L, Run("let result = conv.fromBin(\"101\");"));
    }

    // ── conv.charCode / conv.fromCharCode ─────────────────────────────────────

    [Fact]
    public void CharCode_Char_ReturnsCodePoint()
    {
        Assert.Equal(65L, Run("let result = conv.charCode(\"A\");"));
    }

    [Fact]
    public void FromCharCode_Point_ReturnsCharacter()
    {
        Assert.Equal("A", Run("let result = conv.fromCharCode(65);"));
    }

    // ── conv.toStr ────────────────────────────────────────────────────────────

    [Fact]
    public void ToStr_Int_ReturnsStringRepresentation()
    {
        Assert.Equal("42", Run("let result = conv.toStr(42);"));
    }

    // ── conv.toInt edge cases ─────────────────────────────────────────────────

    [Fact]
    public void ToInt_FloatTruncated()
    {
        Assert.Equal(3L, Run("let result = conv.toInt(3.9);"));
    }

    [Fact]
    public void ToInt_InvalidString_ThrowsError()
    {
        RunExpectingError("conv.toInt(\"not_a_number\");");
    }
}
