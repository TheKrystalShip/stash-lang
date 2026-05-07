namespace Stash.Stdlib.BuiltIns;

using System;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>env</c> namespace built-in functions.
/// </summary>
[StashNamespace(Capability = StashCapabilities.Environment)]
public static partial class EnvBuiltIns
{
    /// <summary>Returns the value of an environment variable, or null if not set. If a default is provided it is returned instead of null.</summary>
    /// <param name="name">The environment variable name</param>
    /// <param name="default">Optional default value when the variable is not set</param>
    /// <returns>The value, default, or null</returns>
    [StashFn(Raw = true)]
    private static StashValue Get(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1 || args.Length > 2)
            throw new RuntimeError("'env.get' requires 1 or 2 arguments.");
        var name = SvArgs.String(args, 0, "env.get");

        var value = System.Environment.GetEnvironmentVariable(name);
        if (value is null)
            return args.Length == 2 ? args[1] : StashValue.Null;
        return StashValue.FromObj(value);
    }

    /// <summary>Sets an environment variable to the given value.</summary>
    /// <param name="name">The environment variable name</param>
    /// <param name="value">The value to assign</param>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Set(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var name = SvArgs.String(args, 0, "env.set");
        var value = SvArgs.String(args, 1, "env.set");

        System.Environment.SetEnvironmentVariable(name, value);
        return StashValue.Null;
    }

    /// <summary>Returns true if the environment variable is set.</summary>
    /// <param name="name">The environment variable name</param>
    /// <returns>True if the variable exists</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Has(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var name = SvArgs.String(args, 0, "env.has");

        return StashValue.FromBool(System.Environment.GetEnvironmentVariable(name) != null);
    }

    /// <summary>Returns a dictionary of all current environment variables.</summary>
    /// <returns>A dictionary mapping variable names to their values</returns>
    [StashFn(Raw = true, ReturnType = "dict")]
    private static StashValue All(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var dict = new StashDictionary();
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            dict.Set(entry.Key.ToString()!, StashValue.FromObject(entry.Value?.ToString()));
        }
        return StashValue.FromObj(dict);
    }

    /// <summary>Returns a dictionary of all environment variables whose names start with the given prefix.</summary>
    /// <param name="prefix">The prefix to filter by</param>
    /// <returns>A dictionary of matching environment variables</returns>
    [StashFn(Raw = true, ReturnType = "dict")]
    private static StashValue WithPrefix(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Removes an environment variable.</summary>
    /// <param name="name">The environment variable name to remove</param>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Remove(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var name = SvArgs.String(args, 0, "env.remove");

        System.Environment.SetEnvironmentVariable(name, null);
        return StashValue.Null;
    }

    /// <summary>Removes the environment variable 'name'. Returns true if the variable existed, false otherwise.</summary>
    /// <param name="name">The environment variable name to remove</param>
    /// <returns>True if the variable was set, false if it was not set</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Unset(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Returns the current working directory path.</summary>
    /// <returns>The current working directory</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Cwd(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return StashValue.FromObj(System.Environment.CurrentDirectory);
    }

    /// <summary>Returns the current user's home directory path.</summary>
    /// <returns>The home directory path</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Home(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return StashValue.FromObj(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));
    }

    /// <summary>Returns the machine's hostname.</summary>
    /// <returns>The hostname</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Hostname(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return StashValue.FromObj(System.Environment.MachineName);
    }

    /// <summary>Returns the current user's login name.</summary>
    /// <returns>The username</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue User(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return StashValue.FromObj(System.Environment.UserName);
    }

    /// <summary>Returns the current operating system as a string: 'linux', 'macos', 'windows', or 'unknown'.</summary>
    /// <returns>The OS name</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Os(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Returns the CPU architecture (e.g. 'x64', 'arm64').</summary>
    /// <returns>The architecture name</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Arch(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return StashValue.FromObj(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
    }

    /// <summary>Loads environment variables from a .env file. Optionally prefixes all variable names. Returns the number of variables loaded.</summary>
    /// <param name="path">Path to the .env file</param>
    /// <param name="prefix">Optional prefix to prepend to all variable names</param>
    /// <returns>The number of variables loaded</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue LoadFile(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Saves all current environment variables to a .env file at the given path.</summary>
    /// <param name="path">Path to write the .env file</param>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue SaveFile(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
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
    }

    /// <summary>Changes the current working directory to the given path and pushes it onto the directory stack.</summary>
    /// <param name="path">The directory path to change to</param>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Chdir(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => CurrentProcessImpl.Chdir(ctx, args, "env.chdir");

    /// <summary>Pops the top directory from the stack, changes cwd back to the new top, and returns the popped path. Throws CommandError if the stack is at its root entry.</summary>
    /// <returns>The directory path that was popped</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue PopDir(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => CurrentProcessImpl.PopDir(ctx, args, "env.popDir");

    /// <summary>Returns a copy of the directory stack, oldest entry first.</summary>
    /// <returns>An array of directory path strings</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue DirStack(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => CurrentProcessImpl.DirStack(ctx, args, "env.dirStack");

    /// <summary>Returns the number of entries in the directory stack.</summary>
    /// <returns>The depth as an integer</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue DirStackDepth(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => CurrentProcessImpl.DirStackDepth(ctx, args, "env.dirStackDepth");

    /// <summary>Temporarily changes the working directory to the given path, calls fn(), then restores the original directory. Returns fn's return value.</summary>
    /// <param name="path">The directory to temporarily change to</param>
    /// <param name="fn">The function to execute in the new directory</param>
    /// <returns>The return value of fn</returns>
    [StashFn(Raw = true, ReturnType = "any")]
    private static StashValue WithDir(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => CurrentProcessImpl.WithDir(ctx, args, "env.withDir");

    /// <summary>Exits the current process with the given integer exit code (default 0). Runs all pending defer blocks before terminating. Cannot be caught by try/catch.</summary>
    /// <param name="code">(optional) The exit code. Defaults to 0</param>
    /// <returns>Does not return — exits the process</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Exit(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        long code = args.Length > 0 ? SvArgs.Long(args, 0, "env.exit") : 0L;
        GlobalBuiltIns.EmitExitImpl(ctx, code);
        return StashValue.Null;
    }
}
