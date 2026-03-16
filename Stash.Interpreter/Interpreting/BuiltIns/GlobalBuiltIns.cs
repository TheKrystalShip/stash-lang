namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using Stash.Interpreting.Types;

/// <summary>
/// Registers global built-in functions and types.
/// </summary>
public static class GlobalBuiltIns
{
    public static void Register(Environment globals)
    {
        globals.Define("typeof", new BuiltInFunction("typeof", 1, (_, args) =>
        {
            object? val = args[0];
            if (val is null)
            {
                return "null";
            }

            if (val is long)
            {
                return "int";
            }

            if (val is double)
            {
                return "float";
            }

            if (val is string)
            {
                return "string";
            }

            if (val is bool)
            {
                return "bool";
            }

            if (val is List<object?>)
            {
                return "array";
            }

            if (val is StashInstance)
            {
                return "struct";
            }

            if (val is StashStruct)
            {
                return "struct";
            }

            if (val is StashEnumValue)
            {
                return "enum";
            }

            if (val is StashEnum)
            {
                return "enum";
            }

            if (val is StashDictionary)
            {
                return "dict";
            }

            if (val is StashRange)
            {
                return "range";
            }

            if (val is StashNamespace)
            {
                return "namespace";
            }

            if (val is IStashCallable)
            {
                return "function";
            }

            return "unknown";
        }));

        globals.Define("len", new BuiltInFunction("len", 1, (_, args) =>
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
        }));

        globals.Define("lastError", new BuiltInFunction("lastError", 0, (interpreter, args) =>
        {
            return interpreter.LastError;
        }));

        // Built-in structs for argument parsing
        globals.Define("ArgTree", new StashStruct("ArgTree", new List<string>
        {
            "name", "version", "description", "flags", "options", "commands", "positionals"
        }, new Dictionary<string, StashFunction>()));

        globals.Define("ArgDef", new StashStruct("ArgDef", new List<string>
        {
            "name", "short", "type", "default", "description", "required", "args"
        }, new Dictionary<string, StashFunction>()));

        // parseArgs built-in function
        globals.Define("parseArgs", new BuiltInFunction("parseArgs", 1, (interpreter, fnArgs) =>
        {
            return new ArgumentParser(interpreter.ScriptArgs).Parse(fnArgs[0]);
        }));

        globals.Define("range", new BuiltInFunction("range", -1, (_, args) =>
        {
            if (args.Count < 1 || args.Count > 3)
            {
                throw new RuntimeError("'range' expects 1 to 3 arguments.");
            }

            long start, end, step;
            if (args.Count == 1)
            {
                if (args[0] is not long e)
                {
                    throw new RuntimeError("Arguments to 'range' must be integers.");
                }

                start = 0; end = e; step = 1;
            }
            else if (args.Count == 2)
            {
                if (args[0] is not long s)
                {
                    throw new RuntimeError("Arguments to 'range' must be integers.");
                }

                if (args[1] is not long e)
                {
                    throw new RuntimeError("Arguments to 'range' must be integers.");
                }

                start = s; end = e; step = 1;
            }
            else
            {
                if (args[0] is not long s)
                {
                    throw new RuntimeError("Arguments to 'range' must be integers.");
                }

                if (args[1] is not long e)
                {
                    throw new RuntimeError("Arguments to 'range' must be integers.");
                }

                if (args[2] is not long st)
                {
                    throw new RuntimeError("Arguments to 'range' must be integers.");
                }

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
        }));

        globals.Define("exit", new BuiltInFunction("exit", 1, (interp, args) =>
        {
            if (args[0] is not long code)
            {
                throw new RuntimeError("Argument to 'exit' must be an integer.");
            }

            interp.CleanupTrackedProcesses();
            System.Environment.Exit((int)code);
            return null;
        }));

        globals.Define("hash", new BuiltInFunction("hash", 1, (_, args) =>
        {
            if (args[0] is null)
            {
                return 0L;
            }

            return (long)args[0]!.GetHashCode();
        }));
    }
}
