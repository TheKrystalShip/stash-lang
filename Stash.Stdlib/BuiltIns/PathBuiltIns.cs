using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

namespace Stash.Stdlib.BuiltIns;

/// <summary>
/// Registers the 'path' namespace built-in functions.
/// </summary>
public static class PathBuiltIns
{
    public static NamespaceDefinition Define()
    {
        // ── path namespace ───────────────────────────────────────────────
        var ns = new NamespaceBuilder("path");

        ns.Function("abs", [Param("p", "string")], (_, args) =>
        {
            var p = Args.String(args, 0, "path.abs");

            return System.IO.Path.GetFullPath(p);
        },
            returnType: "string",
            documentation: "Returns the absolute path for the given path string.\n@param p The path\n@return The absolute path");

        ns.Function("dir", [Param("p", "string")], (_, args) =>
        {
            var p = Args.String(args, 0, "path.dir");

            return System.IO.Path.GetDirectoryName(p) ?? "";
        },
            returnType: "string",
            documentation: "Returns the directory component of the path.\n@param p The path\n@return The directory portion");

        ns.Function("base", [Param("p", "string")], (_, args) =>
        {
            var p = Args.String(args, 0, "path.base");

            return System.IO.Path.GetFileName(p);
        },
            returnType: "string",
            documentation: "Returns the filename (including extension) from the path.\n@param p The path\n@return The filename with extension");

        ns.Function("ext", [Param("p", "string")], (_, args) =>
        {
            var p = Args.String(args, 0, "path.ext");

            return System.IO.Path.GetExtension(p);
        },
            returnType: "string",
            documentation: "Returns the file extension including the dot.\n@param p The path\n@return The file extension");

        ns.Function("join", [Param("a", "string"), Param("b", "string")], (_, args) =>
        {
            var a = Args.String(args, 0, "path.join");
            var b = Args.String(args, 1, "path.join");

            return System.IO.Path.Combine(a, b);
        },
            returnType: "string",
            documentation: "Joins two path segments using the platform path separator.\n@param a The first path segment\n@param b The second path segment\n@return The combined path");

        ns.Function("name", [Param("p", "string")], (_, args) =>
        {
            var p = Args.String(args, 0, "path.name");

            return System.IO.Path.GetFileNameWithoutExtension(p);
        },
            returnType: "string",
            documentation: "Returns the filename without extension.\n@param p The path\n@return The filename without extension");

        // ── Additional path utilities ────────────────────────────────────

        ns.Function("normalize", [Param("p", "string")], (_, args) =>
        {
            var p = Args.String(args, 0, "path.normalize");

            return System.IO.Path.GetFullPath(p);
        },
            returnType: "string",
            documentation: "Normalizes the path by resolving '..' and '.' segments.\n@param p The path to normalize\n@return The normalized path");

        ns.Function("isAbsolute", [Param("p", "string")], (_, args) =>
        {
            var p = Args.String(args, 0, "path.isAbsolute");

            return System.IO.Path.IsPathRooted(p);
        },
            returnType: "bool",
            documentation: "Returns true if the path is absolute, false otherwise.\n@param p The path\n@return Whether the path is absolute");

        ns.Function("relative", [Param("from", "string"), Param("to", "string")], (_, args) =>
        {
            var from = Args.String(args, 0, "path.relative");
            var to = Args.String(args, 1, "path.relative");

            var fromUri = new System.Uri(System.IO.Path.GetFullPath(from + System.IO.Path.DirectorySeparatorChar));
            var toUri = new System.Uri(System.IO.Path.GetFullPath(to));
            var relativeUri = fromUri.MakeRelativeUri(toUri);
            return System.Uri.UnescapeDataString(relativeUri.ToString());
        },
            returnType: "string",
            documentation: "Returns the relative path from 'from' to 'to'.\n@param from The source path\n@param to The target path\n@return The relative path");

        ns.Function("separator", [], (_, _) =>
        {
            return System.IO.Path.DirectorySeparatorChar.ToString();
        },
            returnType: "string",
            documentation: "Returns the platform-specific path separator character.\n@return The path separator (e.g. '/' on Linux/macOS, '\\' on Windows)");

        return ns.Build();
    }
}
