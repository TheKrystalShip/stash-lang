namespace Stash.Tests.Interpreting;

public class TimeBuiltInsTests : StashTestBase
{
    // ── time.year ─────────────────────────────────────────────────────────

    [Fact]
    public void Year_CurrentYear()
    {
        var result = Run("let result = time.year();");
        var year = Assert.IsType<long>(result);
        Assert.True(year >= 2024 && year <= 2030);
    }

    [Fact]
    public void Year_FromTimestamp()
    {
        // 2024-01-15 00:00:00 UTC = 1705276800
        var result = Run("let result = time.year(1705276800);");
        Assert.Equal(2024L, result);
    }

    [Fact]
    public void Year_NonNumberThrows()
    {
        RunExpectingError("time.year(\"nope\");");
    }

    // ── time.month ────────────────────────────────────────────────────────

    [Fact]
    public void Month_CurrentMonth()
    {
        var result = Run("let result = time.month();");
        var month = Assert.IsType<long>(result);
        Assert.True(month >= 1 && month <= 12);
    }

    [Fact]
    public void Month_FromTimestamp()
    {
        var result = Run("let result = time.month(1705276800);");
        Assert.Equal(1L, result);
    }

    // ── time.day ──────────────────────────────────────────────────────────

    [Fact]
    public void Day_CurrentDay()
    {
        var result = Run("let result = time.day();");
        var day = Assert.IsType<long>(result);
        Assert.True(day >= 1 && day <= 31);
    }

    [Fact]
    public void Day_FromTimestamp()
    {
        var result = Run("let result = time.day(1705276800);");
        Assert.Equal(15L, result);
    }

    // ── time.hour ─────────────────────────────────────────────────────────

    [Fact]
    public void Hour_CurrentHour()
    {
        var result = Run("let result = time.hour();");
        var hour = Assert.IsType<long>(result);
        Assert.True(hour >= 0 && hour <= 23);
    }

    [Fact]
    public void Hour_FromTimestamp()
    {
        var result = Run("let result = time.hour(1705276800);");
        Assert.Equal(0L, result);
    }

    // ── time.minute ───────────────────────────────────────────────────────

    [Fact]
    public void Minute_CurrentMinute()
    {
        var result = Run("let result = time.minute();");
        var minute = Assert.IsType<long>(result);
        Assert.True(minute >= 0 && minute <= 59);
    }

    // ── time.second ───────────────────────────────────────────────────────

    [Fact]
    public void Second_CurrentSecond()
    {
        var result = Run("let result = time.second();");
        var second = Assert.IsType<long>(result);
        Assert.True(second >= 0 && second <= 59);
    }

    // ── time.dayOfWeek ────────────────────────────────────────────────────

    [Fact]
    public void DayOfWeek_ReturnsString()
    {
        var result = Run("let result = time.dayOfWeek();");
        var dow = Assert.IsType<string>(result);
        var validDays = new[] { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };
        Assert.Contains(dow, validDays);
    }

    [Fact]
    public void DayOfWeek_KnownTimestamp()
    {
        // 2024-01-15 is a Monday
        var result = Run("let result = time.dayOfWeek(1705276800);");
        Assert.Equal("monday", result);
    }

    // ── time.add ──────────────────────────────────────────────────────────

    [Fact]
    public void Add_AddsSeconds()
    {
        var result = Run("let result = time.add(1000, 500);");
        Assert.Equal(1500.0, result);
    }

    [Fact]
    public void Add_IntInputs()
    {
        var result = Run("let result = time.add(100, 50);");
        Assert.Equal(150.0, result);
    }

    [Fact]
    public void Add_NonNumberThrows()
    {
        RunExpectingError("time.add(\"nope\", 5);");
    }

    // ── time.diff ─────────────────────────────────────────────────────────

    [Fact]
    public void Diff_PositiveDifference()
    {
        var result = Run("let result = time.diff(1000, 500);");
        Assert.Equal(500.0, result);
    }

    [Fact]
    public void Diff_ReversedOrder()
    {
        var result = Run("let result = time.diff(500, 1000);");
        Assert.Equal(500.0, result);
    }

    [Fact]
    public void Diff_SameTimestamp()
    {
        var result = Run("let result = time.diff(100, 100);");
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Diff_NonNumberThrows()
    {
        RunExpectingError("time.diff(\"nope\", 5);");
    }
}
