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
}
