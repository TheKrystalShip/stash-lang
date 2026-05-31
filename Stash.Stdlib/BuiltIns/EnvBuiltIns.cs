namespace Stash.Stdlib.BuiltIns;

using System;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the <c>env</c> namespace built-in functions.
/// </summary>
[StashNamespace(Capability = StashCapabilities.Environment)]
public static partial class EnvBuiltIns
{
    /// <summary>Returns the value of an environment variable, or null if not set. If a default is provided it is returned instead of null.</summary>
    /// <param name="name">The environment variable name</param>
    /// <param name="default">Optional default value when the variable is not set</param>
    /// <exception cref="TypeError">if the variable name is not a string</exception>
    /// <returns>The value, default, or null</returns>
    // Raw: second arg is pass-through StashValue (any type allowed as default)
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
    [StashFn]
    public static void Set(string name, string value)
    {
        System.Environment.SetEnvironmentVariable(name, value);
    }

    /// <summary>Returns true if the environment variable is set.</summary>
    /// <param name="name">The environment variable name</param>
    /// <returns>True if the variable exists</returns>
    [StashFn]
    public static bool Has(string name) =>
        System.Environment.GetEnvironmentVariable(name) != null;

    /// <summary>Returns a dictionary of all current environment variables.</summary>
    /// <returns>A dictionary mapping variable names to their values</returns>
    [StashFn]
    public static StashDictionary All()
    {
        var dict = new StashDictionary();
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            dict.Set(entry.Key.ToString()!, StashValue.FromObject(entry.Value?.ToString()));
        }
        return dict;
    }

    /// <summary>Returns a dictionary of all environment variables whose names start with the given prefix.</summary>
    /// <param name="prefix">The prefix to filter by</param>
    /// <returns>A dictionary of matching environment variables</returns>
    [StashFn]
    public static StashDictionary WithPrefix(string prefix)
    {
        var dict = new StashDictionary();
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            var key = entry.Key.ToString()!;
            if (key.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                dict.Set(key, StashValue.FromObject(entry.Value?.ToString()));
            }
        }
        return dict;
    }

    /// <summary>Removes an environment variable.</summary>
    /// <param name="name">The environment variable name to remove</param>
    [StashFn]
    public static void Remove(string name)
    {
        System.Environment.SetEnvironmentVariable(name, null);
    }

    /// <summary>Removes the environment variable 'name'. Returns true if the variable existed, false otherwise.</summary>
    /// <param name="name">The environment variable name to remove</param>
    /// <exception cref="ValueError">if `name` is empty, contains '=', or contains a null character</exception>
    /// <returns>True if the variable was set, false if it was not set</returns>
    [StashFn]
    public static bool Unset(string name)
    {
        if (name.Length == 0)
            throw new ValueError("'env.unset': name must not be empty.");
        if (name.Contains('='))
            throw new ValueError("'env.unset': name must not contain '='.");
        if (name.Contains('\0'))
            throw new ValueError("'env.unset': name must not contain null characters.");

        bool existed = System.Environment.GetEnvironmentVariable(name) is not null;
        System.Environment.SetEnvironmentVariable(name, null);
        return existed;
    }

    /// <summary>
    /// The current working directory path. Re-read on every access because
    /// <c>env.chdir</c> can change it at runtime.
    /// </summary>
    /// <exception cref="IOError">if the current directory cannot be determined</exception>
    [StashMember(Stability = Stability.Live, Capability = StashCapabilities.Environment,
        ReturnType = "string", Throws = new[] { typeof(IOError) })]
    public static string Cwd(IInterpreterContext ctx) => System.Environment.CurrentDirectory;

    /// <summary>
    /// The current user's home directory path. Cached on first access; stable for the
    /// process lifetime.
    /// </summary>
    [StashMember(Capability = StashCapabilities.Environment, ReturnType = "string")]
    public static string Home(IInterpreterContext ctx) =>
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// The machine's hostname. Cached on first access; stable for the process lifetime.
    /// </summary>
    [StashMember(Capability = StashCapabilities.Environment, ReturnType = "string")]
    public static string Hostname(IInterpreterContext ctx) => System.Environment.MachineName;

    /// <summary>
    /// The current user's login name. Cached on first access; stable for the process
    /// lifetime.
    /// </summary>
    [StashMember(Capability = StashCapabilities.Environment, ReturnType = "string")]
    public static string User(IInterpreterContext ctx) => System.Environment.UserName;

    /// <summary>Loads environment variables from a .env file. Optionally prefixes all variable names. Returns the number of variables loaded.</summary>
    /// <param name="path">Path to the .env file</param>
    /// <param name="prefix">Optional prefix to prepend to all variable names</param>
    /// <exception cref="IOError">if the .env file cannot be read</exception>
    /// <returns>The number of variables loaded</returns>
    [StashFn]
    public static long LoadFile(IInterpreterContext ctx, string path, string prefix = "")
    {
        var filePath = ctx.ExpandTilde(path);

        string text;
        try
        {
            text = System.IO.File.ReadAllText(filePath);
        }
        catch (System.IO.IOException e)
        {
            throw new IOError("env.loadFile: " + e.Message);
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
    }

    /// <summary>Saves all current environment variables to a .env file at the given path.</summary>
    /// <param name="path">Path to write the .env file</param>
    /// <exception cref="IOError">if the file cannot be written</exception>
    [StashFn]
    public static void SaveFile(IInterpreterContext ctx, string path)
    {
        var filePath = ctx.ExpandTilde(path);

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
            System.IO.File.WriteAllText(filePath, sb.ToString(), StashEncodings.Utf8NoBom);
        }
        catch (System.IO.IOException e)
        {
            throw new IOError("env.saveFile: " + e.Message);
        }

    }

    /// <summary>Changes the current working directory to the given path and pushes it onto the directory stack.</summary>
    /// <param name="path">The directory path to change to</param>
    /// <exception cref="CommandError">if the directory does not exist</exception>
    /// <exception cref="TypeError">if `path` is not a string</exception>
    /// <returns>null</returns>
    // Raw: delegates to CurrentProcessImpl.Chdir which takes raw ReadOnlySpan<StashValue>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue Chdir(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => CurrentProcessImpl.Chdir(ctx, args, "env.chdir");

    /// <summary>Pops the top directory from the stack, changes cwd back to the new top, and returns the popped path. Throws CommandError if the stack is at its root entry.</summary>
    /// <exception cref="CommandError">if the directory stack is already at its root entry</exception>
    /// <returns>The directory path that was popped</returns>
    // Raw: delegates to CurrentProcessImpl.PopDir which takes raw ReadOnlySpan<StashValue>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue PopDir(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => CurrentProcessImpl.PopDir(ctx, args, "env.popDir");

    /// <summary>Returns a copy of the directory stack, oldest entry first.</summary>
    /// <returns>An array of directory path strings</returns>
    // Raw: delegates to CurrentProcessImpl.DirStack which takes raw ReadOnlySpan<StashValue>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue DirStack(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => CurrentProcessImpl.DirStack(ctx, args, "env.dirStack");

    /// <summary>Returns the number of entries in the directory stack.</summary>
    /// <returns>The depth as an integer</returns>
    // Raw: delegates to CurrentProcessImpl.DirStackDepth which takes raw ReadOnlySpan<StashValue>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue DirStackDepth(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => CurrentProcessImpl.DirStackDepth(ctx, args, "env.dirStackDepth");

    /// <summary>Temporarily changes the working directory to the given path, calls fn(), then restores the original directory. Returns fn's return value.</summary>
    /// <param name="path">The directory to temporarily change to</param>
    /// <param name="fn">The function to execute in the new directory</param>
    /// <exception cref="IOError">if the directory does not exist</exception>
    /// <exception cref="TypeError">if `path` is not a string or `fn` is not callable</exception>
    /// <returns>The return value of fn</returns>
    // Raw: delegates to CurrentProcessImpl.WithDir which takes raw ReadOnlySpan<StashValue>
    [StashFn(Raw = true, ReturnType = "any")]
    private static StashValue WithDir(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
        => CurrentProcessImpl.WithDir(ctx, args, "env.withDir");

    /// <summary>Exits the current process with the given integer exit code (default 0). Runs all pending defer blocks before terminating. Cannot be caught by try/catch.</summary>
    /// <param name="code">The exit code. Defaults to 0</param>
    /// <returns>Does not return — exits the process</returns>
    [StashFn]
    public static void Exit(IInterpreterContext ctx, long code = 0L)
    {
        GlobalBuiltIns.EmitExitImpl(ctx, code);
    }
}
