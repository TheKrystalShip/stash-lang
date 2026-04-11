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

    // ── time.toTimezone ────────────────────────────────────────────────────

    [Fact]
    public void ToTimezone_UTC_NoChange()
    {
        var result = Run(@"
            let ts = 1705276800.0;
            let result = time.toTimezone(ts, ""UTC"");
        ");
        Assert.Equal(1705276800.0, (double)result!, 1.0);
    }

    [Fact]
    public void ToTimezone_NewYork_AppliesOffset()
    {
        // Jan 15, 2024 00:00:00 UTC → EST is UTC-5
        var result = Run(@"
            let ts = 1705276800.0;
            let result = time.toTimezone(ts, ""America/New_York"");
        ");
        double adjusted = (double)result!;
        // Should be ts - 5*3600 = ts - 18000
        Assert.Equal(1705276800.0 - 18000.0, adjusted, 1.0);
    }

    [Fact]
    public void ToTimezone_InvalidTimezone_ThrowsError()
    {
        RunExpectingError(@"
            let result = time.toTimezone(1705276800.0, ""Invalid/Timezone"");
        ");
    }

    // ── time.toUTC ─────────────────────────────────────────────────────────

    [Fact]
    public void ToUTC_ReverseOfToTimezone()
    {
        var result = Run(@"
            let ts = 1705276800.0;
            let local = time.toTimezone(ts, ""America/New_York"");
            let result = time.toUTC(local, ""America/New_York"");
        ");
        Assert.Equal(1705276800.0, (double)result!, 1.0);
    }

    // ── time.timezone ──────────────────────────────────────────────────────

    [Fact]
    public void Timezone_ReturnsString()
    {
        var result = Run(@"let result = time.timezone();");
        Assert.IsType<string>(result);
        Assert.True(((string)result!).Length > 0);
    }

    // ── time.timezones ─────────────────────────────────────────────────────

    [Fact]
    public void Timezones_ReturnsNonEmptyArray()
    {
        var result = Run(@"let result = len(time.timezones()) > 0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Timezones_ContainsUTC()
    {
        var result = Run(@"let result = arr.includes(time.timezones(), ""UTC"");");
        Assert.Equal(true, result);
    }

    // ── time.offset ────────────────────────────────────────────────────────

    [Fact]
    public void Offset_UTC_ReturnsZero()
    {
        var result = Run(@"let result = time.offset(1705276800.0, ""UTC"");");
        Assert.Equal(0.0, (double)result!, 0.01);
    }

    [Fact]
    public void Offset_InvalidTimezone_ThrowsError()
    {
        RunExpectingError(@"
            let result = time.offset(1705276800.0, ""Not/Real"");
        ");
    }

    // ── Duration helpers ───────────────────────────────────────────────────

    [Fact]
    public void Seconds_ReturnsIdentity()
    {
        var result = Run(@"let result = time.seconds(5);");
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void Minutes_ReturnsTimesSeconds()
    {
        var result = Run(@"let result = time.minutes(2);");
        Assert.Equal(120.0, result);
    }

    [Fact]
    public void Hours_ReturnsTimesSeconds()
    {
        var result = Run(@"let result = time.hours(1);");
        Assert.Equal(3600.0, result);
    }

    [Fact]
    public void Days_ReturnsTimesSeconds()
    {
        var result = Run(@"let result = time.days(1);");
        Assert.Equal(86400.0, result);
    }

    [Fact]
    public void Weeks_ReturnsTimesSeconds()
    {
        var result = Run(@"let result = time.weeks(1);");
        Assert.Equal(604800.0, result);
    }

    [Fact]
    public void DurationHelpers_WithAdd_WorkCorrectly()
    {
        var result = Run(@"
            let ts = 1705276800.0;
            let result = time.add(ts, time.hours(2));
        ");
        Assert.Equal(1705276800.0 + 7200.0, (double)result!, 1.0);
    }

    // ── time.startOf ───────────────────────────────────────────────────────

    [Fact]
    public void StartOf_Day_ZerosHourMinuteSecond()
    {
        // Jan 15, 2024 12:34:56 UTC = 1705322096
        var result = Run(@"
            let ts = 1705322096.0;
            let start = time.startOf(ts, ""day"");
            let result = time.hour(start) == 0 && time.minute(start) == 0 && time.second(start) == 0;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void StartOf_Month_FirstDayZeroTime()
    {
        var result = Run(@"
            let ts = 1705322096.0;
            let start = time.startOf(ts, ""month"");
            let result = time.day(start) == 1 && time.hour(start) == 0;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void StartOf_Year_Jan1ZeroTime()
    {
        var result = Run(@"
            let ts = 1705322096.0;
            let start = time.startOf(ts, ""year"");
            let result = time.month(start) == 1 && time.day(start) == 1 && time.hour(start) == 0;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void StartOf_InvalidUnit_ThrowsError()
    {
        RunExpectingError(@"
            let result = time.startOf(1705322096.0, ""week"");
        ");
    }

    // ── time.endOf ─────────────────────────────────────────────────────────

    [Fact]
    public void EndOf_Day_Returns235959()
    {
        var result = Run(@"
            let ts = 1705322096.0;
            let end = time.endOf(ts, ""day"");
            let result = time.hour(end) == 23 && time.minute(end) == 59 && time.second(end) == 59;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void EndOf_Month_LastDayOfMonth()
    {
        // January 2024: 31 days
        var result = Run(@"
            let ts = 1705322096.0;
            let end = time.endOf(ts, ""month"");
            let result = time.day(end) == 31;
        ");
        Assert.Equal(true, result);
    }

    // ── time.isLeapYear ────────────────────────────────────────────────────

    [Fact]
    public void IsLeapYear_2024_ReturnsTrue()
    {
        // 2024 is a leap year; Jan 15 2024 = 1705276800
        var result = Run(@"let result = time.isLeapYear(1705276800.0);");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IsLeapYear_2023_ReturnsFalse()
    {
        // Jan 1 2023 = 1672531200
        var result = Run(@"let result = time.isLeapYear(1672531200.0);");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IsLeapYear_NoArgs_CurrentYear()
    {
        var result = Run(@"let result = typeof(time.isLeapYear());");
        Assert.Equal("bool", result);
    }

    // ── time.daysInMonth ───────────────────────────────────────────────────

    [Fact]
    public void DaysInMonth_January_Returns31()
    {
        var result = Run(@"let result = time.daysInMonth(1705276800.0);");
        Assert.Equal(31L, result);
    }

    [Fact]
    public void DaysInMonth_Feb2024_Returns29()
    {
        // Feb 15, 2024 = 1707955200
        var result = Run(@"let result = time.daysInMonth(1707955200.0);");
        Assert.Equal(29L, result);
    }

    [Fact]
    public void DaysInMonth_NoArgs_HasResult()
    {
        var result = Run(@"let result = time.daysInMonth() > 0;");
        Assert.Equal(true, result);
    }
}
