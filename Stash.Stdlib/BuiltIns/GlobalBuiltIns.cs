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

            object? obj = val.AsObj;
            return StashValue.FromObj(obj switch
            {
                null => "null",
                string => "string",
                List<object?> => "array",
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
                _ => obj.GetType().Name.Contains("BoundMethod") ? "function" : "unknown"
            });
        }, returnType: "string");

        b.Function("semver", [Param("value", "string")], (_, args) =>
        {
            string str = Args.String(args, 0, "semver");

            if (StashSemVer.TryParse(str, out StashSemVer? result))
            {
                return result;
            }

            string detail = StashSemVer.ValidateFormat(str) ?? $"Invalid semantic version '{str}'.";
            throw new RuntimeError(detail, null);
        }, returnType: "semver", documentation: "Parses a string into a semver value.");

        b.Function("nameof", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            StashValue val = args[0];

            if (val.IsNull) return StashValue.FromObj("null");
            if (val.IsInt) return StashValue.FromObj("int");
            if (val.IsFloat) return StashValue.FromObj("float");
            if (val.IsBool) return StashValue.FromObj("bool");

            object? obj = val.AsObj;
            return StashValue.FromObj(obj switch
            {
                null => "null",
                string => "string",
                List<object?> => "array",
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
        }, returnType: "string");

        b.Function("len", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            StashValue val = args[0];
            if (val.IsObj)
            {
                object? obj = val.AsObj;
                if (obj is string s) return StashValue.FromInt((long)s.Length);
                if (obj is List<object?> list) return StashValue.FromInt((long)list.Count);
                if (obj is StashDictionary dict) return StashValue.FromInt((long)dict.Count);
            }
            throw new RuntimeError("Argument to 'len' must be a string, array, or dictionary.");
        }, returnType: "int");

        b.Function("lastError", [], (ctx, args) =>
        {
            return ctx.LastError;
        }, returnType: "Error");

        b.Function("range", [Param("start_or_end", "int"), Param("end", "int"), Param("step", "int")], (_, args) =>
        {
            Args.Count(args, 1, 3, "range");
            long start, end, step;
            if (args.Count == 1)
            {
                var e = Args.Long(args, 0, "range");
                start = 0; end = e; step = 1;
            }
            else if (args.Count == 2)
            {
                var s = Args.Long(args, 0, "range");
                var e = Args.Long(args, 1, "range");
                start = s; end = e; step = 1;
            }
            else
            {
                var s = Args.Long(args, 0, "range");
                var e = Args.Long(args, 1, "range");
                var st = Args.Long(args, 2, "range");
                if (st == 0)
                {
                    throw new RuntimeError("'range' step cannot be zero.");
                }

                start = s; end = e; step = st;
            }

            var result = new List<object?>();
            if (step > 0)
            {
                for (long i = start; i < end; i += step)
                {
                    result.Add(i);
                }
            }
            else
            {
                for (long i = start; i > end; i += step)
                {
                    result.Add(i);
                }
            }
            return result;
        }, returnType: "array", isVariadic: true);

        if (capabilities.HasFlag(StashCapabilities.Process))
        {
            b.Function("exit", [Param("code", "int")], (ctx, args) =>
            {
                var code = Args.Long(args, 0, "exit");
                ctx.EmitExit((int)code);
                return null;
            });
        }

        b.Function("hash", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args[0].IsNull) return StashValue.FromInt(0L);
            return StashValue.FromInt((long)args[0].ToObject()!.GetHashCode());
        }, returnType: "int");

        // Enums
        b.Enum("Backoff", ["Fixed", "Linear", "Exponential"]);

        // Struct definitions
        b.Struct("RetryOptions", [
            new ("delay", "duration"),
            new ("backoff", "Backoff"),
            new ("maxDelay", "duration"),
            new ("jitter", "duration"),
            new ("timeout", "duration"),
            new ("on", "Error"),
        ]);

        return b.Build();
    }
}
