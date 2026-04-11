namespace Stash.Tests.Scheduler;

using Stash.Scheduler;

public class CronExpressionTests
{
    // ── Parse — Full Wildcard ────────────────────────────────────────────────

    [Fact]
    public void Parse_AllWildcards_ExpandsAllFields()
    {
        var cron = CronExpression.Parse("* * * * *");

        Assert.Equal(60, cron.Minutes.Length);      // 0–59
        Assert.Equal(24, cron.Hours.Length);        // 0–23
        Assert.Equal(31, cron.DaysOfMonth.Length);  // 1–31
        Assert.Equal(12, cron.Months.Length);       // 1–12
        Assert.Equal(7, cron.DaysOfWeek.Length);    // 0–6
    }

    // ── Parse — Step expressions ─────────────────────────────────────────────

    [Fact]
    public void Parse_MinuteStep5_ExpandsTo12Values()
    {
        var cron = CronExpression.Parse("*/5 * * * *");

        Assert.Equal(12, cron.Minutes.Length);
        Assert.Equal(new int[] { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55 }, cron.Minutes);
    }

    [Fact]
    public void Parse_RangeStep_ExpandsCorrectly()
    {
        var cron = CronExpression.Parse("1-5/2 * * * *");

        Assert.Equal(new int[] { 1, 3, 5 }, cron.Minutes);
    }

    // ── Parse — Range expressions ────────────────────────────────────────────

    [Fact]
    public void Parse_HourRange9To17WithWeekdays_ExpandsCorrectly()
    {
        var cron = CronExpression.Parse("0 9-17 * * 1-5");

        Assert.Equal(new int[] { 0 }, cron.Minutes);
        Assert.Equal(Enumerable.Range(9, 9).ToArray(), cron.Hours);       // 9..17
        Assert.Equal(new int[] { 1, 2, 3, 4, 5 }, cron.DaysOfWeek);
    }

    [Fact]
    public void Parse_MinuteRange5To10_ExpandsCorrectly()
    {
        var cron = CronExpression.Parse("5-10 * * * *");

        Assert.Equal(new int[] { 5, 6, 7, 8, 9, 10 }, cron.Minutes);
    }

    // ── Parse — Fixed values and lists ──────────────────────────────────────

    [Fact]
    public void Parse_FixedDayOfMonth_ParsesCorrectly()
    {
        var cron = CronExpression.Parse("30 2 1 * *");

        Assert.Equal(new int[] { 30 }, cron.Minutes);
        Assert.Equal(new int[] { 2 }, cron.Hours);
        Assert.Equal(new int[] { 1 }, cron.DaysOfMonth);
    }

    [Fact]
    public void Parse_CommaListMinutes_ParsesAllValues()
    {
        var cron = CronExpression.Parse("0,15,30,45 * * * *");

        Assert.Equal(new int[] { 0, 15, 30, 45 }, cron.Minutes);
    }

    // ── Parse — Day-of-week normalization ────────────────────────────────────

    [Fact]
    public void Parse_DowZero_ReturnsSundayAsZero()
    {
        var cron = CronExpression.Parse("0 0 * * 0");

        Assert.Equal(new int[] { 0 }, cron.DaysOfWeek);
    }

    [Fact]
    public void Parse_DowSeven_NormalizesToZero()
    {
        var cron = CronExpression.Parse("0 0 * * 7");

        Assert.Equal(new int[] { 0 }, cron.DaysOfWeek);
    }

    // ── TryParse — Rejection cases ───────────────────────────────────────────

    [Fact]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        bool ok = CronExpression.TryParse("", out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_TooFewFields_ReturnsFalse()
    {
        bool ok = CronExpression.TryParse("* *", out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_SixFields_ReturnsFalse()
    {
        bool ok = CronExpression.TryParse("* * * * * *", out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_MinuteOutOfRange_ReturnsFalse()
    {
        bool ok = CronExpression.TryParse("60 * * * *", out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_HourOutOfRange_ReturnsFalse()
    {
        bool ok = CronExpression.TryParse("* 24 * * *", out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_DayOfMonthZero_ReturnsFalse()
    {
        bool ok = CronExpression.TryParse("* * 0 * *", out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_MonthThirteen_ReturnsFalse()
    {
        bool ok = CronExpression.TryParse("* * * 13 *", out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_DayOfWeekEight_ReturnsFalse()
    {
        bool ok = CronExpression.TryParse("* * * * 8", out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData("; * * * *")]
    [InlineData("| * * * *")]
    [InlineData("& * * * *")]
    public void TryParse_ShellMetacharacter_ReturnsFalse(string expression)
    {
        bool ok = CronExpression.TryParse(expression, out _);

        Assert.False(ok);
    }

    // ── Parse — Throws on invalid input ─────────────────────────────────────

    [Fact]
    public void Parse_InvalidExpression_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CronExpression.Parse("not valid"));
    }

    // ── ToSystemdCalendar ────────────────────────────────────────────────────

    [Fact]
    public void ToSystemdCalendar_AllWildcards_ReturnsUniversalCalendar()
    {
        var cron = CronExpression.Parse("* * * * *");

        Assert.Equal("*-*-* *:*:00", cron.ToSystemdCalendar());
    }

    [Fact]
    public void ToSystemdCalendar_MinuteStep5_ReturnsStepNotation()
    {
        var cron = CronExpression.Parse("*/5 * * * *");

        Assert.Equal("*-*-* *:0/5:00", cron.ToSystemdCalendar());
    }

    [Fact]
    public void ToSystemdCalendar_MinuteZeroAllHours_ReturnsPaddedMinute()
    {
        var cron = CronExpression.Parse("0 * * * *");

        Assert.Equal("*-*-* *:00:00", cron.ToSystemdCalendar());
    }

    [Fact]
    public void ToSystemdCalendar_FixedHourAndMinute_ReturnsFormattedTime()
    {
        var cron = CronExpression.Parse("30 2 * * *");

        Assert.Equal("*-*-* 02:30:00", cron.ToSystemdCalendar());
    }

    [Fact]
    public void ToSystemdCalendar_FixedDomAndTime_ReturnsDateAndTime()
    {
        var cron = CronExpression.Parse("0 0 1 * *");

        Assert.Equal("*-*-01 00:00:00", cron.ToSystemdCalendar());
    }

    [Fact]
    public void ToSystemdCalendar_WeekdayRange_ContainsDowPrefixAndHourRange()
    {
        var cron = CronExpression.Parse("0 9-17 * * 1-5");
        string result = cron.ToSystemdCalendar();

        Assert.Contains("Mon..Fri", result);
        Assert.Contains("09..17", result);
    }

    [Fact]
    public void ToSystemdCalendar_MinuteStep15_ReturnsStepNotation()
    {
        var cron = CronExpression.Parse("*/15 * * * *");

        Assert.Equal("*-*-* *:0/15:00", cron.ToSystemdCalendar());
    }

    [Fact]
    public void ToSystemdCalendar_SundayOnly_ContainsSunPrefix()
    {
        var cron = CronExpression.Parse("0 0 * * 0");
        string result = cron.ToSystemdCalendar();

        Assert.Contains("Sun", result);
    }

    // ── ToString ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_ReturnsNormalizedOriginal()
    {
        var cron = CronExpression.Parse("0 9-17 * * 1-5");

        Assert.Equal("0 9-17 * * 1-5", cron.ToString());
    }
}
