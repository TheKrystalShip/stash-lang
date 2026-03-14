namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;

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

        globals.Define("env", envNs);
    }
}
