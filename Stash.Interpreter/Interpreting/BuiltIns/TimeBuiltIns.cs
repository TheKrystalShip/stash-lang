namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Stash.Interpreting.Types;

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
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var ns = new StashNamespace("time");

        // time.now() — Returns the current UTC time as a Unix timestamp (seconds since epoch, float).
        ns.Define("now", new BuiltInFunction("time.now", 0, (interp, args) =>
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }));

        // time.millis() — Returns the current UTC time as Unix milliseconds (integer).
        ns.Define("millis", new BuiltInFunction("time.millis", 0, (interp, args) =>
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }));

        // time.sleep(seconds) — Suspends the current thread for the given number of seconds (float or int).
        ns.Define("sleep", new BuiltInFunction("time.sleep", 1, (interp, args) =>
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
        }));

        // time.format(timestamp, format) — Formats a Unix timestamp using a .NET format string. Returns a string.
        ns.Define("format", new BuiltInFunction("time.format", 2, (interp, args) =>
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

            if (args[1] is not string fmt)
            {
                throw new RuntimeError("Second argument to 'time.format' must be a string.");
            }

            var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
            return dto.ToString(fmt, CultureInfo.InvariantCulture);
        }));

        // time.parse(string, format) — Parses a date/time string using a .NET format string. Returns a Unix timestamp (float).
        ns.Define("parse", new BuiltInFunction("time.parse", 2, (interp, args) =>
        {
            if (args[0] is not string str)
            {
                throw new RuntimeError("First argument to 'time.parse' must be a string.");
            }

            if (args[1] is not string fmt)
            {
                throw new RuntimeError("Second argument to 'time.parse' must be a string.");
            }

            try
            {
                var dto = DateTimeOffset.ParseExact(str, fmt, CultureInfo.InvariantCulture);
                return dto.ToUnixTimeMilliseconds() / 1000.0;
            }
            catch (FormatException)
            {
                throw new RuntimeError($"'time.parse' could not parse \"{str}\" with format \"{fmt}\".");
            }
        }));

        // time.date() — Returns today's UTC date as a string in "yyyy-MM-dd" format.
        ns.Define("date", new BuiltInFunction("time.date", 0, (interp, args) =>
        {
            return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }));

        // time.clock() — Returns a high-resolution monotonic timestamp in seconds (float). Useful for benchmarking.
        ns.Define("clock", new BuiltInFunction("time.clock", 0, (interp, args) =>
        {
            return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        }));

        // time.iso() — Returns the current UTC time as an ISO 8601 string (e.g. "2024-01-15T12:30:00.0000000+00:00").
        ns.Define("iso", new BuiltInFunction("time.iso", 0, (interp, args) =>
        {
            return DateTimeOffset.UtcNow.ToString("o");
        }));

        // ── Time decomposition functions ─────────────────────────────────

        // time.year(timestamp?) — Returns the UTC year component of a timestamp (or now if omitted). Returns an integer.
        ns.Define("year", new BuiltInFunction("time.year", -1, (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.year", args);
            return (long)dt.Year;
        }));

        // time.month(timestamp?) — Returns the UTC month (1–12) of a timestamp (or now if omitted). Returns an integer.
        ns.Define("month", new BuiltInFunction("time.month", -1, (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.month", args);
            return (long)dt.Month;
        }));

        // time.day(timestamp?) — Returns the UTC day-of-month (1–31) of a timestamp (or now if omitted). Returns an integer.
        ns.Define("day", new BuiltInFunction("time.day", -1, (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.day", args);
            return (long)dt.Day;
        }));

        // time.hour(timestamp?) — Returns the UTC hour (0–23) of a timestamp (or now if omitted). Returns an integer.
        ns.Define("hour", new BuiltInFunction("time.hour", -1, (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.hour", args);
            return (long)dt.Hour;
        }));

        // time.minute(timestamp?) — Returns the UTC minute (0–59) of a timestamp (or now if omitted). Returns an integer.
        ns.Define("minute", new BuiltInFunction("time.minute", -1, (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.minute", args);
            return (long)dt.Minute;
        }));

        // time.second(timestamp?) — Returns the UTC second (0–59) of a timestamp (or now if omitted). Returns an integer.
        ns.Define("second", new BuiltInFunction("time.second", -1, (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.second", args);
            return (long)dt.Second;
        }));

        // time.dayOfWeek(timestamp?) — Returns the UTC day-of-week name in lowercase (e.g. "monday") for a timestamp (or now if omitted).
        ns.Define("dayOfWeek", new BuiltInFunction("time.dayOfWeek", -1, (_, args) =>
        {
            var dt = GetDateTimeFromArgs("time.dayOfWeek", args);
            return dt.DayOfWeek.ToString().ToLowerInvariant();
        }));

        // time.add(timestamp, seconds) — Adds a number of seconds to a Unix timestamp. Returns the new timestamp (float).
        ns.Define("add", new BuiltInFunction("time.add", 2, (_, args) =>
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
        }));

        // time.diff(timestamp1, timestamp2) — Returns the absolute difference in seconds between two Unix timestamps (float).
        ns.Define("diff", new BuiltInFunction("time.diff", 2, (_, args) =>
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
        }));

        globals.Define("time", ns);
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
