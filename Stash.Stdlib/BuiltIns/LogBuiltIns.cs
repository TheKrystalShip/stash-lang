namespace Stash.Stdlib.BuiltIns;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>Registers the <c>log</c> namespace providing structured logging with levels, timestamps, and text/JSON output.</summary>
public static class LogBuiltIns
{
    // ── Level constants ───────────────────────────────────────────────────────

    private const int LevelDebug = 0;
    private const int LevelInfo  = 1;
    private const int LevelWarn  = 2;
    private const int LevelError = 3;

    private static readonly string[] LevelNames = ["DEBUG", "INFO ", "WARN ", "ERROR"];
    private static readonly string[] LevelNamesJson = ["DEBUG", "INFO", "WARN", "ERROR"];

    // ── Public API ────────────────────────────────────────────────────────────

    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("log");

        ns.Function("debug", [Param("message", "string"), Param("data", "any")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                Emit(ctx, LevelDebug, args);
                return StashValue.Null;
            },
            isVariadic: true,
            returnType: "null",
            documentation: "Logs a message at DEBUG level. Suppressed unless log level is set to 'debug'.\n@param message The log message\n@param data Optional extra fields (dict) or a scalar value\n@return null");

        ns.Function("info", [Param("message", "string"), Param("data", "any")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                Emit(ctx, LevelInfo, args);
                return StashValue.Null;
            },
            isVariadic: true,
            returnType: "null",
            documentation: "Logs a message at INFO level.\n@param message The log message\n@param data Optional extra fields (dict) or a scalar value\n@return null");

        ns.Function("warn", [Param("message", "string"), Param("data", "any")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                Emit(ctx, LevelWarn, args);
                return StashValue.Null;
            },
            isVariadic: true,
            returnType: "null",
            documentation: "Logs a message at WARN level.\n@param message The log message\n@param data Optional extra fields (dict) or a scalar value\n@return null");

        ns.Function("error", [Param("message", "string"), Param("data", "any")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                Emit(ctx, LevelError, args);
                return StashValue.Null;
            },
            isVariadic: true,
            returnType: "null",
            documentation: "Logs a message at ERROR level.\n@param message The log message\n@param data Optional extra fields (dict) or a scalar value\n@return null");

        ns.Function("setLevel", [Param("level", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                var level = SvArgs.String(args, 0, "log.setLevel");
                int parsed = ParseLevel(level);
                ctx.LoggerState.Level = parsed;
                return StashValue.Null;
            },
            returnType: "null",
            documentation: "Sets the minimum log level. Messages below this level are suppressed.\n@param level One of: 'debug', 'info', 'warn', 'error'\n@return null");

        ns.Function("setFormat", [Param("format", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                var format = SvArgs.String(args, 0, "log.setFormat");
                if (format != "text" && format != "json")
                    throw new RuntimeError($"log.setFormat: unknown format '{format}'. Expected 'text' or 'json'.", errorType: StashErrorTypes.ValueError);
                ctx.LoggerState.Format = format;
                return StashValue.Null;
            },
            returnType: "null",
            documentation: "Sets the output format.\n@param format 'text' (default) or 'json'\n@return null");

        ns.Function("setOutput", [Param("target", "string")],
            (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                var target = SvArgs.String(args, 0, "log.setOutput");
                if (target != "stdout" && target != "stderr")
                {
                    try { ctx.LoggerState.SetFileOutput(target); }
                    catch (Exception ex) { throw new RuntimeError($"log.setOutput: failed to open file '{target}': {ex.Message}", errorType: StashErrorTypes.IOError); }
                }
                else
                {
                    ctx.LoggerState.ClearFileOutput();
                    ctx.LoggerState.Output = target;
                }
                return StashValue.Null;
            },
            returnType: "null",
            documentation: "Sets the log output destination.\n@param target 'stdout', 'stderr', or a file path\n@return null");

        ns.Function("withFields", [Param("fields", "dict")],
            static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
            {
                var fields = SvArgs.Dict(args, 0, "log.withFields");
                return StashValue.FromObj(BuildScopedLogger(fields));
            },
            returnType: "dict",
            documentation: "Returns a scoped logger dict with preset fields merged into every log entry.\n@param fields A dictionary of fields to attach to all log messages\n@return A logger dict with debug/info/warn/error methods");

        return ns.Build();
    }

    // ── Core emit logic ───────────────────────────────────────────────────────

    internal static void Emit(IInterpreterContext ctx, int level, ReadOnlySpan<StashValue> args,
        StashDictionary? presetFields = null)
    {
        var state = ctx.LoggerState;
        int currentLevel = state.Level;
        string format = state.Format;
        string output = state.Output;
        TextWriter? fileWriter = state.FileWriter;

        if (level < currentLevel)
            return;

        if (args.Length == 0)
            throw new RuntimeError("log functions require at least a message argument.");

        string message = SvArgs.String(args, 0, LevelNamesJson[level]);

        // Collect extra fields
        StashDictionary? dataDict = null;
        string? dataScalar = null;

        if (args.Length >= 2)
        {
            var dataArg = args[1];
            if (dataArg.IsObj && dataArg.AsObj is StashDictionary d)
                dataDict = d;
            else if (!dataArg.IsNull)
                dataScalar = RuntimeValues.Stringify(dataArg.ToObject());
        }

        string line = format == "json"
            ? FormatJson(level, message, presetFields, dataDict, dataScalar)
            : FormatText(level, message, presetFields, dataDict, dataScalar);

        TextWriter writer = GetWriter(ctx, output, fileWriter);
        writer.WriteLine(line);

        string channel = output == "stdout" ? "stdout" : "stderr";
        if (fileWriter is null)
            ctx.NotifyOutput(channel, line + "\n");
    }

    private static TextWriter GetWriter(IInterpreterContext ctx, string output, TextWriter? fileWriter)
    {
        if (fileWriter is not null)
            return fileWriter;
        return output == "stdout" ? ctx.Output : ctx.ErrorOutput;
    }

    // ── Text formatting ───────────────────────────────────────────────────────

    private static string FormatText(int level, string message, StashDictionary? preset,
        StashDictionary? dataDict, string? dataScalar)
    {
        string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(ts);
        sb.Append("] ");
        sb.Append(LevelNames[level]);
        sb.Append(' ');
        sb.Append(message);

        AppendTextFields(sb, preset);
        AppendTextFields(sb, dataDict);

        if (dataScalar is not null)
        {
            sb.Append(' ');
            sb.Append("data=");
            sb.Append(dataScalar);
        }

        return sb.ToString();
    }

    private static void AppendTextFields(StringBuilder sb, StashDictionary? dict)
    {
        if (dict is null) return;
        foreach (object key in dict.RawKeys())
        {
            string k = key.ToString() ?? "";
            string v = RuntimeValues.Stringify(dict.Get(key).ToObject());
            sb.Append(' ');
            sb.Append(k);
            sb.Append('=');
            // Quote values with spaces
            if (v.Contains(' ') || v.Contains('"'))
            {
                sb.Append('"');
                sb.Append(v.Replace("\"", "\\\""));
                sb.Append('"');
            }
            else
            {
                sb.Append(v);
            }
        }
    }

    // ── JSON formatting ───────────────────────────────────────────────────────

    private static string FormatJson(int level, string message, StashDictionary? preset,
        StashDictionary? dataDict, string? dataScalar)
    {
        string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var buf = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buf);

        writer.WriteStartObject();
        writer.WriteString("ts", ts);
        writer.WriteString("level", LevelNamesJson[level]);
        writer.WriteString("msg", message);

        WriteJsonFields(writer, preset);
        WriteJsonFields(writer, dataDict);

        if (dataScalar is not null)
            writer.WriteString("data", dataScalar);

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    private static void WriteJsonFields(Utf8JsonWriter writer, StashDictionary? dict)
    {
        if (dict is null) return;
        foreach (object key in dict.RawKeys())
        {
            string k = key.ToString() ?? "";
            object? val = dict.Get(key).ToObject();
            WriteJsonValue(writer, k, val);
        }
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, string key, object? val)
    {
        switch (val)
        {
            case null:
                writer.WriteNull(key);
                break;
            case bool b:
                writer.WriteBoolean(key, b);
                break;
            case long n:
                writer.WriteNumber(key, n);
                break;
            case double d:
                writer.WriteNumber(key, d);
                break;
            default:
                writer.WriteString(key, RuntimeValues.Stringify(val));
                break;
        }
    }

    // ── Scoped logger ─────────────────────────────────────────────────────────

    private static StashDictionary BuildScopedLogger(StashDictionary presetFields)
    {
        var logger = new StashDictionary();

        logger.Set("debug", StashValue.FromObj(new BuiltInFunction("log.debug", -1,
            (ctx, args) => { EmitScoped(ctx, LevelDebug, args, presetFields); return StashValue.Null; })));

        logger.Set("info", StashValue.FromObj(new BuiltInFunction("log.info", -1,
            (ctx, args) => { EmitScoped(ctx, LevelInfo, args, presetFields); return StashValue.Null; })));

        logger.Set("warn", StashValue.FromObj(new BuiltInFunction("log.warn", -1,
            (ctx, args) => { EmitScoped(ctx, LevelWarn, args, presetFields); return StashValue.Null; })));

        logger.Set("error", StashValue.FromObj(new BuiltInFunction("log.error", -1,
            (ctx, args) => { EmitScoped(ctx, LevelError, args, presetFields); return StashValue.Null; })));

        return logger;
    }

    private static void EmitScoped(IInterpreterContext ctx, int level,
        ReadOnlySpan<StashValue> args, StashDictionary presetFields)
    {
        var state = ctx.LoggerState;
        int currentLevel = state.Level;
        string format = state.Format;
        string output = state.Output;
        TextWriter? fileWriter = state.FileWriter;

        if (level < currentLevel)
            return;

        if (args.Length == 0)
            throw new RuntimeError("log functions require at least a message argument.");

        string message = SvArgs.String(args, 0, LevelNamesJson[level]);

        StashDictionary? dataDict = null;
        string? dataScalar = null;

        if (args.Length >= 2)
        {
            var dataArg = args[1];
            if (dataArg.IsObj && dataArg.AsObj is StashDictionary d)
                dataDict = d;
            else if (!dataArg.IsNull)
                dataScalar = RuntimeValues.Stringify(dataArg.ToObject());
        }

        // Merge preset fields with data dict
        StashDictionary? merged = MergeFields(presetFields, dataDict);

        string line = format == "json"
            ? FormatJson(level, message, null, merged, dataScalar)
            : FormatText(level, message, null, merged, dataScalar);

        TextWriter writer = GetWriter(ctx, output, fileWriter);
        writer.WriteLine(line);

        string channel = output == "stdout" ? "stdout" : "stderr";
        if (fileWriter is null)
            ctx.NotifyOutput(channel, line + "\n");
    }

    private static StashDictionary? MergeFields(StashDictionary preset, StashDictionary? extra)
    {
        var merged = new StashDictionary();
        foreach (object key in preset.RawKeys())
            merged.Set(key, preset.Get(key));
        if (extra is not null)
        {
            foreach (object key in extra.RawKeys())
                merged.Set(key, extra.Get(key));
        }
        return merged;
    }

    // ── Level parsing ─────────────────────────────────────────────────────────

    private static int ParseLevel(string level) =>
        level.ToLowerInvariant() switch
        {
            "debug" => LevelDebug,
            "info"  => LevelInfo,
            "warn" or "warning" => LevelWarn,
            "error" => LevelError,
            _ => throw new RuntimeError($"log.setLevel: unknown level '{level}'. Expected 'debug', 'info', 'warn', or 'error'.", errorType: StashErrorTypes.ValueError)
        };
}
