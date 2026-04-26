namespace Stash.Tests.Interpreting;

public class MathBuiltInsTests : StashTestBase
{
    // ── math.round with precision ─────────────────────────────────────────────

    [Fact]
    public void Round_WithPrecision_RoundsToDecimalPlaces()
    {
        Assert.Equal(3.14, Run("let result = math.round(3.14159, 2);"));
    }

    [Fact]
    public void Round_WithZeroPrecision_RoundsToInteger()
    {
        Assert.Equal(3.0, Run("let result = math.round(3.14159, 0);"));
    }

    [Fact]
    public void Round_WithNegativePrecision_RoundsToTens()
    {
        Assert.Equal(1200.0, Run("let result = math.round(1234.5, -2);"));
    }

    [Fact]
    public void Round_NoPrecision_ExistingBehavior()
    {
        Assert.Equal(4.0, Run("let result = math.round(3.7);"));
    }

    // ── math.min variadic ─────────────────────────────────────────────────────

    [Fact]
    public void Min_Variadic_ReturnsSmallest()
    {
        Assert.Equal(1L, Run("let result = math.min(3, 1, 4, 1, 5);"));
    }

    [Fact]
    public void Min_TwoArgs_ExistingBehavior()
    {
        Assert.Equal(3L, Run("let result = math.min(5, 3);"));
    }

    // ── math.max variadic ─────────────────────────────────────────────────────

    [Fact]
    public void Max_Variadic_ReturnsLargest()
    {
        Assert.Equal(5L, Run("let result = math.max(3, 1, 4, 1, 5);"));
    }

    [Fact]
    public void Max_TwoArgs_ExistingBehavior()
    {
        Assert.Equal(5L, Run("let result = math.max(5, 3);"));
    }

    // ── math.log with base ────────────────────────────────────────────────────

    [Fact]
    public void Log_WithBase10_ReturnsCorrect()
    {
        Assert.Equal(2.0, (double)Run("let result = math.log(100, 10);")!, 10);
    }

    [Fact]
    public void Log_WithBase2_ReturnsCorrect()
    {
        Assert.Equal(3.0, (double)Run("let result = math.log(8, 2);")!, 10);
    }

    [Fact]
    public void Log_NoBase_NaturalLog()
    {
        var result = (double)Run("let result = math.log(math.E);")!;
        Assert.InRange(result, 0.9999999, 1.0000001);
    }

    // ── math.randomInt variadic ───────────────────────────────────────────────

    [Fact]
    public void RandomInt_NoArgs_ReturnsLong()
    {
        Assert.IsType<long>(Run("let result = math.randomInt();"));
    }

    [Fact]
    public void RandomInt_OneArg_ReturnsBoundedLong()
    {
        var result = (long)Run("let result = math.randomInt(100);")!;
        Assert.InRange(result, 0L, 100L);
    }

    // ── math.floor / math.ceil ────────────────────────────────────────────────

    [Fact]
    public void Floor_PositiveFloat_RoundsDown()
    {
        Assert.Equal(3.0, Run("let result = math.floor(3.9);"));
    }

    [Fact]
    public void Floor_NegativeFloat_RoundsDown()
    {
        Assert.Equal(-4.0, Run("let result = math.floor(-3.1);"));
    }

    [Fact]
    public void Ceil_PositiveFloat_RoundsUp()
    {
        Assert.Equal(4.0, Run("let result = math.ceil(3.1);"));
    }

    [Fact]
    public void Ceil_NegativeFloat_RoundsUp()
    {
        Assert.Equal(-3.0, Run("let result = math.ceil(-3.9);"));
    }

    // ── math.clamp ────────────────────────────────────────────────────────────

    [Fact]
    public void Clamp_ValueBetweenBounds_ReturnsValue()
    {
        Assert.Equal(5L, Run("let result = math.clamp(5, 1, 10);"));
    }

    [Fact]
    public void Clamp_ValueAtMin_ReturnsMin()
    {
        Assert.Equal(1L, Run("let result = math.clamp(1, 1, 10);"));
    }

    [Fact]
    public void Clamp_ValueAtMax_ReturnsMax()
    {
        Assert.Equal(10L, Run("let result = math.clamp(10, 1, 10);"));
    }

    [Fact]
    public void Clamp_ValueBelowMin_ReturnsMin()
    {
        Assert.Equal(1L, Run("let result = math.clamp(-5, 1, 10);"));
    }

    [Fact]
    public void Clamp_ValueAboveMax_ReturnsMax()
    {
        Assert.Equal(10L, Run("let result = math.clamp(99, 1, 10);"));
    }

    // ── math.pow ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pow_NegativeExponent_ReturnsFloat()
    {
        var result = (double)Run("let result = math.pow(2, -1);")!;
        Assert.Equal(0.5, result, precision: 10);
    }

    // ── math.log10 ────────────────────────────────────────────────────────────

    [Fact]
    public void Log10_100_Returns2()
    {
        var result = (double)Run("let result = math.log10(100);")!;
        Assert.Equal(2.0, result, precision: 10);
    }

    // ── math.abs ──────────────────────────────────────────────────────────────

    [Fact]
    public void Abs_NegativeInt_ReturnsPositive()
    {
        Assert.Equal(7L, Run("let result = math.abs(-7);"));
    }

    // ── math.sqrt ─────────────────────────────────────────────────────────────

    [Fact]
    public void Sqrt_4_Returns2()
    {
        Assert.Equal(2.0, Run("let result = math.sqrt(4);"));
    }
}
