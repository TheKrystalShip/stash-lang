namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using System.Text;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'env' namespace built-in functions.
/// </summary>
public static class EnvBuiltIns
{
    private static string ExpandTilde(string path) => Interpreter.ExpandTilde(path);

    public static void Register(Environment globals)
    {
        // ── env namespace ────────────────────────────────────────────────
        var envNs = new StashNamespace("env");

        envNs.Define("get", new BuiltInFunction("env.get", 1, (_, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("Argument to 'env.get' must be a string.");
            }

            return System.Environment.GetEnvironmentVariable(name);
        }));

        envNs.Define("set", new BuiltInFunction("env.set", 2, (_, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("First argument to 'env.set' must be a string.");
            }

            if (args[1] is not string value)
            {
                throw new RuntimeError("Second argument to 'env.set' must be a string.");
            }

            System.Environment.SetEnvironmentVariable(name, value);
            return null;
        }));

        envNs.Define("has", new BuiltInFunction("env.has", 1, (_, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("Argument to 'env.has' must be a string.");
            }

            return (bool)(System.Environment.GetEnvironmentVariable(name) != null);
        }));

        envNs.Define("all", new BuiltInFunction("env.all", 0, (_, args) =>
        {
            var dict = new StashDictionary();
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                dict.Set(entry.Key.ToString()!, entry.Value?.ToString());
            }
            return dict;
        }));

        envNs.Define("withPrefix", new BuiltInFunction("env.withPrefix", 1, (_, args) =>
        {
            if (args[0] is not string prefix)
            {
                throw new RuntimeError("Argument to 'env.withPrefix' must be a string.");
            }

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
        }));

        envNs.Define("remove", new BuiltInFunction("env.remove", 1, (_, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("Argument to 'env.remove' must be a string.");
            }

            System.Environment.SetEnvironmentVariable(name, null);
            return null;
        }));

        envNs.Define("cwd", new BuiltInFunction("env.cwd", 0, (_, args) =>
        {
            return System.Environment.CurrentDirectory;
        }));

        envNs.Define("home", new BuiltInFunction("env.home", 0, (_, args) =>
        {
            return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        }));

        envNs.Define("hostname", new BuiltInFunction("env.hostname", 0, (_, args) =>
        {
            return System.Environment.MachineName;
        }));

        envNs.Define("user", new BuiltInFunction("env.user", 0, (_, args) =>
        {
            return System.Environment.UserName;
        }));

        envNs.Define("os", new BuiltInFunction("env.os", 0, (_, args) =>
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
        }));

        envNs.Define("arch", new BuiltInFunction("env.arch", 0, (_, args) =>
        {
            return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        }));

        envNs.Define("loadFile", new BuiltInFunction("env.loadFile", -1, (_, args) =>
        {
            if (args.Count < 1 || args.Count > 2)
            {
                throw new RuntimeError("'env.loadFile' expects 1 or 2 arguments.");
            }

            if (args[0] is not string filePath)
            {
                throw new RuntimeError("Argument to 'env.loadFile' must be a string.");
            }

            var prefix = "";
            if (args.Count == 2)
            {
                if (args[1] is not string prefixArg)
                {
                    throw new RuntimeError("Second argument to 'env.loadFile' must be a string.");
                }
                prefix = prefixArg;
            }

            filePath = ExpandTilde(filePath);

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
        }));

        envNs.Define("saveFile", new BuiltInFunction("env.saveFile", 1, (_, args) =>
        {
            if (args[0] is not string filePath)
            {
                throw new RuntimeError("Argument to 'env.saveFile' must be a string.");
            }

            filePath = ExpandTilde(filePath);

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
        }));

        globals.Define("env", envNs);
    }
}
