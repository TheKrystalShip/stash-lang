namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers global built-in functions and types.
/// </summary>
public static class GlobalBuiltIns
{
    public static NamespaceDefinition Define(StashCapabilities capabilities)
    {
        var b = new NamespaceBuilder("");

        b.Function("typeof", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            StashValue val = args[0];

            if (val.IsNull) return StashValue.FromObj("null");
            if (val.IsInt) return StashValue.FromObj("int");
            if (val.IsFloat) return StashValue.FromObj("float");
            if (val.IsBool) return StashValue.FromObj("bool");
            if (val.IsByte) return StashValue.FromObj("byte");

            object? obj = val.AsObj;
            return StashValue.FromObj(obj switch
            {
                null => "null",
                string => "string",
                List<StashValue> => "array",
                StashTypedArray ta => $"{ta.ElementTypeName}[]",
                StashSecret => "secret",
                StashError => "Error",
                StashInstance => "struct",
                StashStruct => "struct",
                StashEnumValue => "enum",
                StashEnum => "enum",
                StashInterface => "interface",
                StashDictionary => "dict",
                StashRange => "range",
                StashFuture => "Future",
                StashNamespace => "namespace",
                StashDuration => "duration",
                StashByteSize => "bytes",
                StashIpAddress => "ip",
                StashSemVer => "semver",
                IStashCallable => "function",
                _ => obj.GetType().Name.Contains("BoundMethod") ? "function" : ctx.ResolveRegisteredTypeName(obj)
            });
        }, returnType: "string",
            documentation: "Returns the type name of a value as a string.\n@param value The value to inspect\n@return A string such as 'int', 'string', 'array', 'dict', 'null', 'bool', 'float', or a struct/enum name");

        b.Function("semver", [Param("value", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            string str = SvArgs.String(args, 0, "semver");

            if (StashSemVer.TryParse(str, out StashSemVer? result))
            {
                return StashValue.FromObj(result!);
            }

            string detail = StashSemVer.ValidateFormat(str) ?? $"Invalid semantic version '{str}'.";
            throw new RuntimeError(detail, null);
        }, returnType: "semver", documentation: "Parses a string into a semver value.\n@param value A semantic version string in the format MAJOR.MINOR.PATCH (e.g. '1.2.3' or '2.0.0-beta.1')\n@return A semver value representing the parsed version");

        b.Function("nameof", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            StashValue val = args[0];

            if (val.IsNull) return StashValue.FromObj("null");
            if (val.IsInt) return StashValue.FromObj("int");
            if (val.IsFloat) return StashValue.FromObj("float");
            if (val.IsBool) return StashValue.FromObj("bool");
            if (val.IsByte) return StashValue.FromObj("byte");

            object? obj = val.AsObj;
            return StashValue.FromObj(obj switch
            {
                null => "null",
                string => "string",
                List<StashValue> => "array",
                StashSecret => "secret",
                StashError => "Error",
                StashInstance inst => inst.TypeName,
                StashStruct s => s.Name,
                StashEnumValue ev => $"{ev.TypeName}.{ev.MemberName}",
                StashEnum e => e.Name,
                StashInterface i => i.Name,
                StashDictionary => "dict",
                StashRange => "range",
                StashFuture => "Future",
                StashNamespace => "namespace",
                Runtime.BuiltInFunction bf => bf.Name,
                IStashCallable c => c.Name ?? "function",
                _ => "unknown"
            });
        }, returnType: "string",
            documentation: "Returns the name of a variable, function, struct, or enum as a string.\n@param value The value to name\n@return The name of the value, or its type name for primitives");

        b.Function("len", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            StashValue val = args[0];
            if (val.IsObj)
            {
                object? obj = val.AsObj;
                if (obj is StashSecret sec)
                {
                    object? inner = sec.InnerValue.IsObj ? sec.InnerValue.AsObj : null;
                    if (inner is string innerStr) return StashValue.FromInt((long)innerStr.Length);
                    if (inner is List<StashValue> innerList) return StashValue.FromInt((long)innerList.Count);
                    if (inner is StashDictionary innerDict) return StashValue.FromInt((long)innerDict.Count);
                    throw new RuntimeError("Argument to 'len' must be a string, array, or dictionary.");
                }
                if (obj is string s) return StashValue.FromInt((long)s.Length);
                if (obj is List<StashValue> svList) return StashValue.FromInt((long)svList.Count);
                if (obj is StashTypedArray typedArr) return StashValue.FromInt((long)typedArr.Count);
                if (obj is StashDictionary dict) return StashValue.FromInt((long)dict.Count);
            }
            throw new RuntimeError("Argument to 'len' must be a string, array, or dictionary.");
        }, returnType: "int",
            documentation: "Returns the length of a string, array, or dictionary.\n@param value A string, array, or dict\n@return The number of characters, elements, or entries");

        b.Function("lastError", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            return ctx.LastError is not null ? StashValue.FromObj(ctx.LastError) : StashValue.Null;
        }, returnType: "Error",
            documentation: "Returns the last error that was caught in a try/catch block, or null if none.\n@return The last caught Error value, or null");

        b.Function("range", [Param("start_or_end", "int"), Param("end", "int"), Param("step", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 3) throw new RuntimeError("'range' expects 1 to 3 arguments.");
            long start, end, step;
            if (args.Length == 1)
            {
                var e = SvArgs.Long(args, 0, "range");
                start = 0; end = e; step = 1;
            }
            else if (args.Length == 2)
            {
                var s = SvArgs.Long(args, 0, "range");
                var e = SvArgs.Long(args, 1, "range");
                start = s; end = e; step = 1;
            }
            else
            {
                var s = SvArgs.Long(args, 0, "range");
                var e = SvArgs.Long(args, 1, "range");
                var st = SvArgs.Long(args, 2, "range");
                if (st == 0)
                {
                    throw new RuntimeError("'range' step cannot be zero.");
                }

                start = s; end = e; step = st;
            }

            var result = new List<StashValue>();
            if (step > 0)
            {
                for (long i = start; i < end; i += step)
                {
                    result.Add(StashValue.FromInt(i));
                }
            }
            else
            {
                for (long i = start; i > end; i += step)
                {
                    result.Add(StashValue.FromInt(i));
                }
            }
            return StashValue.FromObj(result);
        }, returnType: "array", isVariadic: true,
            documentation: "Generates an array of integers. With one argument, generates 0..n-1. With two, generates start..end-1. With three, uses the given step.\n@param start_or_end End (exclusive) when called with 1 arg, or start when called with 2-3 args\n@param end End (exclusive) when called with 2 or 3 args\n@param step Step size (positive or negative); must not be zero\n@return An array of integers");

        if (capabilities.HasFlag(StashCapabilities.Process))
        {
            b.Function("exit", [Param("code", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                var code = SvArgs.Long(args, 0, "exit");
                ctx.EmitExit((int)code);
                return StashValue.Null;
            },
                returnType: "never",
                documentation: "Terminates the program with the given exit code.\n@param code The exit code to return to the OS\n@return never");
        }

        b.Function("hash", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args[0].IsNull) return StashValue.FromInt(0L);
            return StashValue.FromInt((long)args[0].ToObject()!.GetHashCode());
        }, returnType: "int",
            documentation: "Returns a hash code for the given value.\n@param value The value to hash\n@return An integer hash code");

        b.Function("secret", [Param("value")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(new StashSecret(args[0]));
        }, returnType: "secret", documentation: "Wraps a value as a secret. Secrets auto-redact when printed or interpolated.\n@param value The value to protect.\n@return A secret-wrapped value.");

        b.Function("reveal", [Param("value", "secret")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashValue val = args[0];
            if (val.IsObj && val.AsObj is StashSecret sec)
            {
                return sec.Reveal();
            }
            throw new RuntimeError("Argument to 'reveal' must be a secret.");
        }, returnType: "any", documentation: "Unwraps a secret value, returning the real underlying value.\n@param value The secret to unwrap.\n@return The original value.");

        // Enums
        b.Enum("Backoff", ["Fixed", "Linear", "Exponential"]);

        // Typed error structs — registered here so the VM can instantiate them via
        // struct-init literals (e.g. `throw ValueError { message: "x" }`).
        b.Struct(StashErrorTypes.ValueError,        [new("message", "string")]);
        b.Struct(StashErrorTypes.TypeError,         [new("message", "string")]);
        b.Struct(StashErrorTypes.ParseError,        [new("message", "string")]);
        b.Struct(StashErrorTypes.IndexError,        [new("message", "string")]);
        b.Struct(StashErrorTypes.IOError,           [new("message", "string")]);
        b.Struct(StashErrorTypes.NotSupportedError, [new("message", "string")]);
        b.Struct(StashErrorTypes.TimeoutError,      [new("message", "string")]);
        b.Struct(StashErrorTypes.CommandError, [
            new("message",  "string"),
            new("exitCode", "int"),
            new("stderr",   "string"),
            new("stdout",   "string"),
            new("command",  "string"),
        ]);
        b.Struct(StashErrorTypes.LockError, [new("message", "string"), new("path", "string")]);

        // Prompt-related structs — registered here so LSP hover/completion knows their fields.
        b.Struct("PromptGit", [
            new("isInRepo",       "bool"),
            new("branch",         "string"),
            new("isDirty",        "bool"),
            new("stagedCount",    "int"),
            new("unstagedCount",  "int"),
            new("untrackedCount", "int"),
            new("ahead",          "int"),
            new("behind",         "int"),
        ]);
        b.Struct("PromptContext", [
            new("cwd",            "string"),
            new("cwdAbsolute",    "string"),
            new("user",           "string"),
            new("host",           "string"),
            new("hostFull",       "string"),
            new("time",           "float"),
            new("lastExitCode",   "int"),
            new("lineNumber",     "int"),
            new("mode",           "string"),
            new("hostColor",      "string"),
            new("git",            "PromptGit"),
        ]);

        // Retry-related struct and context types
        b.Struct("RetryOptions", [
            new("delay",    "duration"),
            new("backoff",  "Backoff"),
            new("maxDelay", "duration"),
            new("jitter",   "bool"),
            new("timeout",  "duration"),
            new("on",       "array"),
        ]);
        // RetryContext is metadata-only — the runtime `attempt` variable is a dict, but
        // registering the struct here gives the LSP field info for hover/completion.
        b.Struct("RetryContext", [
            new("current",   "int"),
            new("max",       "int"),
            new("remaining", "int"),
            new("elapsed",   "duration"),
            new("errors",    "array"),
        ]);

        return b.Build();
    }
}
