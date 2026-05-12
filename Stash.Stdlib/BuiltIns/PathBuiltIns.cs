namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib;

/// <summary>
/// Registers the 'path' namespace built-in functions.
/// </summary>
[StashNamespace]
public static partial class PathBuiltIns
{
    /// <summary>Returns the absolute path for the given path string.</summary>
    /// <param name="p">The path</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The absolute path</returns>
    [StashFn]
    public static string Abs(string p) => System.IO.Path.GetFullPath(p);

    /// <summary>Returns the directory component of the path.</summary>
    /// <param name="p">The path</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The directory portion</returns>
    [StashFn]
    public static string Dir(string p) => System.IO.Path.GetDirectoryName(p) ?? "";

    /// <summary>Returns the filename (including extension) from the path.</summary>
    /// <param name="p">The path</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The filename with extension</returns>
    [StashFn]
    public static string Base(string p) => System.IO.Path.GetFileName(p);

    /// <summary>Returns the file extension including the dot.</summary>
    /// <param name="p">The path</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The file extension</returns>
    [StashFn]
    public static string Ext(string p) => System.IO.Path.GetExtension(p);

    /// <summary>Joins two or more path segments using the platform path separator.</summary>
    /// <param name="a">The first path segment</param>
    /// <param name="b">The second path segment</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The combined path</returns>
    [StashFn]
    public static string Join(string a, string b, params StashValue[] rest)
    {
        var accumulated = System.IO.Path.Combine(a, b);
        for (int i = 0; i < rest.Length; i++)
        {
            var sv = rest[i];
            if (!(sv.IsObj && sv.AsObj is string seg))
                throw new RuntimeError($"{SvArgs.Ordinal(i + 2)} argument to 'path.join' must be a string.");
            accumulated = System.IO.Path.Combine(accumulated, seg);
        }
        return accumulated;
    }

    /// <summary>Returns the filename without extension.</summary>
    /// <param name="p">The path</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The filename without extension</returns>
    [StashFn]
    public static string Name(string p) => System.IO.Path.GetFileNameWithoutExtension(p);

    /// <summary>Normalizes the path by resolving '..' and '.' segments.</summary>
    /// <param name="p">The path to normalize</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The normalized path</returns>
    [StashFn]
    public static string Normalize(string p) => System.IO.Path.GetFullPath(p);

    /// <summary>Returns true if the path is absolute, false otherwise.</summary>
    /// <param name="p">The path</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>Whether the path is absolute</returns>
    [StashFn]
    public static bool IsAbsolute(string p) => System.IO.Path.IsPathRooted(p);

    /// <summary>Returns the relative path from 'from' to 'to'.</summary>
    /// <param name="from">The source path</param>
    /// <param name="to">The target path</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The relative path</returns>
    [StashFn]
    public static string Relative(string from, string to)
    {
        var fromUri = new System.Uri(System.IO.Path.GetFullPath(from + System.IO.Path.DirectorySeparatorChar));
        var toUri = new System.Uri(System.IO.Path.GetFullPath(to));
        var relativeUri = fromUri.MakeRelativeUri(toUri);
        return System.Uri.UnescapeDataString(relativeUri.ToString());
    }

    /// <summary>Returns the platform-specific path separator character.</summary>
    /// <returns>The path separator (e.g. '/' on Linux/macOS, '\' on Windows)</returns>
    [StashFn]
    public static string Separator() => System.IO.Path.DirectorySeparatorChar.ToString();
}
