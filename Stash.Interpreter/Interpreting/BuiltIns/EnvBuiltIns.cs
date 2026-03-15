namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'env' namespace built-in functions.
/// </summary>
public static class EnvBuiltIns
{
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

        globals.Define("env", envNs);
    }
}
