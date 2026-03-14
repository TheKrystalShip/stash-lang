namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Registers the 'str' namespace built-in functions.
/// </summary>
public static class StrBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        // ── str namespace ────────────────────────────────────────────────
        var str = new StashNamespace("str");

        str.Define("upper", new BuiltInFunction("str.upper", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.upper' must be a string.");
            }

            return s.ToUpperInvariant();
        }));

        str.Define("lower", new BuiltInFunction("str.lower", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.lower' must be a string.");
            }

            return s.ToLowerInvariant();
        }));

        str.Define("trim", new BuiltInFunction("str.trim", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.trim' must be a string.");
            }

            return s.Trim();
        }));

        str.Define("trimStart", new BuiltInFunction("str.trimStart", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.trimStart' must be a string.");
            }

            return s.TrimStart();
        }));

        str.Define("trimEnd", new BuiltInFunction("str.trimEnd", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.trimEnd' must be a string.");
            }

            return s.TrimEnd();
        }));

        str.Define("contains", new BuiltInFunction("str.contains", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.contains' must be a string.");
            }

            if (args[1] is not string sub)
            {
                throw new RuntimeError("Second argument to 'str.contains' must be a string.");
            }

            return s.Contains(sub, StringComparison.Ordinal);
        }));

        str.Define("startsWith", new BuiltInFunction("str.startsWith", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.startsWith' must be a string.");
            }

            if (args[1] is not string prefix)
            {
                throw new RuntimeError("Second argument to 'str.startsWith' must be a string.");
            }

            return s.StartsWith(prefix, StringComparison.Ordinal);
        }));

        str.Define("endsWith", new BuiltInFunction("str.endsWith", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.endsWith' must be a string.");
            }

            if (args[1] is not string suffix)
            {
                throw new RuntimeError("Second argument to 'str.endsWith' must be a string.");
            }

            return s.EndsWith(suffix, StringComparison.Ordinal);
        }));

        str.Define("indexOf", new BuiltInFunction("str.indexOf", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.indexOf' must be a string.");
            }

            if (args[1] is not string sub)
            {
                throw new RuntimeError("Second argument to 'str.indexOf' must be a string.");
            }

            return (long)s.IndexOf(sub, StringComparison.Ordinal);
        }));

        str.Define("lastIndexOf", new BuiltInFunction("str.lastIndexOf", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.lastIndexOf' must be a string.");
            }

            if (args[1] is not string sub)
            {
                throw new RuntimeError("Second argument to 'str.lastIndexOf' must be a string.");
            }

            return (long)s.LastIndexOf(sub, StringComparison.Ordinal);
        }));

        str.Define("substring", new BuiltInFunction("str.substring", -1, (_, args) =>
        {
            if (args.Count < 2 || args.Count > 3)
            {
                throw new RuntimeError("'str.substring' requires 2 or 3 arguments.");
            }

            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.substring' must be a string.");
            }

            if (args[1] is not long start)
            {
                throw new RuntimeError("Second argument to 'str.substring' must be an integer.");
            }

            if (start < 0 || start > s.Length)
            {
                throw new RuntimeError($"'str.substring' start index {start} is out of range for string of length {s.Length}.");
            }

            if (args.Count == 3)
            {
                if (args[2] is not long end)
                {
                    throw new RuntimeError("Third argument to 'str.substring' must be an integer.");
                }

                if (end < start || end > s.Length)
                {
                    throw new RuntimeError($"'str.substring' end index {end} is out of range.");
                }

                return s.Substring((int)start, (int)(end - start));
            }
            return s.Substring((int)start);
        }));

        str.Define("replace", new BuiltInFunction("str.replace", 3, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.replace' must be a string.");
            }

            if (args[1] is not string oldStr)
            {
                throw new RuntimeError("Second argument to 'str.replace' must be a string.");
            }

            if (args[2] is not string newStr)
            {
                throw new RuntimeError("Third argument to 'str.replace' must be a string.");
            }

            int idx = s.IndexOf(oldStr, StringComparison.Ordinal);
            if (idx < 0)
            {
                return s;
            }

            return s.Substring(0, idx) + newStr + s.Substring(idx + oldStr.Length);
        }));

        str.Define("replaceAll", new BuiltInFunction("str.replaceAll", 3, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.replaceAll' must be a string.");
            }

            if (args[1] is not string oldStr)
            {
                throw new RuntimeError("Second argument to 'str.replaceAll' must be a string.");
            }

            if (args[2] is not string newStr)
            {
                throw new RuntimeError("Third argument to 'str.replaceAll' must be a string.");
            }

            return s.Replace(oldStr, newStr, StringComparison.Ordinal);
        }));

        str.Define("split", new BuiltInFunction("str.split", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.split' must be a string.");
            }

            if (args[1] is not string delimiter)
            {
                throw new RuntimeError("Second argument to 'str.split' must be a string.");
            }

            var parts = s.Split(new[] { delimiter }, StringSplitOptions.None);
            return parts.Select(p => (object?)p).ToList();
        }));

        str.Define("repeat", new BuiltInFunction("str.repeat", 2, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.repeat' must be a string.");
            }

            if (args[1] is not long count)
            {
                throw new RuntimeError("Second argument to 'str.repeat' must be an integer.");
            }

            if (count < 0)
            {
                throw new RuntimeError("'str.repeat' count must be >= 0.");
            }

            return string.Concat(Enumerable.Repeat(s, (int)count));
        }));

        str.Define("reverse", new BuiltInFunction("str.reverse", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.reverse' must be a string.");
            }

            return new string(s.Reverse().ToArray());
        }));

        str.Define("chars", new BuiltInFunction("str.chars", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'str.chars' must be a string.");
            }

            return s.Select(c => (object?)c.ToString()).ToList();
        }));

        str.Define("padStart", new BuiltInFunction("str.padStart", -1, (_, args) =>
            RuntimeValues.PadString("str.padStart", args, padLeft: true)));

        str.Define("padEnd", new BuiltInFunction("str.padEnd", -1, (_, args) =>
            RuntimeValues.PadString("str.padEnd", args, padLeft: false)));

        globals.Define("str", str);
    }
}
