namespace Stash.Interpreting.BuiltIns;

using System;
using System.Globalization;
using System.IO;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'log' namespace providing structured logging (debug, info, warn, error, setLevel, setFormat, toFile).
/// </summary>
public static class LogBuiltIns
{
    private static string _level = "info";
    private static string _format = "[{time}] [{level}] {msg}";
    private static TextWriter? _fileWriter;

    private static readonly string[] _levelOrder = ["debug", "info", "warn", "error", "off"];

    private static int GetLevelIndex(string level)
    {
        for (int i = 0; i < _levelOrder.Length; i++)
        {
            if (_levelOrder[i] == level)
            {
                return i;
            }
        }

        return 1; // default to info
    }

    private static bool ShouldLog(string msgLevel)
    {
        return GetLevelIndex(msgLevel) >= GetLevelIndex(_level);
    }

    private static string FormatMessage(string level, string msg)
    {
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return _format
            .Replace("{time}", now)
            .Replace("{level}", level.ToUpperInvariant())
            .Replace("{msg}", msg);
    }

    public static void Register(Stash.Interpreting.Environment globals)
    {
        var log = new StashNamespace("log");

        log.Define("debug", new BuiltInFunction("log.debug", 1, (interp, args) =>
        {
            if (!ShouldLog("debug"))
            {
                return null;
            }

            string text = RuntimeValues.Stringify(args[0]);
            string formatted = FormatMessage("debug", text);
            WriteLog(interp, formatted);
            return null;
        }));

        log.Define("info", new BuiltInFunction("log.info", 1, (interp, args) =>
        {
            if (!ShouldLog("info"))
            {
                return null;
            }

            string text = RuntimeValues.Stringify(args[0]);
            string formatted = FormatMessage("info", text);
            WriteLog(interp, formatted);
            return null;
        }));

        log.Define("warn", new BuiltInFunction("log.warn", 1, (interp, args) =>
        {
            if (!ShouldLog("warn"))
            {
                return null;
            }

            string text = RuntimeValues.Stringify(args[0]);
            string formatted = FormatMessage("warn", text);
            WriteLog(interp, formatted);
            return null;
        }));

        log.Define("error", new BuiltInFunction("log.error", 1, (interp, args) =>
        {
            if (!ShouldLog("error"))
            {
                return null;
            }

            string text = RuntimeValues.Stringify(args[0]);
            string formatted = FormatMessage("error", text);
            WriteLog(interp, formatted);
            return null;
        }));

        log.Define("setLevel", new BuiltInFunction("log.setLevel", 1, (_, args) =>
        {
            if (args[0] is not string level)
            {
                throw new RuntimeError("Argument to 'log.setLevel' must be a string.");
            }

            string normalized = level.ToLowerInvariant();
            if (Array.IndexOf(_levelOrder, normalized) < 0)
            {
                throw new RuntimeError("'log.setLevel' level must be one of: \"debug\", \"info\", \"warn\", \"error\", \"off\".");
            }

            _level = normalized;
            return null;
        }));

        log.Define("setFormat", new BuiltInFunction("log.setFormat", 1, (_, args) =>
        {
            if (args[0] is not string fmt)
            {
                throw new RuntimeError("Argument to 'log.setFormat' must be a string.");
            }

            _format = fmt;
            return null;
        }));

        log.Define("toFile", new BuiltInFunction("log.toFile", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'log.toFile' must be a string.");
            }

            _fileWriter?.Dispose();
            _fileWriter = new StreamWriter(path, append: true) { AutoFlush = true };
            return null;
        }));

        globals.Define("log", log);
    }

    private static void WriteLog(Interpreter interp, string message)
    {
        if (_fileWriter != null)
        {
            _fileWriter.WriteLine(message);
        }
        else
        {
            interp.ErrorOutput.WriteLine(message);
        }
    }
}
