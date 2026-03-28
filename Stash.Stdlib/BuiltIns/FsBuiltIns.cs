namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>fs</c> namespace built-in functions for filesystem operations.
/// </summary>
/// <remarks>
/// <para>
/// Provides 27 functions for reading, writing, and inspecting the filesystem:
/// <c>fs.readFile</c>, <c>fs.writeFile</c>, <c>fs.appendFile</c>, <c>fs.readLines</c>,
/// <c>fs.createFile</c>, <c>fs.exists</c>, <c>fs.dirExists</c>, <c>fs.pathExists</c>,
/// <c>fs.createDir</c>, <c>fs.listDir</c>, <c>fs.delete</c>, <c>fs.copy</c>, <c>fs.move</c>,
/// <c>fs.size</c>, <c>fs.stat</c>, <c>fs.glob</c>, <c>fs.walk</c>, <c>fs.isFile</c>,
/// <c>fs.isDir</c>, <c>fs.isSymlink</c>, <c>fs.symlink</c>, <c>fs.modifiedAt</c>,
/// <c>fs.readable</c>, <c>fs.writable</c>, <c>fs.executable</c>, <c>fs.tempFile</c>,
/// and <c>fs.tempDir</c>.
/// </para>
/// <para>
/// This namespace is only registered when the <see cref="StashCapabilities.FileSystem"/>
/// capability is enabled. All path arguments support <c>~</c> home-directory expansion.
/// </para>
/// </remarks>
public static class FsBuiltIns
{
    /// <summary>
    /// Expands a leading <c>~</c> to the user's home directory in the given path.
    /// </summary>
    /// <param name="path">The path to expand.</param>
    /// <returns>The path with <c>~</c> replaced by the home directory, or the original path if no <c>~</c> prefix.</returns>
    public static NamespaceDefinition Define()
    {
        // ── fs namespace ──────────────────────────────────────────────────
        var ns = new NamespaceBuilder("fs");
        ns.RequiresCapability(StashCapabilities.FileSystem);

        // fs.readFile(path) — Reads the entire contents of a file as a string. Throws on I/O error.
        ns.Function("readFile", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.readFile' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try { return System.IO.File.ReadAllText(path); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot read file '{path}': {e.Message}"); }
        });

        // fs.writeFile(path, content) — Writes a string to a file, creating or overwriting it. Returns null.
        ns.Function("writeFile", [Param("path", "string"), Param("content", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("First argument to 'fs.writeFile' must be a string.");
            }

            if (args[1] is not string content)
            {
                throw new RuntimeError("Second argument to 'fs.writeFile' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try { System.IO.File.WriteAllText(path, content); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot write file '{path}': {e.Message}"); }
            return null;
        });

        // fs.exists(path) — Returns true if the given path is an existing file, false otherwise.
        ns.Function("exists", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.exists' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            return System.IO.File.Exists(path);
        });

        // fs.dirExists(path) — Returns true if the given path is an existing directory, false otherwise.
        ns.Function("dirExists", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.dirExists' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            return System.IO.Directory.Exists(path);
        });

        // fs.pathExists(path) — Returns true if the given path exists as either a file or directory, false otherwise.
        ns.Function("pathExists", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.pathExists' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            return System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
        });

        // fs.createDir(path) — Creates a directory (and any missing parent directories). Returns null. No-ops if the directory already exists.
        ns.Function("createDir", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.createDir' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try { System.IO.Directory.CreateDirectory(path); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot create directory '{path}': {e.Message}"); }
            return null;
        });

        // fs.delete(path) — Deletes a file or recursively deletes a directory. Throws if the path does not exist.
        ns.Function("delete", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.delete' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
                else if (System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.Delete(path, true);
                }
                else
                {
                    throw new RuntimeError($"Path does not exist: '{path}'.");
                }
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot delete '{path}': {e.Message}"); }
            return null;
        });

        // fs.copy(src, dst) — Copies a file from src to dst, overwriting dst if it exists. Returns null.
        ns.Function("copy", [Param("src", "string"), Param("dst", "string")], (ctx, args) =>
        {
            if (args[0] is not string src)
            {
                throw new RuntimeError("First argument to 'fs.copy' must be a string.");
            }

            if (args[1] is not string dst)
            {
                throw new RuntimeError("Second argument to 'fs.copy' must be a string.");
            }
            src = ctx.ExpandTilde(src);
            dst = ctx.ExpandTilde(dst);

            try { System.IO.File.Copy(src, dst, overwrite: true); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot copy '{src}' to '{dst}': {e.Message}"); }
            return null;
        });

        // fs.move(src, dst) — Moves or renames a file from src to dst, overwriting dst if it exists. Returns null.
        ns.Function("move", [Param("src", "string"), Param("dst", "string")], (ctx, args) =>
        {
            if (args[0] is not string src)
            {
                throw new RuntimeError("First argument to 'fs.move' must be a string.");
            }

            if (args[1] is not string dst)
            {
                throw new RuntimeError("Second argument to 'fs.move' must be a string.");
            }
            src = ctx.ExpandTilde(src);
            dst = ctx.ExpandTilde(dst);

            try { System.IO.File.Move(src, dst, overwrite: true); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot move '{src}' to '{dst}': {e.Message}"); }
            return null;
        });

        // fs.size(path) — Returns the size of a file in bytes (integer).
        ns.Function("size", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.size' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try { return new System.IO.FileInfo(path).Length; }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot get size of '{path}': {e.Message}"); }
        });

        // fs.listDir(path) — Returns an array of all file and directory paths directly inside the given directory.
        ns.Function("listDir", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.listDir' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try
            {
                var entries = System.IO.Directory.GetFileSystemEntries(path);
                var result = new List<object?>();
                foreach (var entry in entries)
                {
                    result.Add(entry);
                }

                return result;
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot list directory '{path}': {e.Message}"); }
        });

        // fs.appendFile(path, content) — Appends a string to the end of a file, creating it if it doesn't exist. Returns null.
        ns.Function("appendFile", [Param("path", "string"), Param("content", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("First argument to 'fs.appendFile' must be a string.");
            }

            if (args[1] is not string content)
            {
                throw new RuntimeError("Second argument to 'fs.appendFile' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try { System.IO.File.AppendAllText(path, content); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot append to file '{path}': {e.Message}"); }
            return null;
        });

        // fs.readLines(path) — Reads all lines of a file and returns them as an array of strings.
        ns.Function("readLines", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.readLines' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try
            {
                var lines = System.IO.File.ReadAllLines(path);
                return new List<object?>(lines);
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot read file '{path}': {e.Message}"); }
        });

        // fs.glob(pattern) — Returns an array of file paths matching a glob pattern (e.g. "src/**/*.cs"). Supports wildcards in filename only.
        ns.Function("glob", [Param("pattern", "string")], (ctx, args) =>
        {
            if (args[0] is not string pattern)
            {
                throw new RuntimeError("Argument to 'fs.glob' must be a string.");
            }
            pattern = ctx.ExpandTilde(pattern);

            try
            {
                string dir = System.IO.Path.GetDirectoryName(pattern) ?? ".";
                string filePattern = System.IO.Path.GetFileName(pattern);
                if (string.IsNullOrEmpty(dir))
                {
                    dir = ".";
                }

                if (string.IsNullOrEmpty(filePattern))
                {
                    filePattern = "*";
                }

                var files = System.IO.Directory.GetFiles(dir, filePattern, System.IO.SearchOption.AllDirectories);
                return new List<object?>(files);
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"fs.glob failed: {e.Message}"); }
        });

        // fs.isFile(path) — Returns true if the path refers to an existing regular file.
        ns.Function("isFile", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.isFile' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            return System.IO.File.Exists(path);
        });

        // fs.isDir(path) — Returns true if the path refers to an existing directory.
        ns.Function("isDir", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.isDir' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            return System.IO.Directory.Exists(path);
        });

        // fs.isSymlink(path) — Returns true if the path is an existing symbolic link (reparse point).
        ns.Function("isSymlink", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.isSymlink' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try
            {
                var info = new System.IO.FileInfo(path);
                return info.Exists && info.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint);
            }
            catch (System.IO.IOException)
            {
                return false;
            }
        });

        // fs.tempFile() — Creates a new empty temporary file and returns its path string.
        ns.Function("tempFile", [], (_, _) =>
        {
            return System.IO.Path.GetTempFileName();
        });

        // fs.tempDir() — Creates a new temporary directory with a random name and returns its path string.
        ns.Function("tempDir", [], (_, _) =>
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        });

        // fs.modifiedAt(path) — Returns the last-modified time of a file as a Unix timestamp (float, seconds since epoch).
        ns.Function("modifiedAt", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.modifiedAt' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try
            {
                var info = new System.IO.FileInfo(path);
                return (double)new System.DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0;
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot get modified time for '{path}': {e.Message}"); }
        });

        // fs.walk(path) — Recursively walks a directory and returns an array of all file paths within it (all depths).
        ns.Function("walk", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.walk' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try
            {
                var files = System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories);
                return new List<object?>(files);
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"fs.walk failed: {e.Message}"); }
        });

        // fs.readable(path) — Returns true if the path exists and the current process can read it.
        ns.Function("readable", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.readable' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try
            {
                if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                {
                    return false;
                }

                using var stream = System.IO.File.OpenRead(path);
                return true;
            }
            catch (System.UnauthorizedAccessException) { return false; }
            catch (System.IO.IOException) { return false; }
        });

        // fs.writable(path) — Returns true if the path exists and the current process can write to it.
        ns.Function("writable", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.writable' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try
            {
                if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                {
                    return false;
                }

                if (System.IO.File.Exists(path))
                {
                    using var stream = System.IO.File.OpenWrite(path);
                    return true;
                }
                // For directories, check by attempting to create a temp file
                var testFile = System.IO.Path.Combine(path, System.IO.Path.GetRandomFileName());
                using (System.IO.File.Create(testFile)) { }
                System.IO.File.Delete(testFile);
                return true;
            }
            catch (System.UnauthorizedAccessException) { return false; }
            catch (System.IO.IOException) { return false; }
        });

        // fs.executable(path) — Returns true if the path is an existing file and appears to be executable (by extension on Windows, by Unix mode bits on Unix).
        ns.Function("executable", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.executable' must be a string.");
            }
            path = ctx.ExpandTilde(path);

            try
            {
                if (!System.IO.File.Exists(path))
                {
                    return false;
                }

                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    // On Windows, check file extension
                    var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    return ext is ".exe" or ".cmd" or ".bat" or ".com" or ".ps1";
                }
                else
                {
                    // On Unix, check executable permission via file mode
                    var mode = System.IO.File.GetUnixFileMode(path);
                    return (mode & (System.IO.UnixFileMode.UserExecute |
                                    System.IO.UnixFileMode.GroupExecute |
                                    System.IO.UnixFileMode.OtherExecute)) != 0;
                }
            }
            catch (System.IO.IOException) { return false; }
        });

        // fs.createFile(path) — Creates an empty file at path, or updates its last-modified time if it already exists (similar to Unix touch). Returns null.
        ns.Function("createFile", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.createFile' must be a string.");
            }

            path = ctx.ExpandTilde(path);

            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.SetLastWriteTimeUtc(path, System.DateTime.UtcNow);
                }
                else
                {
                    using (System.IO.File.Create(path)) { }
                }
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot create file '{path}': {e.Message}"); }
            return null;
        });

        // fs.symlink(target, linkPath) — Creates a symbolic link at linkPath pointing to target. Returns null.
        ns.Function("symlink", [Param("target", "string"), Param("linkPath", "string")], (ctx, args) =>
        {
            if (args[0] is not string target)
            {
                throw new RuntimeError("First argument to 'fs.symlink' must be a string.");
            }

            if (args[1] is not string linkPath)
            {
                throw new RuntimeError("Second argument to 'fs.symlink' must be a string.");
            }

            target = ctx.ExpandTilde(target);
            linkPath = ctx.ExpandTilde(linkPath);

            try
            {
                System.IO.File.CreateSymbolicLink(linkPath, target);
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot create symlink '{linkPath}': {e.Message}"); }
            return null;
        });

        // fs.stat(path) — Returns a dict with file metadata: size (int), isFile (bool), isDir (bool), isSymlink (bool), modified (float), created (float), name (string).
        ns.Function("stat", [Param("path", "string")], (ctx, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.stat' must be a string.");
            }

            path = ctx.ExpandTilde(path);

            try
            {
                var info = new System.IO.FileInfo(path);
                if (!info.Exists && !System.IO.Directory.Exists(path))
                {
                    throw new RuntimeError($"Path does not exist: '{path}'.");
                }

                var isDir = System.IO.Directory.Exists(path);
                var result = new StashDictionary();
                result.Set("size", isDir ? 0L : info.Length);
                result.Set("isFile", info.Exists && !isDir);
                result.Set("isDir", isDir);
                bool isSymlink = false;
                try { isSymlink = (System.IO.File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) != 0; } catch { }
                result.Set("isSymlink", isSymlink);
                result.Set("modified", (double)new System.DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0);
                result.Set("created", (double)new System.DateTimeOffset(info.CreationTimeUtc).ToUnixTimeMilliseconds() / 1000.0);
                result.Set("name", System.IO.Path.GetFileName(path));
                return result;
            }
            catch (RuntimeError) { throw; }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot stat '{path}': {e.Message}"); }
        });

        return ns.Build();
    }
}
