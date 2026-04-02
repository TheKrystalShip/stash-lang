namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers global built-in functions and types.
/// </summary>
public static class GlobalBuiltIns
{
    public static GlobalDefinition Define(StashCapabilities capabilities)
    {
        var gb = new GlobalBuilder();

        gb.Function("typeof", [Param("value")], (_, args) =>
        {
            object? val = args[0];

            return val switch
            {
                null => "null",
                long => "int",
                double => "float",
                string => "string",
                bool => "bool",
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
                IStashCallable => "function",
                _ => "unknown"
            };
        }, returnType: "string");

        gb.Function("nameof", [Param("value")], (_, args) =>
        {
            object? val = args[0];

            return val switch
            {
                null => "null",
                long => "int",
                double => "float",
                string => "string",
                bool => "bool",
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
                BuiltInFunction bf => bf.Name,
                IStashCallable c => c.Name ?? "function",
                _ => "unknown"
            };
        }, returnType: "string");

        gb.Function("len", [Param("value")], (_, args) =>
        {
            object? val = args[0];
            if (val is string s)
            {
                return (long)s.Length;
            }

            if (val is List<object?> list)
            {
                return (long)list.Count;
            }

            if (val is StashDictionary dict)
            {
                return (long)dict.Count;
            }

            throw new RuntimeError("Argument to 'len' must be a string, array, or dictionary.");
        }, returnType: "int");

        gb.Function("lastError", [], (ctx, args) =>
        {
            return ctx.LastError;
        }, returnType: "Error");

        gb.Function("range", [Param("start_or_end", "int"), Param("end", "int"), Param("step", "int")], (_, args) =>
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
        }, returnType: "array", arity: -1);

        if (capabilities.HasFlag(StashCapabilities.Process))
        {
            gb.Function("exit", [Param("code", "int")], (ctx, args) =>
            {
                var code = Args.Long(args, 0, "exit");
                ctx.EmitExit((int)code);
                return null;
            });
        }

        gb.Function("hash", [Param("value")], (_, args) =>
        {
            if (args[0] is null)
            {
                return 0L;
            }

            return (long)args[0]!.GetHashCode();
        }, returnType: "int");

        return gb.Build();
    }
}
