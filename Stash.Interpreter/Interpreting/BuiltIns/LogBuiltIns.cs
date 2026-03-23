namespace Stash.Interpreting.BuiltIns;

using System;
using System.Globalization;
using System.IO;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the <c>log</c> namespace built-in functions for structured, level-filtered logging.
/// </summary>
/// <remarks>
/// <para>
/// Provides leveled logging functions: <c>log.debug</c>, <c>log.info</c>, <c>log.warn</c>,
/// and <c>log.error</c>. The active minimum level is controlled via <c>log.setLevel</c>
/// (supported levels: <c>debug</c>, <c>info</c>, <c>warn</c>, <c>error</c>, <c>off</c>).
/// </para>
/// <para>
/// The output format is configurable via <c>log.setFormat</c> using <c>{time}</c>,
/// <c>{level}</c>, and <c>{msg}</c> placeholders. Log output can be redirected to a file
/// using <c>log.toFile</c>; otherwise messages are written to the interpreter's error output.
/// </para>
/// </remarks>
public static class LogBuiltIns
{
    /// <summary>The current minimum log level. Messages below this level are suppressed.</summary>
    private static string _level = "info";

    /// <summary>The current log message format template. Supports <c>{time}</c>, <c>{level}</c>, and <c>{msg}</c> placeholders.</summary>
    private static string _format = "[{time}] [{level}] {msg}";

    /// <summary>Optional file writer for redirecting log output. When <see langword="null"/>, output goes to <see cref="Interpreter.ErrorOutput"/>.</summary>
    private static TextWriter? _fileWriter;

    /// <summary>Ordered list of log level names, from least to most severe. <c>off</c> disables all logging.</summary>
    private static readonly string[] _levelOrder = ["debug", "info", "warn", "error", "off"];

    /// <summary>
    /// Returns the numeric index of the given level name within <see cref="_levelOrder"/>.
    /// </summary>
    /// <param name="level">The level name to look up.</param>
    /// <returns>The index of <paramref name="level"/>, or <c>1</c> (info) if not found.</returns>
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

    /// <summary>
    /// Determines whether a message at <paramref name="msgLevel"/> should be emitted under the current log level.
    /// </summary>
    /// <param name="msgLevel">The severity level of the message being tested.</param>
    /// <returns><see langword="true"/> if the message should be logged; otherwise <see langword="false"/>.</returns>
    private static bool ShouldLog(string msgLevel)
    {
        return GetLevelIndex(msgLevel) >= GetLevelIndex(_level);
    }

    /// <summary>
    /// Formats a log message by substituting <c>{time}</c>, <c>{level}</c>, and <c>{msg}</c> tokens in <see cref="_format"/>.
    /// </summary>
    /// <param name="level">The severity level label to embed in the formatted message.</param>
    /// <param name="msg">The message body.</param>
    /// <returns>The fully-formatted log string.</returns>
    private static string FormatMessage(string level, string msg)
    {
        var now = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return _format
            .Replace("{time}", now)
            .Replace("{level}", level.ToUpperInvariant())
            .Replace("{msg}", msg);
    }

    /// <summary>
    /// Registers all <c>log</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Stash.Interpreting.Environment"/> to register functions in.</param>
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var log = new StashNamespace("log");

        // log.debug(msg) — Logs 'msg' at the DEBUG level. No-op if the current level is above debug.
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

        // log.info(msg) — Logs 'msg' at the INFO level. No-op if the current level is above info.
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

        // log.warn(msg) — Logs 'msg' at the WARN level. No-op if the current level is above warn.
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

        // log.error(msg) — Logs 'msg' at the ERROR level. No-op if the current level is "off".
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

        // log.setLevel(level) — Sets the minimum log level. Messages below this level are silently discarded.
        //   Valid values: "debug", "info", "warn", "error", "off".
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

        // log.setFormat(template) — Sets the log message format template.
        //   Use {time}, {level}, and {msg} as placeholders. Default: "[{time}] [{level}] {msg}".
        log.Define("setFormat", new BuiltInFunction("log.setFormat", 1, (_, args) =>
        {
            if (args[0] is not string fmt)
            {
                throw new RuntimeError("Argument to 'log.setFormat' must be a string.");
            }

            _format = fmt;
            return null;
        }));

        // log.toFile(path) — Redirects all subsequent log output to an append-mode file at 'path'.
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

    /// <summary>
    /// Writes a formatted log message to the active output destination.
    /// </summary>
    /// <param name="interp">The current interpreter, used to access <see cref="Interpreter.ErrorOutput"/> when no file writer is set.</param>
    /// <param name="message">The fully-formatted log message to write.</param>
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
