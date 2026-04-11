namespace Stash.Stdlib.BuiltIns;

using System;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the 'env' namespace built-in functions.
/// </summary>
public static class EnvBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("env");
        ns.RequiresCapability(StashCapabilities.Environment);

        ns.Function("get", [Param("name", "string"), Param("default", "any")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'env.get' requires 1 or 2 arguments.");
            var name = SvArgs.String(args, 0, "env.get");

            var value = System.Environment.GetEnvironmentVariable(name);
            if (value is null)
                return args.Length == 2 ? args[1] : StashValue.Null;
            return StashValue.FromObj(value);
        },
            isVariadic: true,
            documentation: "Returns the value of an environment variable, or null if not set. If a default is provided it is returned instead of null.\n@param name The environment variable name\n@param default Optional default value when the variable is not set\n@return The value, default, or null");

        ns.Function("set", [Param("name", "string"), Param("value", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var name = SvArgs.String(args, 0, "env.set");
            var value = SvArgs.String(args, 1, "env.set");

            System.Environment.SetEnvironmentVariable(name, value);
            return StashValue.Null;
        });

        ns.Function("has", [Param("name", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var name = SvArgs.String(args, 0, "env.has");

            return StashValue.FromBool(System.Environment.GetEnvironmentVariable(name) != null);
        });

        ns.Function("all", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dict = new StashDictionary();
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                dict.Set(entry.Key.ToString()!, StashValue.FromObject(entry.Value?.ToString()));
            }
            return StashValue.FromObj(dict);
        });

        ns.Function("withPrefix", [Param("prefix", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var prefix = SvArgs.String(args, 0, "env.withPrefix");

            var dict = new StashDictionary();
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                var key = entry.Key.ToString()!;
                if (key.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    dict.Set(key, StashValue.FromObject(entry.Value?.ToString()));
                }
            }
            return StashValue.FromObj(dict);
        });

        ns.Function("remove", [Param("name", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var name = SvArgs.String(args, 0, "env.remove");

            System.Environment.SetEnvironmentVariable(name, null);
            return StashValue.Null;
        });

        ns.Function("cwd", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(System.Environment.CurrentDirectory);
        });

        ns.Function("home", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));
        });

        ns.Function("hostname", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(System.Environment.MachineName);
        });

        ns.Function("user", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(System.Environment.UserName);
        });

        ns.Function("os", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                return StashValue.FromObj("linux");
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                return StashValue.FromObj("macos");
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return StashValue.FromObj("windows");
            }

            return StashValue.FromObj("unknown");
        });

        ns.Function("arch", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        });

        ns.Function("loadFile", [Param("path", "string"), Param("prefix", "string?")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'env.loadFile' expects 1 or 2 arguments.");
            var filePath = SvArgs.String(args, 0, "env.loadFile");
            var prefix = "";
            if (args.Length == 2)
            {
                prefix = SvArgs.String(args, 1, "env.loadFile");
            }

            filePath = ctx.ExpandTilde(filePath);

            string text;
            try
            {
                text = System.IO.File.ReadAllText(filePath);
            }
            catch (System.IO.IOException e)
            {
                throw new RuntimeError("env.loadFile: " + e.Message);
            }

            long count = 0;
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var eqIndex = line.IndexOf('=');
                if (eqIndex < 0)
                {
                    continue;
                }

                var key = line.Substring(0, eqIndex).Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                var value = line.Substring(eqIndex + 1).Trim();

                // Strip surrounding quotes (single or double)
                if (value.Length >= 2)
                {
                    if ((value[0] == '"' && value[value.Length - 1] == '"') ||
                        (value[0] == '\'' && value[value.Length - 1] == '\''  ))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                }

                System.Environment.SetEnvironmentVariable(prefix + key, value);
                count++;
            }

            return StashValue.FromInt(count);
        }, isVariadic: true);

        ns.Function("saveFile", [Param("path", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var filePath = SvArgs.String(args, 0, "env.saveFile");

            filePath = ctx.ExpandTilde(filePath);

            var sb = new StringBuilder();
            var entries = new System.Collections.Generic.SortedDictionary<string, string>();
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                var key = entry.Key.ToString()!;
                var val = entry.Value?.ToString() ?? "";
                entries[key] = val;
            }

            foreach (var kvp in entries)
            {
                // Quote values that contain spaces, #, or quotes
                if (kvp.Value.Contains(' ') || kvp.Value.Contains('#') ||
                    kvp.Value.Contains('"') || kvp.Value.Contains('\'' ))
                {
                    // Use double quotes, escape existing double quotes
                    var escapedValue = kvp.Value.Replace("\"", "\\\"");
                    sb.AppendLine($"{kvp.Key}=\"{escapedValue}\"");
                }
                else
                {
                    sb.AppendLine($"{kvp.Key}={kvp.Value}");
                }
            }

            try
            {
                System.IO.File.WriteAllText(filePath, sb.ToString());
            }
            catch (System.IO.IOException e)
            {
                throw new RuntimeError("env.saveFile: " + e.Message);
            }

            return StashValue.Null;
        });

        return ns.Build();
    }
}
