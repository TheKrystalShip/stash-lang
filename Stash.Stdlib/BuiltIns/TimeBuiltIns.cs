namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Stash.Runtime;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

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
public static class TimeBuiltIns
{
    /// <summary>
    /// Registers all <c>time</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Stash.Interpreting.Environment"/> to register functions in.</param>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("time");

        // time.now() — Returns the current UTC time as a Unix timestamp (seconds since epoch, float).
        ns.Function("now", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            return StashValue.FromFloat(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        },
            returnType: "float",
            documentation: "Returns the current UTC time as a Unix timestamp (seconds since epoch).\n@return Current timestamp as float");

        // time.millis() — Returns the current UTC time as Unix milliseconds (integer).
        ns.Function("millis", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            return StashValue.FromInt(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        },
            returnType: "int",
            documentation: "Returns the current UTC time as Unix milliseconds.\n@return Current time in milliseconds");

        // time.sleep(seconds) — Suspends the current thread for the given number of seconds (float or int).
        ns.Function("sleep", [Param("seconds", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "void",
            documentation: "Suspends execution for the given number of seconds.\n@param seconds The duration to sleep (float or int)");

        // time.format(timestamp, format) — Formats a Unix timestamp using a .NET format string. Returns a string.
        ns.Function("format", [Param("timestamp", "number"), Param("format", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            double seconds = SvArgs.Numeric(args, 0, "time.format");
            var fmt = SvArgs.String(args, 1, "time.format");
            var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
            return StashValue.FromObj(dto.ToString(fmt, CultureInfo.InvariantCulture));
        },
            returnType: "string",
            documentation: "Formats a Unix timestamp using a .NET format string.\n@param timestamp Unix seconds\n@param format .NET format string\n@return Formatted date/time string");

        // time.parse(string, format) — Parses a date/time string using a .NET format string. Returns a Unix timestamp (float).
        ns.Function("parse", [Param("str", "string"), Param("format", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "float",
            documentation: "Parses a date string using a .NET format string. Returns a Unix timestamp.\n@param str The date string\n@param format The .NET format\n@return Unix timestamp as float");

        // time.date() — Returns today's UTC date as a string in "yyyy-MM-dd" format.
        ns.Function("date", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            return StashValue.FromObj(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        },
            returnType: "string",
            documentation: "Returns today's UTC date as a string in 'yyyy-MM-dd' format.\n@return Date string");

        // time.clock() — Returns a high-resolution monotonic timestamp in seconds (float). Useful for benchmarking.
        ns.Function("clock", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            return StashValue.FromFloat((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        },
            returnType: "float",
            documentation: "Returns a high-resolution monotonic timestamp in seconds. Suitable for benchmarking.\n@return Seconds as float");

        // time.iso() — Returns the current UTC time as an ISO 8601 string (e.g. "2024-01-15T12:30:00.0000000+00:00").
        ns.Function("iso", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            return StashValue.FromObj(DateTimeOffset.UtcNow.ToString("o"));
        },
            returnType: "string",
            documentation: "Returns the current UTC time as an ISO 8601 string.\n@return ISO 8601 formatted string");

        // ── Time decomposition functions ────────────────────────────────────

        // time.year(timestamp?) — Returns the UTC year component of a timestamp (or now if omitted). Returns an integer.
        ns.Function("year", [Param("timestamp", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dt = GetDateTimeFromArgs("time.year", args);
            return StashValue.FromInt((long)dt.Year);
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC year of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Year as int");

        // time.month(timestamp?) — Returns the UTC month (1–12) of a timestamp (or now if omitted). Returns an integer.
        ns.Function("month", [Param("timestamp", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dt = GetDateTimeFromArgs("time.month", args);
            return StashValue.FromInt((long)dt.Month);
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC month (1-12) of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Month as int");

        // time.day(timestamp?) — Returns the UTC day-of-month (1–31) of a timestamp (or now if omitted). Returns an integer.
        ns.Function("day", [Param("timestamp", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dt = GetDateTimeFromArgs("time.day", args);
            return StashValue.FromInt((long)dt.Day);
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC day of month (1-31) of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Day as int");

        // time.hour(timestamp?) — Returns the UTC hour (0–23) of a timestamp (or now if omitted). Returns an integer.
        ns.Function("hour", [Param("timestamp", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dt = GetDateTimeFromArgs("time.hour", args);
            return StashValue.FromInt((long)dt.Hour);
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC hour (0-23) of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Hour as int");

        // time.minute(timestamp?) — Returns the UTC minute (0–59) of a timestamp (or now if omitted). Returns an integer.
        ns.Function("minute", [Param("timestamp", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dt = GetDateTimeFromArgs("time.minute", args);
            return StashValue.FromInt((long)dt.Minute);
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC minute (0-59) of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Minute as int");

        // time.second(timestamp?) — Returns the UTC second (0–59) of a timestamp (or now if omitted). Returns an integer.
        ns.Function("second", [Param("timestamp", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dt = GetDateTimeFromArgs("time.second", args);
            return StashValue.FromInt((long)dt.Second);
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC second (0-59) of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Second as int");

        // time.dayOfWeek(timestamp?) — Returns the UTC day-of-week name in lowercase (e.g. "monday") for a timestamp (or now if omitted).
        ns.Function("dayOfWeek", [Param("timestamp", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dt = GetDateTimeFromArgs("time.dayOfWeek", args);
            return StashValue.FromObj(dt.DayOfWeek.ToString().ToLowerInvariant());
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Returns the UTC day of week as lowercase string (e.g. 'monday') of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Day of week string");

        // time.add(timestamp, seconds) — Adds a number of seconds to a Unix timestamp. Returns the new timestamp (float).
        ns.Function("add", [Param("timestamp", "number"), Param("seconds", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            double ts = SvArgs.Numeric(args, 0, "time.add");
            double seconds = SvArgs.Numeric(args, 1, "time.add");
            return StashValue.FromFloat(ts + seconds);
        },
            returnType: "float",
            documentation: "Adds seconds to a Unix timestamp and returns the new timestamp.\n@param timestamp The base timestamp\n@param seconds Number of seconds to add\n@return New timestamp as float");

        // time.diff(timestamp1, timestamp2) — Returns the absolute difference in seconds between two Unix timestamps (float).
        ns.Function("diff", [Param("timestamp1", "number"), Param("timestamp2", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            double ts1 = SvArgs.Numeric(args, 0, "time.diff");
            double ts2 = SvArgs.Numeric(args, 1, "time.diff");
            return StashValue.FromFloat(Math.Abs(ts1 - ts2));
        },
            returnType: "float",
            documentation: "Returns the absolute difference in seconds between two Unix timestamps.\n@param timestamp1 First timestamp\n@param timestamp2 Second timestamp\n@return Absolute difference in seconds");

        // ── Timezone Functions ───────────────────────────────────────────────

        // time.toTimezone(timestamp, timezone) — Converts a UTC timestamp to a specific timezone.
        ns.Function("toTimezone", [Param("timestamp", "number"), Param("timezone", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "float",
            documentation: "Converts a UTC timestamp to the local time in a timezone by applying the UTC offset.\n@param timestamp Unix seconds (UTC)\n@param timezone IANA or Windows timezone ID\n@return Offset-adjusted Unix timestamp as float");

        // time.toUTC(timestamp, timezone) — Converts a local-time timestamp (in the given timezone) back to UTC.
        ns.Function("toUTC", [Param("timestamp", "number"), Param("timezone", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            double ts = SvArgs.Numeric(args, 0, "time.toUTC");
            var timezone = SvArgs.String(args, 1, "time.toUTC");
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
            catch (TimeZoneNotFoundException)
            {
                throw new RuntimeError($"'time.toUTC' unknown timezone '{timezone}'. Use time.timezones() to list available IDs.", errorType: StashErrorTypes.ValueError);
            }
            // Interpret ts as local time in the given timezone
            var localAsUnspecified = DateTime.SpecifyKind(
                DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000)).UtcDateTime,
                DateTimeKind.Unspecified);
            var offset = tz.GetUtcOffset(localAsUnspecified);
            return StashValue.FromFloat(ts - offset.TotalSeconds);
        },
            returnType: "float",
            documentation: "Interprets a timestamp as local time in the given timezone and returns the UTC equivalent.\n@param timestamp Unix seconds (local time)\n@param timezone IANA or Windows timezone ID\n@return UTC Unix timestamp as float");

        // time.timezone() — Returns the local system timezone ID as a string.
        ns.Function("timezone", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            return StashValue.FromObj(TimeZoneInfo.Local.Id);
        },
            returnType: "string",
            documentation: "Returns the local system timezone ID.\n@return Timezone ID string (e.g. 'America/New_York')");

        // time.timezones() — Returns an array of all available timezone IDs.
        ns.Function("timezones", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            var list = TimeZoneInfo.GetSystemTimeZones().Select(z => StashValue.FromObj(z.Id)).ToList();
            return StashValue.FromObj(list);
        },
            returnType: "array",
            documentation: "Returns an array of all available timezone IDs.\n@return Array of timezone ID strings");

        // time.offset(timestamp, timezone) — Returns the UTC offset in hours for a timezone at a specific time.
        ns.Function("offset", [Param("timestamp", "number"), Param("timezone", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "float",
            documentation: "Returns the UTC offset in hours for a timezone at a specific timestamp.\n@param timestamp Unix seconds\n@param timezone IANA or Windows timezone ID\n@return UTC offset in hours (e.g. -5.0 for EST, 5.5 for IST)");

        // ── Duration Convenience Helpers ─────────────────────────────────────

        // time.seconds(n) — Returns n seconds (identity, for consistency with other helpers). Returns float.
        ns.Function("seconds", [Param("n", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            double n = SvArgs.Numeric(args, 0, "time.seconds");
            return StashValue.FromFloat(n);
        },
            returnType: "float",
            documentation: "Returns n seconds (identity, for consistency). Useful with time.add().\n@param n Number of seconds\n@return n as float");

        // time.minutes(n) — Returns n * 60 seconds. Returns float.
        ns.Function("minutes", [Param("n", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            double n = SvArgs.Numeric(args, 0, "time.minutes");
            return StashValue.FromFloat(n * 60.0);
        },
            returnType: "float",
            documentation: "Returns the number of seconds in n minutes.\n@param n Number of minutes\n@return Seconds as float");

        // time.hours(n) — Returns n * 3600 seconds. Returns float.
        ns.Function("hours", [Param("n", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            double n = SvArgs.Numeric(args, 0, "time.hours");
            return StashValue.FromFloat(n * 3600.0);
        },
            returnType: "float",
            documentation: "Returns the number of seconds in n hours.\n@param n Number of hours\n@return Seconds as float");

        // time.days(n) — Returns n * 86400 seconds. Returns float.
        ns.Function("days", [Param("n", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            double n = SvArgs.Numeric(args, 0, "time.days");
            return StashValue.FromFloat(n * 86400.0);
        },
            returnType: "float",
            documentation: "Returns the number of seconds in n days.\n@param n Number of days\n@return Seconds as float");

        // time.weeks(n) — Returns n * 604800 seconds. Returns float.
        ns.Function("weeks", [Param("n", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            double n = SvArgs.Numeric(args, 0, "time.weeks");
            return StashValue.FromFloat(n * 604800.0);
        },
            returnType: "float",
            documentation: "Returns the number of seconds in n weeks.\n@param n Number of weeks\n@return Seconds as float");

        // ── Utility Functions ────────────────────────────────────────────────

        // time.startOf(timestamp, unit) — Returns the timestamp truncated to the start of the given unit.
        ns.Function("startOf", [Param("timestamp", "number"), Param("unit", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "float",
            documentation: "Returns the timestamp truncated to the start of the given unit (UTC).\n@param timestamp Unix seconds\n@param unit One of: year, month, day, hour, minute\n@return Start-of-unit Unix timestamp as float");

        // time.endOf(timestamp, unit) — Returns the timestamp at the end of the given unit (last millisecond).
        ns.Function("endOf", [Param("timestamp", "number"), Param("unit", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "float",
            documentation: "Returns the timestamp at the end of the given unit (last millisecond, UTC).\n@param timestamp Unix seconds\n@param unit One of: year, month, day, hour, minute\n@return End-of-unit Unix timestamp as float");

        // time.isLeapYear(timestamp?) — Returns whether the year is a leap year. Variadic — no args means current year.
        ns.Function("isLeapYear", [Param("timestamp", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dt = GetDateTimeFromArgs("time.isLeapYear", args);
            return StashValue.FromBool(DateTime.IsLeapYear(dt.Year));
        },
            returnType: "bool",
            isVariadic: true,
            documentation: "Returns whether the year of a timestamp is a leap year (defaults to current year).\n@param timestamp Optional Unix timestamp\n@return true if leap year");

        // time.daysInMonth(timestamp?) — Returns the number of days in the month of the timestamp. Variadic — no args means current month.
        ns.Function("daysInMonth", [Param("timestamp", "number")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dt = GetDateTimeFromArgs("time.daysInMonth", args);
            return StashValue.FromInt((long)DateTime.DaysInMonth(dt.Year, dt.Month));
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the number of days in the month of a timestamp (defaults to current month).\n@param timestamp Optional Unix timestamp\n@return Days in month as int");

        return ns.Build();
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
