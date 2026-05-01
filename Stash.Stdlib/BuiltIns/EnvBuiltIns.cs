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
        },
            returnType: "null",
            documentation: "Sets an environment variable to the given value.\n@param name The environment variable name\n@param value The value to assign\n@return null");

        ns.Function("has", [Param("name", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var name = SvArgs.String(args, 0, "env.has");

            return StashValue.FromBool(System.Environment.GetEnvironmentVariable(name) != null);
        },
            returnType: "bool",
            documentation: "Returns true if the environment variable is set.\n@param name The environment variable name\n@return True if the variable exists");

        ns.Function("all", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dict = new StashDictionary();
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                dict.Set(entry.Key.ToString()!, StashValue.FromObject(entry.Value?.ToString()));
            }
            return StashValue.FromObj(dict);
        },
            returnType: "dict",
            documentation: "Returns a dictionary of all current environment variables.\n@return A dictionary mapping variable names to their values");

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
        },
            returnType: "dict",
            documentation: "Returns a dictionary of all environment variables whose names start with the given prefix.\n@param prefix The prefix to filter by\n@return A dictionary of matching environment variables");

        ns.Function("remove", [Param("name", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var name = SvArgs.String(args, 0, "env.remove");

            System.Environment.SetEnvironmentVariable(name, null);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Removes an environment variable.\n@param name The environment variable name to remove\n@return null");

        ns.Function("unset", [Param("name", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashValue v0 = args[0];
            if (!v0.IsObj || v0.AsObj is not string name)
                throw new RuntimeError("1st argument to 'env.unset' must be a string.", errorType: StashErrorTypes.TypeError);
            if (name.Length == 0)
                throw new RuntimeError("'env.unset': name must not be empty.", errorType: StashErrorTypes.ValueError);
            if (name.Contains('='))
                throw new RuntimeError("'env.unset': name must not contain '='.", errorType: StashErrorTypes.ValueError);
            if (name.Contains('\0'))
                throw new RuntimeError("'env.unset': name must not contain null characters.", errorType: StashErrorTypes.ValueError);

            bool existed = System.Environment.GetEnvironmentVariable(name) is not null;
            System.Environment.SetEnvironmentVariable(name, null);
            return StashValue.FromBool(existed);
        },
            returnType: "bool",
            documentation: "Removes the environment variable 'name'. Returns true if the variable existed, false otherwise.\n@param name The environment variable name to remove\n@return True if the variable was set, false if it was not set");

        ns.Function("cwd", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(System.Environment.CurrentDirectory);
        },
            returnType: "string",
            documentation: "Returns the current working directory path.\n@return The current working directory");

        ns.Function("home", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));
        },
            returnType: "string",
            documentation: "Returns the current user's home directory path.\n@return The home directory path");

        ns.Function("hostname", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(System.Environment.MachineName);
        },
            returnType: "string",
            documentation: "Returns the machine's hostname.\n@return The hostname");

        ns.Function("user", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(System.Environment.UserName);
        },
            returnType: "string",
            documentation: "Returns the current user's login name.\n@return The username");

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
        },
            returnType: "string",
            documentation: "Returns the current operating system as a string: 'linux', 'macos', 'windows', or 'unknown'.\n@return The OS name");

        ns.Function("arch", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        },
            returnType: "string",
            documentation: "Returns the CPU architecture (e.g. 'x64', 'arm64').\n@return The architecture name");

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
                throw new RuntimeError("env.loadFile: " + e.Message, errorType: StashErrorTypes.IOError);
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
        },
            isVariadic: true,
            returnType: "int",
            documentation: "Loads environment variables from a .env file. Optionally prefixes all variable names. Returns the number of variables loaded.\n@param path Path to the .env file\n@param prefix Optional prefix to prepend to all variable names\n@return The number of variables loaded");

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
                throw new RuntimeError("env.saveFile: " + e.Message, errorType: StashErrorTypes.IOError);
            }

            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Saves all current environment variables to a .env file at the given path.\n@param path Path to write the .env file\n@return null");

        ns.Function("chdir", [Param("path", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            CurrentProcessImpl.Chdir(ctx, args, "env.chdir"),
            returnType: "null",
            documentation: "Changes the current working directory to the given path and pushes it onto the directory stack.\n@param path The directory path to change to\n@return null");

        ns.Function("popDir", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            CurrentProcessImpl.PopDir(ctx, args, "env.popDir"),
            returnType: "string",
            documentation: "Pops the top directory from the stack, changes cwd back to the new top, and returns the popped path.\nThrows CommandError if the stack is at its root entry.\n@return The directory path that was popped");

        ns.Function("dirStack", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            CurrentProcessImpl.DirStack(ctx, args, "env.dirStack"),
            returnType: "array",
            documentation: "Returns a copy of the directory stack, oldest entry first.\n@return An array of directory path strings");

        ns.Function("dirStackDepth", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            CurrentProcessImpl.DirStackDepth(ctx, args, "env.dirStackDepth"),
            returnType: "int",
            documentation: "Returns the number of entries in the directory stack.\n@return The depth as an integer");

        ns.Function("withDir", [Param("path", "string"), Param("fn", "function")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            CurrentProcessImpl.WithDir(ctx, args, "env.withDir"),
            returnType: "any",
            documentation: "Temporarily changes the working directory to the given path, calls fn(), then restores the original directory. Returns fn's return value.\n@param path The directory to temporarily change to\n@param fn The function to execute in the new directory\n@return The return value of fn");

        ns.Function("exit", [Param("code", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            long code = args.Length > 0 ? SvArgs.Long(args, 0, "env.exit") : 0L;
            GlobalBuiltIns.EmitExitImpl(ctx, code);
            return StashValue.Null;
        },
            returnType: "null",
            isVariadic: true,
            documentation: "Exits the current process with the given integer exit code (default 0). Runs all pending defer blocks before terminating. Cannot be caught by try/catch.\n@param code (optional) The exit code. Defaults to 0\n@return Does not return — exits the process");

        return ns.Build();
    }
}
