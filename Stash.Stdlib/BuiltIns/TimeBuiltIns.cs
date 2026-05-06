namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Stash.Runtime;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>time</c> namespace built-in functions for date/time operations.
/// </summary>
/// <remarks>
/// <para>
/// Provides functions for working with timestamps and dates: <c>time.now</c>, <c>time.millis</c>,
/// <c>time.sleep</c>, <c>time.format</c>, <c>time.parse</c>, <c>time.date</c>, <c>time.clock</c>,
/// <c>time.iso</c>, <c>time.year</c>, <c>time.month</c>, <c>time.day</c>, <c>time.hour</c>,
/// <c>time.minute</c>, <c>time.second</c>, <c>time.dayOfWeek</c>, <c>time.add</c>, and <c>time.diff</c>.
/// </para>
/// <para>
/// Timestamps are represented as Unix epoch seconds (floating-point).
/// </para>
/// </remarks>
[StashNamespace]
public static partial class TimeBuiltIns
{
    /// <summary>Returns the current UTC time as a Unix timestamp (seconds since epoch).</summary>
    /// <returns>Current timestamp as float</returns>
    [StashFn]
    public static double Now() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    /// <summary>Returns the current UTC time as Unix milliseconds.</summary>
    /// <returns>Current time in milliseconds</returns>
    [StashFn]
    public static long Millis() =>
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Suspends execution for the given number of seconds.</summary>
    /// <param name="seconds">The duration to sleep (float or int)</param>
    [StashFn(Raw = true, ReturnType = "void")]
    private static StashValue Sleep(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        double seconds = SvArgs.Numeric(args, 0, "time.sleep");
        int ms = (int)(seconds * 1000);
        if (ctx.CancellationToken.CanBeCanceled)
        {
            ctx.CancellationToken.WaitHandle.WaitOne(ms);
            ctx.CancellationToken.ThrowIfCancellationRequested();
        }
        else
        {
            Thread.Sleep(ms);
        }
        return StashValue.Null;
    }

    /// <summary>Formats a Unix timestamp using a .NET format string.</summary>
    /// <param name="timestamp">Unix seconds</param>
    /// <param name="format">.NET format string</param>
    /// <returns>Formatted date/time string</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Format(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double seconds = SvArgs.Numeric(args, 0, "time.format");
        var fmt = SvArgs.String(args, 1, "time.format");
        var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
        return StashValue.FromObj(dto.ToString(fmt, CultureInfo.InvariantCulture));
    }

    /// <summary>Parses a date string using a .NET format string. Returns a Unix timestamp.</summary>
    /// <param name="str">The date string</param>
    /// <param name="format">The .NET format</param>
    /// <returns>Unix timestamp as float</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue Parse(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var str = SvArgs.String(args, 0, "time.parse");
        var fmt = SvArgs.String(args, 1, "time.parse");
        try
        {
            var dto = DateTimeOffset.ParseExact(str, fmt, CultureInfo.InvariantCulture);
            return StashValue.FromFloat(dto.ToUnixTimeMilliseconds() / 1000.0);
        }
        catch (FormatException)
        {
            throw new RuntimeError($"'time.parse' could not parse \"{str}\" with format \"{fmt}\".", errorType: StashErrorTypes.ParseError);
        }
    }

    /// <summary>Returns today's UTC date as a string in 'yyyy-MM-dd' format.</summary>
    /// <returns>Date string</returns>
    [StashFn]
    public static string Date() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Returns a high-resolution monotonic timestamp in seconds. Suitable for benchmarking.</summary>
    /// <returns>Seconds as float</returns>
    [StashFn]
    public static double Clock() =>
        (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    /// <summary>Returns the current UTC time as an ISO 8601 string.</summary>
    /// <returns>ISO 8601 formatted string</returns>
    [StashFn]
    public static string Iso() =>
        DateTimeOffset.UtcNow.ToString("o");

    // ── Time decomposition functions ─────────────────────────────────────────

    /// <summary>Returns the UTC year of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <returns>Year as int</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue Year(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var dt = GetDateTimeFromArgs("time.year", args);
        return StashValue.FromInt((long)dt.Year);
    }

    /// <summary>Returns the UTC month (1-12) of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <returns>Month as int</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue Month(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var dt = GetDateTimeFromArgs("time.month", args);
        return StashValue.FromInt((long)dt.Month);
    }

    /// <summary>Returns the UTC day of month (1-31) of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <returns>Day as int</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue Day(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var dt = GetDateTimeFromArgs("time.day", args);
        return StashValue.FromInt((long)dt.Day);
    }

    /// <summary>Returns the UTC hour (0-23) of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <returns>Hour as int</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue Hour(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var dt = GetDateTimeFromArgs("time.hour", args);
        return StashValue.FromInt((long)dt.Hour);
    }

    /// <summary>Returns the UTC minute (0-59) of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <returns>Minute as int</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue Minute(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var dt = GetDateTimeFromArgs("time.minute", args);
        return StashValue.FromInt((long)dt.Minute);
    }

    /// <summary>Returns the UTC second (0-59) of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <returns>Second as int</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue Second(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var dt = GetDateTimeFromArgs("time.second", args);
        return StashValue.FromInt((long)dt.Second);
    }

    /// <summary>Returns the UTC day of week as lowercase string (e.g. 'monday') of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <returns>Day of week string</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue DayOfWeek(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var dt = GetDateTimeFromArgs("time.dayOfWeek", args);
        return StashValue.FromObj(dt.DayOfWeek.ToString().ToLowerInvariant());
    }

    /// <summary>Adds seconds to a Unix timestamp and returns the new timestamp.</summary>
    /// <param name="timestamp">The base timestamp</param>
    /// <param name="seconds">Number of seconds to add</param>
    /// <returns>New timestamp as float</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue Add(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double ts = SvArgs.Numeric(args, 0, "time.add");
        double seconds = SvArgs.Numeric(args, 1, "time.add");
        return StashValue.FromFloat(ts + seconds);
    }

    /// <summary>Returns the absolute difference in seconds between two Unix timestamps.</summary>
    /// <param name="timestamp1">First timestamp</param>
    /// <param name="timestamp2">Second timestamp</param>
    /// <returns>Absolute difference in seconds</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue Diff(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double ts1 = SvArgs.Numeric(args, 0, "time.diff");
        double ts2 = SvArgs.Numeric(args, 1, "time.diff");
        return StashValue.FromFloat(Math.Abs(ts1 - ts2));
    }

    // ── Timezone Functions ───────────────────────────────────────────────────

    /// <summary>Converts a UTC timestamp to the local time in a timezone by applying the UTC offset.</summary>
    /// <param name="timestamp">Unix seconds (UTC)</param>
    /// <param name="timezone">IANA or Windows timezone ID</param>
    /// <returns>Offset-adjusted Unix timestamp as float</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue ToTimezone(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double ts = SvArgs.Numeric(args, 0, "time.toTimezone");
        var timezone = SvArgs.String(args, 1, "time.toTimezone");
        var utcDto = DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000));
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException)
        {
            throw new RuntimeError($"'time.toTimezone' unknown timezone '{timezone}'. Use time.timezones() to list available IDs.", errorType: StashErrorTypes.ValueError);
        }
        var offset = tz.GetUtcOffset(utcDto.UtcDateTime);
        return StashValue.FromFloat(ts + offset.TotalSeconds);
    }

    /// <summary>Interprets a timestamp as local time in the given timezone and returns the UTC equivalent.</summary>
    /// <param name="timestamp">Unix seconds (local time)</param>
    /// <param name="timezone">IANA or Windows timezone ID</param>
    /// <returns>UTC Unix timestamp as float</returns>
    [StashFn(Name = "toUTC", Raw = true, ReturnType = "float")]
    private static StashValue ToUTC(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double ts = SvArgs.Numeric(args, 0, "time.toUTC");
        var timezone = SvArgs.String(args, 1, "time.toUTC");
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException)
        {
            throw new RuntimeError($"'time.toUTC' unknown timezone '{timezone}'. Use time.timezones() to list available IDs.", errorType: StashErrorTypes.ValueError);
        }
        var localAsUnspecified = DateTime.SpecifyKind(
            DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000)).UtcDateTime,
            DateTimeKind.Unspecified);
        var offset = tz.GetUtcOffset(localAsUnspecified);
        return StashValue.FromFloat(ts - offset.TotalSeconds);
    }

    /// <summary>Returns the local system timezone ID.</summary>
    /// <returns>Timezone ID string (e.g. 'America/New_York')</returns>
    [StashFn]
    public static string Timezone() =>
        TimeZoneInfo.Local.Id;

    /// <summary>Returns an array of all available timezone IDs.</summary>
    /// <returns>Array of timezone ID strings</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Timezones(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var list = TimeZoneInfo.GetSystemTimeZones().Select(z => StashValue.FromObj(z.Id)).ToList();
        return StashValue.FromObj(list);
    }

    /// <summary>Returns the UTC offset in hours for a timezone at a specific timestamp.</summary>
    /// <param name="timestamp">Unix seconds</param>
    /// <param name="timezone">IANA or Windows timezone ID</param>
    /// <returns>UTC offset in hours (e.g. -5.0 for EST, 5.5 for IST)</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue Offset(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double ts = SvArgs.Numeric(args, 0, "time.offset");
        var timezone = SvArgs.String(args, 1, "time.offset");
        var utcDto = DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000));
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException)
        {
            throw new RuntimeError($"'time.offset' unknown timezone '{timezone}'. Use time.timezones() to list available IDs.", errorType: StashErrorTypes.ValueError);
        }
        var offset = tz.GetUtcOffset(utcDto.UtcDateTime);
        return StashValue.FromFloat(offset.TotalHours);
    }

    // ── Duration Convenience Helpers ─────────────────────────────────────────

    /// <summary>Returns n seconds (identity, for consistency). Useful with time.add().</summary>
    /// <param name="n">Number of seconds</param>
    /// <returns>n as float</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue Seconds(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double n = SvArgs.Numeric(args, 0, "time.seconds");
        return StashValue.FromFloat(n);
    }

    /// <summary>Returns the number of seconds in n minutes.</summary>
    /// <param name="n">Number of minutes</param>
    /// <returns>Seconds as float</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue Minutes(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double n = SvArgs.Numeric(args, 0, "time.minutes");
        return StashValue.FromFloat(n * 60.0);
    }

    /// <summary>Returns the number of seconds in n hours.</summary>
    /// <param name="n">Number of hours</param>
    /// <returns>Seconds as float</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue Hours(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double n = SvArgs.Numeric(args, 0, "time.hours");
        return StashValue.FromFloat(n * 3600.0);
    }

    /// <summary>Returns the number of seconds in n days.</summary>
    /// <param name="n">Number of days</param>
    /// <returns>Seconds as float</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue Days(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double n = SvArgs.Numeric(args, 0, "time.days");
        return StashValue.FromFloat(n * 86400.0);
    }

    /// <summary>Returns the number of seconds in n weeks.</summary>
    /// <param name="n">Number of weeks</param>
    /// <returns>Seconds as float</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue Weeks(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double n = SvArgs.Numeric(args, 0, "time.weeks");
        return StashValue.FromFloat(n * 604800.0);
    }

    // ── Utility Functions ────────────────────────────────────────────────────

    /// <summary>Returns the timestamp truncated to the start of the given unit (UTC).</summary>
    /// <param name="timestamp">Unix seconds</param>
    /// <param name="unit">One of: year, month, day, hour, minute</param>
    /// <returns>Start-of-unit Unix timestamp as float</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue StartOf(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double ts = SvArgs.Numeric(args, 0, "time.startOf");
        var unit = SvArgs.String(args, 1, "time.startOf");
        var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000));
        DateTimeOffset result = unit switch
        {
            "year"   => new DateTimeOffset(dto.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "month"  => new DateTimeOffset(dto.Year, dto.Month, 1, 0, 0, 0, TimeSpan.Zero),
            "day"    => new DateTimeOffset(dto.Year, dto.Month, dto.Day, 0, 0, 0, TimeSpan.Zero),
            "hour"   => new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, 0, 0, TimeSpan.Zero),
            "minute" => new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, 0, TimeSpan.Zero),
            _ => throw new RuntimeError($"'time.startOf' unknown unit '{unit}'. Use: year, month, day, hour, minute", errorType: StashErrorTypes.ValueError),
        };
        return StashValue.FromFloat(result.ToUnixTimeMilliseconds() / 1000.0);
    }

    /// <summary>Returns the timestamp at the end of the given unit (last millisecond, UTC).</summary>
    /// <param name="timestamp">Unix seconds</param>
    /// <param name="unit">One of: year, month, day, hour, minute</param>
    /// <returns>End-of-unit Unix timestamp as float</returns>
    [StashFn(Raw = true, ReturnType = "float")]
    private static StashValue EndOf(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        double ts = SvArgs.Numeric(args, 0, "time.endOf");
        var unit = SvArgs.String(args, 1, "time.endOf");
        var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000));
        DateTimeOffset result = unit switch
        {
            "year"   => new DateTimeOffset(dto.Year, 12, 31, 23, 59, 59, 999, TimeSpan.Zero),
            "month"  => new DateTimeOffset(dto.Year, dto.Month, DateTime.DaysInMonth(dto.Year, dto.Month), 23, 59, 59, 999, TimeSpan.Zero),
            "day"    => new DateTimeOffset(dto.Year, dto.Month, dto.Day, 23, 59, 59, 999, TimeSpan.Zero),
            "hour"   => new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, 59, 59, 999, TimeSpan.Zero),
            "minute" => new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, 59, 999, TimeSpan.Zero),
            _ => throw new RuntimeError($"'time.endOf' unknown unit '{unit}'. Use: year, month, day, hour, minute", errorType: StashErrorTypes.ValueError),
        };
        return StashValue.FromFloat(result.ToUnixTimeMilliseconds() / 1000.0);
    }

    /// <summary>Returns whether the year of a timestamp is a leap year (defaults to current year).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <returns>true if leap year</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue IsLeapYear(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var dt = GetDateTimeFromArgs("time.isLeapYear", args);
        return StashValue.FromBool(DateTime.IsLeapYear(dt.Year));
    }

    /// <summary>Returns the number of days in the month of a timestamp (defaults to current month).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <returns>Days in month as int</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue DaysInMonth(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        var dt = GetDateTimeFromArgs("time.daysInMonth", args);
        return StashValue.FromInt((long)DateTime.DaysInMonth(dt.Year, dt.Month));
    }

    private static DateTimeOffset GetDateTimeFromArgs(string funcName, ReadOnlySpan<StashValue> args)
    {
        if (args.Length > 1)
        {
            throw new RuntimeError($"'{funcName}' expects 0 or 1 arguments.");
        }

        if (args.Length == 0)
        {
            return DateTimeOffset.UtcNow;
        }

        double seconds = SvArgs.Numeric(args, 0, funcName);
        return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
    }
}
