namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
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
        ns.Function("now", [], (_, _) =>
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        },
            returnType: "float",
            documentation: "Returns the current UTC time as a Unix timestamp (seconds since epoch).\n@return Current timestamp as float");

        // time.millis() — Returns the current UTC time as Unix milliseconds (integer).
        ns.Function("millis", [], (_, _) =>
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        },
            returnType: "int",
            documentation: "Returns the current UTC time as Unix milliseconds.\n@return Current time in milliseconds");

        // time.sleep(seconds) — Suspends the current thread for the given number of seconds (float or int).
        ns.Function("sleep", [Param("seconds", "number")], (_, args) =>
        {
            int ms;
            if (args[0] is long l)
            {
                ms = (int)(l * 1000);
            }
            else if (args[0] is double d)
            {
                ms = (int)(d * 1000);
            }
            else
            {
                throw new RuntimeError("Argument to 'time.sleep' must be a number.");
            }

            Thread.Sleep(ms);
            return null;
        },
            returnType: "void",
            documentation: "Suspends execution for the given number of seconds.\n@param seconds The duration to sleep (float or int)");

        // time.format(timestamp, format) — Formats a Unix timestamp using a .NET format string. Returns a string.
        ns.Function("format", [Param("timestamp", "number"), Param("format", "string")], (_, args) =>
        {
            double seconds;
            if (args[0] is long l)
            {
                seconds = (double)l;
            }
            else if (args[0] is double d)
            {
                seconds = d;
            }
            else
            {
                throw new RuntimeError("First argument to 'time.format' must be a number.");
            }

            var fmt = Args.String(args, 1, "time.format");

            var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
            return dto.ToString(fmt, CultureInfo.InvariantCulture);
        },
            returnType: "string",
            documentation: "Formats a Unix timestamp using a .NET format string.\n@param timestamp Unix seconds\n@param format .NET format string\n@return Formatted date/time string");

        // time.parse(string, format) — Parses a date/time string using a .NET format string. Returns a Unix timestamp (float).
        ns.Function("parse", [Param("str", "string"), Param("format", "string")], (_, args) =>
        {
            var str = Args.String(args, 0, "time.parse");
            var fmt = Args.String(args, 1, "time.parse");

            try
            {
                var dto = DateTimeOffset.ParseExact(str, fmt, CultureInfo.InvariantCulture);
                return dto.ToUnixTimeMilliseconds() / 1000.0;
            }
            catch (FormatException)
            {
                throw new RuntimeError($"'time.parse' could not parse \"{str}\" with format \"{fmt}\".");
            }
        },
            returnType: "float",
            documentation: "Parses a date string using a .NET format string. Returns a Unix timestamp.\n@param str The date string\n@param format The .NET format\n@return Unix timestamp as float");

        // time.date() — Returns today's UTC date as a string in "yyyy-MM-dd" format.
        ns.Function("date", [], (_, _) =>
        {
            return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        },
            returnType: "string",
            documentation: "Returns today's UTC date as a string in 'yyyy-MM-dd' format.\n@return Date string");

        // time.clock() — Returns a high-resolution monotonic timestamp in seconds (float). Useful for benchmarking.
        ns.Function("clock", [], (_, _) =>
        {
            return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        },
            returnType: "float",
            documentation: "Returns a high-resolution monotonic timestamp in seconds. Suitable for benchmarking.\n@return Seconds as float");

        // time.iso() — Returns the current UTC time as an ISO 8601 string (e.g. "2024-01-15T12:30:00.0000000+00:00").
        ns.Function("iso", [], (_, _) =>
        {
            return DateTimeOffset.UtcNow.ToString("o");
        },
            returnType: "string",
            documentation: "Returns the current UTC time as an ISO 8601 string.\n@return ISO 8601 formatted string");

        // ── Time decomposition functions ────────────────────────────────────

        // time.year(timestamp?) — Returns the UTC year component of a timestamp (or now if omitted). Returns an integer.
        ns.Function("year", [Param("timestamp", "number")], (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.year", args);
            return (long)dt.Year;
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC year of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Year as int");

        // time.month(timestamp?) — Returns the UTC month (1–12) of a timestamp (or now if omitted). Returns an integer.
        ns.Function("month", [Param("timestamp", "number")], (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.month", args);
            return (long)dt.Month;
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC month (1-12) of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Month as int");

        // time.day(timestamp?) — Returns the UTC day-of-month (1–31) of a timestamp (or now if omitted). Returns an integer.
        ns.Function("day", [Param("timestamp", "number")], (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.day", args);
            return (long)dt.Day;
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC day of month (1-31) of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Day as int");

        // time.hour(timestamp?) — Returns the UTC hour (0–23) of a timestamp (or now if omitted). Returns an integer.
        ns.Function("hour", [Param("timestamp", "number")], (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.hour", args);
            return (long)dt.Hour;
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC hour (0-23) of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Hour as int");

        // time.minute(timestamp?) — Returns the UTC minute (0–59) of a timestamp (or now if omitted). Returns an integer.
        ns.Function("minute", [Param("timestamp", "number")], (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.minute", args);
            return (long)dt.Minute;
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC minute (0-59) of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Minute as int");

        // time.second(timestamp?) — Returns the UTC second (0–59) of a timestamp (or now if omitted). Returns an integer.
        ns.Function("second", [Param("timestamp", "number")], (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.second", args);
            return (long)dt.Second;
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns the UTC second (0-59) of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Second as int");

        // time.dayOfWeek(timestamp?) — Returns the UTC day-of-week name in lowercase (e.g. "monday") for a timestamp (or now if omitted).
        ns.Function("dayOfWeek", [Param("timestamp", "number")], (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.dayOfWeek", args);
            return dt.DayOfWeek.ToString().ToLowerInvariant();
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Returns the UTC day of week as lowercase string (e.g. 'monday') of a timestamp (or now if omitted).\n@param timestamp Optional Unix timestamp\n@return Day of week string");

        // time.add(timestamp, seconds) — Adds a number of seconds to a Unix timestamp. Returns the new timestamp (float).
        ns.Function("add", [Param("timestamp", "number"), Param("seconds", "number")], (_, args) =>
        {
            double ts;
            if (args[0] is long l)
            {
                ts = (double)l;
            }
            else if (args[0] is double d)
            {
                ts = d;
            }
            else
            {
                throw new RuntimeError("First argument to 'time.add' must be a number (timestamp).");
            }

            double seconds;
            if (args[1] is long ls)
            {
                seconds = (double)ls;
            }
            else if (args[1] is double ds)
            {
                seconds = ds;
            }
            else
            {
                throw new RuntimeError("Second argument to 'time.add' must be a number (seconds).");
            }

            return ts + seconds;
        },
            returnType: "float",
            documentation: "Adds seconds to a Unix timestamp and returns the new timestamp.\n@param timestamp The base timestamp\n@param seconds Number of seconds to add\n@return New timestamp as float");

        // time.diff(timestamp1, timestamp2) — Returns the absolute difference in seconds between two Unix timestamps (float).
        ns.Function("diff", [Param("timestamp1", "number"), Param("timestamp2", "number")], (_, args) =>
        {
            double ts1;
            if (args[0] is long l1)
            {
                ts1 = (double)l1;
            }
            else if (args[0] is double d1)
            {
                ts1 = d1;
            }
            else
            {
                throw new RuntimeError("First argument to 'time.diff' must be a number (timestamp).");
            }

            double ts2;
            if (args[1] is long l2)
            {
                ts2 = (double)l2;
            }
            else if (args[1] is double d2)
            {
                ts2 = d2;
            }
            else
            {
                throw new RuntimeError("Second argument to 'time.diff' must be a number (timestamp).");
            }

            return Math.Abs(ts1 - ts2);
        },
            returnType: "float",
            documentation: "Returns the absolute difference in seconds between two Unix timestamps.\n@param timestamp1 First timestamp\n@param timestamp2 Second timestamp\n@return Absolute difference in seconds");

        return ns.Build();
    }

    private static DateTimeOffset GetDateTimeFromArgs(string funcName, List<object?> args)
    {
        if (args.Count > 1)
        {
            throw new RuntimeError($"'{funcName}' expects 0 or 1 arguments.");
        }

        if (args.Count == 0)
        {
            return DateTimeOffset.UtcNow;
        }

        double seconds;
        if (args[0] is long l)
        {
            seconds = (double)l;
        }
        else if (args[0] is double d)
        {
            seconds = d;
        }
        else
        {
            throw new RuntimeError($"Argument to '{funcName}' must be a number (timestamp).");
        }

        return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
    }
}
