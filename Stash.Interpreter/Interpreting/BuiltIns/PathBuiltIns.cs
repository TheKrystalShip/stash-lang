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

        // ── Additional path utilities ────────────────────────────────────

        pathNs.Define("normalize", new BuiltInFunction("path.normalize", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.normalize' must be a string.");
            }

            return System.IO.Path.GetFullPath(p);
        }));

        pathNs.Define("isAbsolute", new BuiltInFunction("path.isAbsolute", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.isAbsolute' must be a string.");
            }

            return System.IO.Path.IsPathRooted(p);
        }));

        pathNs.Define("relative", new BuiltInFunction("path.relative", 2, (_, args) =>
        {
            if (args[0] is not string from)
            {
                throw new RuntimeError("First argument to 'path.relative' must be a string.");
            }

            if (args[1] is not string to)
            {
                throw new RuntimeError("Second argument to 'path.relative' must be a string.");
            }

            var fromUri = new System.Uri(System.IO.Path.GetFullPath(from + System.IO.Path.DirectorySeparatorChar));
            var toUri = new System.Uri(System.IO.Path.GetFullPath(to));
            var relativeUri = fromUri.MakeRelativeUri(toUri);
            return System.Uri.UnescapeDataString(relativeUri.ToString());
        }));

        pathNs.Define("separator", new BuiltInFunction("path.separator", 0, (_, args) =>
        {
            return System.IO.Path.DirectorySeparatorChar.ToString();
        }));

        globals.Define("path", pathNs);
    }
}
