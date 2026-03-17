namespace Stash.Interpreting.BuiltIns;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Stash.Interpreting.Types;

/// <summary>Registers the <c>time</c> namespace providing time-related functions (now, millis, sleep, format, parse, date, clock, iso).</summary>
public static class TimeBuiltIns
{
    /// <summary>Registers the <c>time</c> namespace and all its functions into the global environment.</summary>
    /// <param name="globals">The global environment to register into.</param>
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var ns = new StashNamespace("time");

        ns.Define("now", new BuiltInFunction("time.now", 0, (interp, args) =>
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        }));

        ns.Define("millis", new BuiltInFunction("time.millis", 0, (interp, args) =>
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }));

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

        ns.Define("date", new BuiltInFunction("time.date", 0, (interp, args) =>
        {
            return DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }));

        ns.Define("clock", new BuiltInFunction("time.clock", 0, (interp, args) =>
        {
            return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        }));

        ns.Define("iso", new BuiltInFunction("time.iso", 0, (interp, args) =>
        {
            return DateTimeOffset.UtcNow.ToString("o");
        }));

        globals.Define("time", ns);
    }
}
