namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Stash.Runtime;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

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
    /// <exception cref="CancellationError">if the cancellation token is triggered while sleeping</exception>
    [StashFn]
    public static void Sleep(IInterpreterContext ctx, [StashParam(Type = "number")] double seconds)
    {
        int ms = (int)(seconds * 1000);

        // Drain-aware wait: park via the callback queue's drain loop so that queued
        // callbacks (fs.watch, signal.on, future timers) fire during the sleep.
        // The deadline is computed now so DrainCallbacks recomputes remaining across drains.
        // When _isDraining is set (a callback called time.sleep internally), DrainCallbacks
        // returns immediately — the inner sleep then falls through to the primitive wait below.
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(ms);
        ctx.DrainCallbacks(WaitMode.Until(deadline));

        // After DrainCallbacks returns the duration has elapsed (or cancellation was thrown).
        // ThrowIfCancellationRequested is still needed: DrainCallbacks (when _isDraining) may
        // have returned immediately without checking cancellation.
        ctx.CancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Formats a Unix timestamp using a .NET format string.</summary>
    /// <param name="timestamp">Unix seconds</param>
    /// <param name="format">.NET format string</param>
    /// <returns>Formatted date/time string</returns>
    [StashFn]
    public static string Format([StashParam(Type = "number")] double timestamp, string format)
    {
        var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestamp * 1000));
        return dto.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Parses a date string using a .NET format string. Returns a Unix timestamp.</summary>
    /// <param name="str">The date string</param>
    /// <param name="format">The .NET format</param>
    /// <exception cref="ParseError">if the date string cannot be parsed with the given format</exception>
    /// <returns>Unix timestamp as float</returns>
    [StashFn]
    public static double Parse(string str, string format)
    {
        try
        {
            var dto = DateTimeOffset.ParseExact(str, format, CultureInfo.InvariantCulture);
            return dto.ToUnixTimeMilliseconds() / 1000.0;
        }
        catch (FormatException)
        {
            throw new ParseError($"'time.parse' could not parse \"{str}\" with format \"{format}\".");
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
    /// <exception cref="TypeError">if `timestamp` is not a number</exception>
    /// <returns>Year as int</returns>
    [StashFn]
    public static long Year(params StashValue[] args)
    {
        var dt = GetDateTimeFromArgs("time.year", args);
        return (long)dt.Year;
    }

    /// <summary>Returns the UTC month (1-12) of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <exception cref="TypeError">if `timestamp` is not a number</exception>
    /// <returns>Month as int</returns>
    [StashFn]
    public static long Month(params StashValue[] args)
    {
        var dt = GetDateTimeFromArgs("time.month", args);
        return (long)dt.Month;
    }

    /// <summary>Returns the UTC day of month (1-31) of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <exception cref="TypeError">if `timestamp` is not a number</exception>
    /// <returns>Day as int</returns>
    [StashFn]
    public static long Day(params StashValue[] args)
    {
        var dt = GetDateTimeFromArgs("time.day", args);
        return (long)dt.Day;
    }

    /// <summary>Returns the UTC hour (0-23) of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <exception cref="TypeError">if `timestamp` is not a number</exception>
    /// <returns>Hour as int</returns>
    [StashFn]
    public static long Hour(params StashValue[] args)
    {
        var dt = GetDateTimeFromArgs("time.hour", args);
        return (long)dt.Hour;
    }

    /// <summary>Returns the UTC minute (0-59) of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <exception cref="TypeError">if `timestamp` is not a number</exception>
    /// <returns>Minute as int</returns>
    [StashFn]
    public static long Minute(params StashValue[] args)
    {
        var dt = GetDateTimeFromArgs("time.minute", args);
        return (long)dt.Minute;
    }

    /// <summary>Returns the UTC second (0-59) of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <exception cref="TypeError">if `timestamp` is not a number</exception>
    /// <returns>Second as int</returns>
    [StashFn]
    public static long Second(params StashValue[] args)
    {
        var dt = GetDateTimeFromArgs("time.second", args);
        return (long)dt.Second;
    }

    /// <summary>Returns the UTC day of week as lowercase string (e.g. 'monday') of a timestamp (or now if omitted).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <exception cref="TypeError">if `timestamp` is not a number</exception>
    /// <returns>Day of week string</returns>
    [StashFn]
    public static string DayOfWeek(params StashValue[] args)
    {
        var dt = GetDateTimeFromArgs("time.dayOfWeek", args);
        return dt.DayOfWeek.ToString().ToLowerInvariant();
    }

    /// <summary>Adds seconds to a Unix timestamp and returns the new timestamp.</summary>
    /// <param name="timestamp">The base timestamp</param>
    /// <param name="seconds">Number of seconds to add</param>
    /// <returns>New timestamp as float</returns>
    [StashFn]
    public static double Add([StashParam(Type = "number")] double timestamp, [StashParam(Type = "number")] double seconds)
        => timestamp + seconds;

    /// <summary>Returns the absolute difference in seconds between two Unix timestamps.</summary>
    /// <param name="timestamp1">First timestamp</param>
    /// <param name="timestamp2">Second timestamp</param>
    /// <returns>Absolute difference in seconds</returns>
    [StashFn]
    public static double Diff([StashParam(Type = "number")] double timestamp1, [StashParam(Type = "number")] double timestamp2)
        => Math.Abs(timestamp1 - timestamp2);

    // ── Timezone Functions ───────────────────────────────────────────────────

    /// <summary>Converts a UTC timestamp to the local time in a timezone by applying the UTC offset.</summary>
    /// <param name="timestamp">Unix seconds (UTC)</param>
    /// <param name="timezone">IANA or Windows timezone ID</param>
    /// <exception cref="ValueError">if the timezone ID is not recognised</exception>
    /// <returns>Offset-adjusted Unix timestamp as float</returns>
    [StashFn]
    public static double ToTimezone([StashParam(Type = "number")] double timestamp, string timezone)
    {
        var utcDto = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestamp * 1000));
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException)
        {
            throw new ValueError($"'time.toTimezone' unknown timezone '{timezone}'. Use time.timezones() to list available IDs.");
        }
        var offset = tz.GetUtcOffset(utcDto.UtcDateTime);
        return timestamp + offset.TotalSeconds;
    }

    /// <summary>Interprets a timestamp as local time in the given timezone and returns the UTC equivalent.</summary>
    /// <param name="timestamp">Unix seconds (local time)</param>
    /// <param name="timezone">IANA or Windows timezone ID</param>
    /// <exception cref="ValueError">if the timezone ID is not recognised</exception>
    /// <returns>UTC Unix timestamp as float</returns>
    [StashFn(Name = "toUTC")]
    public static double ToUTC([StashParam(Type = "number")] double timestamp, string timezone)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException)
        {
            throw new ValueError($"'time.toUTC' unknown timezone '{timezone}'. Use time.timezones() to list available IDs.");
        }
        var localAsUnspecified = DateTime.SpecifyKind(
            DateTimeOffset.FromUnixTimeMilliseconds((long)(timestamp * 1000)).UtcDateTime,
            DateTimeKind.Unspecified);
        var offset = tz.GetUtcOffset(localAsUnspecified);
        return timestamp - offset.TotalSeconds;
    }

    /// <summary>Returns the local system timezone ID.</summary>
    /// <returns>Timezone ID string (e.g. 'America/New_York')</returns>
    [StashFn]
    public static string Timezone() =>
        TimeZoneInfo.Local.Id;

    /// <summary>Returns an array of all available timezone IDs.</summary>
    /// <returns>Array of timezone ID strings</returns>
    [StashFn]
    public static List<StashValue> Timezones()
        => TimeZoneInfo.GetSystemTimeZones().Select(z => StashValue.FromObj(z.Id)).ToList();

    /// <summary>Returns the UTC offset in hours for a timezone at a specific timestamp.</summary>
    /// <param name="timestamp">Unix seconds</param>
    /// <param name="timezone">IANA or Windows timezone ID</param>
    /// <exception cref="ValueError">if the timezone ID is not recognised</exception>
    /// <returns>UTC offset in hours (e.g. -5.0 for EST, 5.5 for IST)</returns>
    [StashFn]
    public static double Offset([StashParam(Type = "number")] double timestamp, string timezone)
    {
        var utcDto = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestamp * 1000));
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException)
        {
            throw new ValueError($"'time.offset' unknown timezone '{timezone}'. Use time.timezones() to list available IDs.");
        }
        return tz.GetUtcOffset(utcDto.UtcDateTime).TotalHours;
    }

    // ── Duration Convenience Helpers ─────────────────────────────────────────

    /// <summary>Returns n seconds (identity, for consistency). Useful with time.add().</summary>
    /// <param name="n">Number of seconds</param>
    /// <returns>n as float</returns>
    [StashFn]
    public static double Seconds([StashParam(Type = "number")] double n) => n;

    /// <summary>Returns the number of seconds in n minutes.</summary>
    /// <param name="n">Number of minutes</param>
    /// <returns>Seconds as float</returns>
    [StashFn]
    public static double Minutes([StashParam(Type = "number")] double n) => n * 60.0;

    /// <summary>Returns the number of seconds in n hours.</summary>
    /// <param name="n">Number of hours</param>
    /// <returns>Seconds as float</returns>
    [StashFn]
    public static double Hours([StashParam(Type = "number")] double n) => n * 3600.0;

    /// <summary>Returns the number of seconds in n days.</summary>
    /// <param name="n">Number of days</param>
    /// <returns>Seconds as float</returns>
    [StashFn]
    public static double Days([StashParam(Type = "number")] double n) => n * 86400.0;

    /// <summary>Returns the number of seconds in n weeks.</summary>
    /// <param name="n">Number of weeks</param>
    /// <returns>Seconds as float</returns>
    [StashFn]
    public static double Weeks([StashParam(Type = "number")] double n) => n * 604800.0;

    // ── Utility Functions ────────────────────────────────────────────────────

    /// <summary>Returns the timestamp truncated to the start of the given unit (UTC).</summary>
    /// <param name="timestamp">Unix seconds</param>
    /// <param name="unit">One of: year, month, day, hour, minute</param>
    /// <exception cref="ValueError">if `unit` is not one of: year, month, day, hour, minute</exception>
    /// <returns>Start-of-unit Unix timestamp as float</returns>
    [StashFn]
    public static double StartOf([StashParam(Type = "number")] double timestamp, string unit)
    {
        var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestamp * 1000));
        DateTimeOffset result = unit switch
        {
            "year"   => new DateTimeOffset(dto.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "month"  => new DateTimeOffset(dto.Year, dto.Month, 1, 0, 0, 0, TimeSpan.Zero),
            "day"    => new DateTimeOffset(dto.Year, dto.Month, dto.Day, 0, 0, 0, TimeSpan.Zero),
            "hour"   => new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, 0, 0, TimeSpan.Zero),
            "minute" => new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, 0, TimeSpan.Zero),
            _ => throw new ValueError($"'time.startOf' unknown unit '{unit}'. Use: year, month, day, hour, minute"),
        };
        return result.ToUnixTimeMilliseconds() / 1000.0;
    }

    /// <summary>Returns the timestamp at the end of the given unit (last millisecond, UTC).</summary>
    /// <param name="timestamp">Unix seconds</param>
    /// <param name="unit">One of: year, month, day, hour, minute</param>
    /// <exception cref="ValueError">if `unit` is not one of: year, month, day, hour, minute</exception>
    /// <returns>End-of-unit Unix timestamp as float</returns>
    [StashFn]
    public static double EndOf([StashParam(Type = "number")] double timestamp, string unit)
    {
        var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(timestamp * 1000));
        DateTimeOffset result = unit switch
        {
            "year"   => new DateTimeOffset(dto.Year, 12, 31, 23, 59, 59, 999, TimeSpan.Zero),
            "month"  => new DateTimeOffset(dto.Year, dto.Month, DateTime.DaysInMonth(dto.Year, dto.Month), 23, 59, 59, 999, TimeSpan.Zero),
            "day"    => new DateTimeOffset(dto.Year, dto.Month, dto.Day, 23, 59, 59, 999, TimeSpan.Zero),
            "hour"   => new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, 59, 59, 999, TimeSpan.Zero),
            "minute" => new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, 59, 999, TimeSpan.Zero),
            _ => throw new ValueError($"'time.endOf' unknown unit '{unit}'. Use: year, month, day, hour, minute"),
        };
        return result.ToUnixTimeMilliseconds() / 1000.0;
    }

    /// <summary>Returns whether the year of a timestamp is a leap year (defaults to current year).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <exception cref="TypeError">if `timestamp` is not a number</exception>
    /// <returns>true if leap year</returns>
    [StashFn]
    public static bool IsLeapYear(params StashValue[] args)
    {
        var dt = GetDateTimeFromArgs("time.isLeapYear", args);
        return DateTime.IsLeapYear(dt.Year);
    }

    /// <summary>Returns the number of days in the month of a timestamp (defaults to current month).</summary>
    /// <param name="timestamp">Optional Unix timestamp</param>
    /// <exception cref="TypeError">if `timestamp` is not a number</exception>
    /// <returns>Days in month as int</returns>
    [StashFn]
    public static long DaysInMonth(params StashValue[] args)
    {
        var dt = GetDateTimeFromArgs("time.daysInMonth", args);
        return (long)DateTime.DaysInMonth(dt.Year, dt.Month);
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
