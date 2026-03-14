using Stash.Interpreting.Types;

namespace Stash.Interpreting.BuiltIns;

/// <summary>
/// Registers the 'path' namespace built-in functions.
/// </summary>
public static class PathBuiltIns
{
    public static void Register(Environment globals)
    {
        // ── path namespace ───────────────────────────────────────────────
        var pathNs = new StashNamespace("path");

        pathNs.Define("abs", new BuiltInFunction("path.abs", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.abs' must be a string.");
            }

            return System.IO.Path.GetFullPath(p);
        }));

        pathNs.Define("dir", new BuiltInFunction("path.dir", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.dir' must be a string.");
            }

            return System.IO.Path.GetDirectoryName(p) ?? "";
        }));

        pathNs.Define("base", new BuiltInFunction("path.base", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.base' must be a string.");
            }

            return System.IO.Path.GetFileName(p);
        }));

        pathNs.Define("ext", new BuiltInFunction("path.ext", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.ext' must be a string.");
            }

            return System.IO.Path.GetExtension(p);
        }));

        pathNs.Define("join", new BuiltInFunction("path.join", 2, (_, args) =>
        {
            if (args[0] is not string a)
            {
                throw new RuntimeError("First argument to 'path.join' must be a string.");
            }

            if (args[1] is not string b)
            {
                throw new RuntimeError("Second argument to 'path.join' must be a string.");
            }

            return System.IO.Path.Combine(a, b);
        }));

        pathNs.Define("name", new BuiltInFunction("path.name", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.name' must be a string.");
            }

            return System.IO.Path.GetFileNameWithoutExtension(p);
        }));

        globals.Define("path", pathNs);
    }
}
