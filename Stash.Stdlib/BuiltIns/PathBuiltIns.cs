using System;
using Stash.Runtime;
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

        ns.Function("abs", [Param("p", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var p = SvArgs.String(args, 0, "path.abs");

            return StashValue.FromObj(System.IO.Path.GetFullPath(p));
        },
            returnType: "string",
            documentation: "Returns the absolute path for the given path string.\n@param p The path\n@return The absolute path");

        ns.Function("dir", [Param("p", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var p = SvArgs.String(args, 0, "path.dir");

            return StashValue.FromObj(System.IO.Path.GetDirectoryName(p) ?? "");
        },
            returnType: "string",
            documentation: "Returns the directory component of the path.\n@param p The path\n@return The directory portion");

        ns.Function("base", [Param("p", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var p = SvArgs.String(args, 0, "path.base");

            return StashValue.FromObj(System.IO.Path.GetFileName(p));
        },
            returnType: "string",
            documentation: "Returns the filename (including extension) from the path.\n@param p The path\n@return The filename with extension");

        ns.Function("ext", [Param("p", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var p = SvArgs.String(args, 0, "path.ext");

            return StashValue.FromObj(System.IO.Path.GetExtension(p));
        },
            returnType: "string",
            documentation: "Returns the file extension including the dot.\n@param p The path\n@return The file extension");

        ns.Function("join", [Param("a", "string"), Param("b", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var a = SvArgs.String(args, 0, "path.join");
            var b = SvArgs.String(args, 1, "path.join");

            return StashValue.FromObj(System.IO.Path.Combine(a, b));
        },
            returnType: "string",
            documentation: "Joins two path segments using the platform path separator.\n@param a The first path segment\n@param b The second path segment\n@return The combined path");

        ns.Function("name", [Param("p", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var p = SvArgs.String(args, 0, "path.name");

            return StashValue.FromObj(System.IO.Path.GetFileNameWithoutExtension(p));
        },
            returnType: "string",
            documentation: "Returns the filename without extension.\n@param p The path\n@return The filename without extension");

        // ── Additional path utilities ────────────────────────────────────

        ns.Function("normalize", [Param("p", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var p = SvArgs.String(args, 0, "path.normalize");

            return StashValue.FromObj(System.IO.Path.GetFullPath(p));
        },
            returnType: "string",
            documentation: "Normalizes the path by resolving '..' and '.' segments.\n@param p The path to normalize\n@return The normalized path");

        ns.Function("isAbsolute", [Param("p", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var p = SvArgs.String(args, 0, "path.isAbsolute");

            return StashValue.FromBool(System.IO.Path.IsPathRooted(p));
        },
            returnType: "bool",
            documentation: "Returns true if the path is absolute, false otherwise.\n@param p The path\n@return Whether the path is absolute");

        ns.Function("relative", [Param("from", "string"), Param("to", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var from = SvArgs.String(args, 0, "path.relative");
            var to = SvArgs.String(args, 1, "path.relative");

            var fromUri = new System.Uri(System.IO.Path.GetFullPath(from + System.IO.Path.DirectorySeparatorChar));
            var toUri = new System.Uri(System.IO.Path.GetFullPath(to));
            var relativeUri = fromUri.MakeRelativeUri(toUri);
            return StashValue.FromObj(System.Uri.UnescapeDataString(relativeUri.ToString()));
        },
            returnType: "string",
            documentation: "Returns the relative path from 'from' to 'to'.\n@param from The source path\n@param to The target path\n@return The relative path");

        ns.Function("separator", [], static (IInterpreterContext _, ReadOnlySpan<StashValue> _) =>
        {
            return StashValue.FromObj(System.IO.Path.DirectorySeparatorChar.ToString());
        },
            returnType: "string",
            documentation: "Returns the platform-specific path separator character.\n@return The path separator (e.g. '/' on Linux/macOS, '\\' on Windows)");

        return ns.Build();
    }
}
