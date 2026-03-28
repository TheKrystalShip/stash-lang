namespace Stash.Stdlib.BuiltIns;

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

        ns.Function("get", [Param("name", "string")], (_, args) =>
        {
            var name = Args.String(args, 0, "env.get");

            return System.Environment.GetEnvironmentVariable(name);
        });

        ns.Function("set", [Param("name", "string"), Param("value", "string")], (_, args) =>
        {
            var name = Args.String(args, 0, "env.set");
            var value = Args.String(args, 1, "env.set");

            System.Environment.SetEnvironmentVariable(name, value);
            return null;
        });

        ns.Function("has", [Param("name", "string")], (_, args) =>
        {
            var name = Args.String(args, 0, "env.has");

            return (bool)(System.Environment.GetEnvironmentVariable(name) != null);
        });

        ns.Function("all", [], (_, args) =>
        {
            var dict = new StashDictionary();
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                dict.Set(entry.Key.ToString()!, entry.Value?.ToString());
            }
            return dict;
        });

        ns.Function("withPrefix", [Param("prefix", "string")], (_, args) =>
        {
            var prefix = Args.String(args, 0, "env.withPrefix");

            var dict = new StashDictionary();
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                var key = entry.Key.ToString()!;
                if (key.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    dict.Set(key, entry.Value?.ToString());
                }
            }
            return dict;
        });

        ns.Function("remove", [Param("name", "string")], (_, args) =>
        {
            var name = Args.String(args, 0, "env.remove");

            System.Environment.SetEnvironmentVariable(name, null);
            return null;
        });

        ns.Function("cwd", [], (_, args) =>
        {
            return System.Environment.CurrentDirectory;
        });

        ns.Function("home", [], (_, args) =>
        {
            return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        });

        ns.Function("hostname", [], (_, args) =>
        {
            return System.Environment.MachineName;
        });

        ns.Function("user", [], (_, args) =>
        {
            return System.Environment.UserName;
        });

        ns.Function("os", [], (_, args) =>
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                return "linux";
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                return "macos";
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return "windows";
            }

            return "unknown";
        });

        ns.Function("arch", [], (_, args) =>
        {
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        });

        ns.Function("loadFile", [Param("path", "string"), Param("prefix", "string?")], (ctx, args) =>
        {
            Args.Count(args, 1, 2, "env.loadFile");
            var filePath = Args.String(args, 0, "env.loadFile");
            var prefix = "";
            if (args.Count == 2)
            {
                prefix = Args.String(args, 1, "env.loadFile");
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

            return count;
        }, isVariadic: true);

        ns.Function("saveFile", [Param("path", "string")], (ctx, args) =>
        {
            var filePath = Args.String(args, 0, "env.saveFile");

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

            return null;
        });

        return ns.Build();
    }
}
