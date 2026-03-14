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
        }));

        globals.Define("ArgDef", new StashStruct("ArgDef", new List<string>
        {
            "name", "short", "type", "default", "description", "required", "args"
        }));

        // parseArgs built-in function
        globals.Define("parseArgs", new BuiltInFunction("parseArgs", 1, (interpreter, fnArgs) =>
        {
            return new ArgumentParser(interpreter.ScriptArgs).Parse(fnArgs[0]);
        }));
    }
}
