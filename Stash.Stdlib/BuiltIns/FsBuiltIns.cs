namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>fs</c> namespace built-in functions for filesystem operations.
/// </summary>
/// <remarks>
/// <para>
/// Provides 31 functions for reading, writing, and inspecting the filesystem:
/// <c>fs.readFile</c>, <c>fs.writeFile</c>, <c>fs.appendFile</c>, <c>fs.readLines</c>,
/// <c>fs.createFile</c>, <c>fs.exists</c>, <c>fs.dirExists</c>, <c>fs.pathExists</c>,
/// <c>fs.createDir</c>, <c>fs.listDir</c>, <c>fs.delete</c>, <c>fs.copy</c>, <c>fs.move</c>,
/// <c>fs.size</c>, <c>fs.stat</c>, <c>fs.glob</c>, <c>fs.walk</c>, <c>fs.isFile</c>,
/// <c>fs.isDir</c>, <c>fs.isSymlink</c>, <c>fs.symlink</c>, <c>fs.modifiedAt</c>,
/// <c>fs.readable</c>, <c>fs.writable</c>, <c>fs.executable</c>, <c>fs.tempFile</c>,
/// <c>fs.tempDir</c>, <c>fs.getPermissions</c>, <c>fs.setPermissions</c>,
/// <c>fs.setReadOnly</c>, and <c>fs.setExecutable</c>.
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
            var path = Args.String(args, 0, "fs.readFile");
            path = ctx.ExpandTilde(path);

            try { return System.IO.File.ReadAllText(path); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot read file '{path}': {e.Message}"); }
        });

        // fs.writeFile(path, content) — Writes a string to a file, creating or overwriting it. Returns null.
        ns.Function("writeFile", [Param("path", "string"), Param("content", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.writeFile");
            var content = Args.String(args, 1, "fs.writeFile");
            path = ctx.ExpandTilde(path);

            try { System.IO.File.WriteAllText(path, content); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot write file '{path}': {e.Message}"); }
            return null;
        });

        // fs.exists(path) — Returns true if the given path is an existing file, false otherwise.
        ns.Function("exists", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.exists");
            path = ctx.ExpandTilde(path);

            return System.IO.File.Exists(path);
        });

        // fs.dirExists(path) — Returns true if the given path is an existing directory, false otherwise.
        ns.Function("dirExists", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.dirExists");
            path = ctx.ExpandTilde(path);

            return System.IO.Directory.Exists(path);
        });

        // fs.pathExists(path) — Returns true if the given path exists as either a file or directory, false otherwise.
        ns.Function("pathExists", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.pathExists");
            path = ctx.ExpandTilde(path);

            return System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
        });

        // fs.createDir(path) — Creates a directory (and any missing parent directories). Returns null. No-ops if the directory already exists.
        ns.Function("createDir", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.createDir");
            path = ctx.ExpandTilde(path);

            try { System.IO.Directory.CreateDirectory(path); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot create directory '{path}': {e.Message}"); }
            return null;
        });

        // fs.delete(path) — Deletes a file or recursively deletes a directory. Throws if the path does not exist.
        ns.Function("delete", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.delete");
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
            var src = Args.String(args, 0, "fs.copy");
            var dst = Args.String(args, 1, "fs.copy");
            src = ctx.ExpandTilde(src);
            dst = ctx.ExpandTilde(dst);

            try { System.IO.File.Copy(src, dst, overwrite: true); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot copy '{src}' to '{dst}': {e.Message}"); }
            return null;
        });

        // fs.move(src, dst) — Moves or renames a file from src to dst, overwriting dst if it exists. Returns null.
        ns.Function("move", [Param("src", "string"), Param("dst", "string")], (ctx, args) =>
        {
            var src = Args.String(args, 0, "fs.move");
            var dst = Args.String(args, 1, "fs.move");
            src = ctx.ExpandTilde(src);
            dst = ctx.ExpandTilde(dst);

            try { System.IO.File.Move(src, dst, overwrite: true); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot move '{src}' to '{dst}': {e.Message}"); }
            return null;
        });

        // fs.size(path) — Returns the size of a file in bytes (integer).
        ns.Function("size", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.size");
            path = ctx.ExpandTilde(path);

            try { return new System.IO.FileInfo(path).Length; }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot get size of '{path}': {e.Message}"); }
        });

        // fs.listDir(path) — Returns an array of all file and directory paths directly inside the given directory.
        ns.Function("listDir", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.listDir");
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
            var path = Args.String(args, 0, "fs.appendFile");
            var content = Args.String(args, 1, "fs.appendFile");
            path = ctx.ExpandTilde(path);

            try { System.IO.File.AppendAllText(path, content); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot append to file '{path}': {e.Message}"); }
            return null;
        });

        // fs.readLines(path) — Reads all lines of a file and returns them as an array of strings.
        ns.Function("readLines", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.readLines");
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
            var pattern = Args.String(args, 0, "fs.glob");
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
            var path = Args.String(args, 0, "fs.isFile");
            path = ctx.ExpandTilde(path);

            return System.IO.File.Exists(path);
        });

        // fs.isDir(path) — Returns true if the path refers to an existing directory.
        ns.Function("isDir", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.isDir");
            path = ctx.ExpandTilde(path);

            return System.IO.Directory.Exists(path);
        });

        // fs.isSymlink(path) — Returns true if the path is an existing symbolic link (reparse point).
        ns.Function("isSymlink", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.isSymlink");
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
            var path = Args.String(args, 0, "fs.modifiedAt");
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
            var path = Args.String(args, 0, "fs.walk");
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
            var path = Args.String(args, 0, "fs.readable");
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
            var path = Args.String(args, 0, "fs.writable");
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
            var path = Args.String(args, 0, "fs.executable");
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
            var path = Args.String(args, 0, "fs.createFile");
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
            var target = Args.String(args, 0, "fs.symlink");
            var linkPath = Args.String(args, 1, "fs.symlink");
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
            var path = Args.String(args, 0, "fs.stat");
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

        // fs.getPermissions(path) — Returns a FilePermissions struct describing the file's permission bits.
        ns.Function("getPermissions", [Param("path", "string")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.getPermissions");
            path = ctx.ExpandTilde(path);

            try
            {
                if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                    throw new RuntimeError($"Path does not exist: '{path}'.");

                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    bool isReadOnly;
                    bool isExe;

                    if (System.IO.File.Exists(path))
                    {
                        var info = new System.IO.FileInfo(path);
                        isReadOnly = info.IsReadOnly;
                        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                        isExe = ext is ".exe" or ".cmd" or ".bat" or ".com" or ".ps1";
                    }
                    else
                    {
                        // Directory
                        var attrs = System.IO.File.GetAttributes(path);
                        isReadOnly = (attrs & System.IO.FileAttributes.ReadOnly) != 0;
                        isExe = false;
                    }

                    return new StashInstance("FilePermissions", new Dictionary<string, object?>
                    {
                        ["owner"] = new StashInstance("FilePermission", new Dictionary<string, object?>
                        {
                            ["read"] = true,
                            ["write"] = !isReadOnly,
                            ["execute"] = isExe,
                        }),
                        ["group"] = new StashInstance("FilePermission", new Dictionary<string, object?>
                        {
                            ["read"] = true,
                            ["write"] = !isReadOnly,
                            ["execute"] = isExe,
                        }),
                        ["others"] = new StashInstance("FilePermission", new Dictionary<string, object?>
                        {
                            ["read"] = true,
                            ["write"] = !isReadOnly,
                            ["execute"] = isExe,
                        }),
                    });
                }
                else
                {
                    var mode = System.IO.File.GetUnixFileMode(path);

                    var owner = new StashInstance("FilePermission", new Dictionary<string, object?>
                    {
                        ["read"] = (mode & System.IO.UnixFileMode.UserRead) != 0,
                        ["write"] = (mode & System.IO.UnixFileMode.UserWrite) != 0,
                        ["execute"] = (mode & System.IO.UnixFileMode.UserExecute) != 0,
                    });

                    var group = new StashInstance("FilePermission", new Dictionary<string, object?>
                    {
                        ["read"] = (mode & System.IO.UnixFileMode.GroupRead) != 0,
                        ["write"] = (mode & System.IO.UnixFileMode.GroupWrite) != 0,
                        ["execute"] = (mode & System.IO.UnixFileMode.GroupExecute) != 0,
                    });

                    var others = new StashInstance("FilePermission", new Dictionary<string, object?>
                    {
                        ["read"] = (mode & System.IO.UnixFileMode.OtherRead) != 0,
                        ["write"] = (mode & System.IO.UnixFileMode.OtherWrite) != 0,
                        ["execute"] = (mode & System.IO.UnixFileMode.OtherExecute) != 0,
                    });

                    return new StashInstance("FilePermissions", new Dictionary<string, object?>
                    {
                        ["owner"] = owner,
                        ["group"] = group,
                        ["others"] = others,
                    });
                }
            }
            catch (RuntimeError) { throw; }
            catch (System.UnauthorizedAccessException e) { throw new RuntimeError($"Cannot get permissions for '{path}': {e.Message}"); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot get permissions for '{path}': {e.Message}"); }
        }, returnType: "FilePermissions", documentation: "Returns a FilePermissions struct describing the read/write/execute permissions for owner, group, and others.\n@param path The path to inspect.\n@return A FilePermissions struct with owner, group, and others fields (each a FilePermission with read, write, execute bools).");

        // fs.setPermissions(path, permissions) — Sets file permissions from a FilePermissions struct.
        ns.Function("setPermissions", [Param("path", "string"), Param("permissions", "FilePermissions")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.setPermissions");
            var perms = Args.Instance(args, 1, "FilePermissions", "fs.setPermissions");
            path = ctx.ExpandTilde(path);

            try
            {
                if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                    throw new RuntimeError($"Path does not exist: '{path}'.");

                var ownerInst = perms.GetField("owner", null) as StashInstance
                    ?? throw new RuntimeError("'owner' field must be a FilePermission struct.");
                var groupInst = perms.GetField("group", null) as StashInstance
                    ?? throw new RuntimeError("'group' field must be a FilePermission struct.");
                var othersInst = perms.GetField("others", null) as StashInstance
                    ?? throw new RuntimeError("'others' field must be a FilePermission struct.");

                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    // Windows: only write permission (ReadOnly attribute) can be controlled
                    bool ownerWrite = ownerInst.GetField("write", null) as bool? ?? false;
                    if (System.IO.File.Exists(path))
                    {
                        new System.IO.FileInfo(path).IsReadOnly = !ownerWrite;
                    }
                    else
                    {
                        // Directory
                        var attrs = System.IO.File.GetAttributes(path);
                        if (!ownerWrite)
                            System.IO.File.SetAttributes(path, attrs | System.IO.FileAttributes.ReadOnly);
                        else
                            System.IO.File.SetAttributes(path, attrs & ~System.IO.FileAttributes.ReadOnly);
                    }
                }
                else
                {
                    var mode = System.IO.UnixFileMode.None;

                    if (ownerInst.GetField("read", null) as bool? ?? false) mode |= System.IO.UnixFileMode.UserRead;
                    if (ownerInst.GetField("write", null) as bool? ?? false) mode |= System.IO.UnixFileMode.UserWrite;
                    if (ownerInst.GetField("execute", null) as bool? ?? false) mode |= System.IO.UnixFileMode.UserExecute;

                    if (groupInst.GetField("read", null) as bool? ?? false) mode |= System.IO.UnixFileMode.GroupRead;
                    if (groupInst.GetField("write", null) as bool? ?? false) mode |= System.IO.UnixFileMode.GroupWrite;
                    if (groupInst.GetField("execute", null) as bool? ?? false) mode |= System.IO.UnixFileMode.GroupExecute;

                    if (othersInst.GetField("read", null) as bool? ?? false) mode |= System.IO.UnixFileMode.OtherRead;
                    if (othersInst.GetField("write", null) as bool? ?? false) mode |= System.IO.UnixFileMode.OtherWrite;
                    if (othersInst.GetField("execute", null) as bool? ?? false) mode |= System.IO.UnixFileMode.OtherExecute;

                    System.IO.File.SetUnixFileMode(path, mode);
                }
            }
            catch (RuntimeError) { throw; }
            catch (System.UnauthorizedAccessException e) { throw new RuntimeError($"Cannot set permissions on '{path}': {e.Message}"); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot set permissions on '{path}': {e.Message}"); }
            return null;
        }, returnType: "null", documentation: "Sets file permissions from a FilePermissions struct. On Unix, sets full rwx bits for owner/group/others. On Windows, controls the read-only attribute based on owner write permission.\n@param path The file path to modify.\n@param permissions A FilePermissions struct with owner, group, and others fields.");

        // fs.setReadOnly(path, readOnly) — Cross-platform convenience for toggling the read-only state.
        ns.Function("setReadOnly", [Param("path", "string"), Param("readOnly", "bool")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.setReadOnly");
            var readOnly = Args.Bool(args, 1, "fs.setReadOnly");
            path = ctx.ExpandTilde(path);

            try
            {
                if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                    throw new RuntimeError($"Path does not exist: '{path}'.");

                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    if (System.IO.File.Exists(path))
                    {
                        new System.IO.FileInfo(path).IsReadOnly = readOnly;
                    }
                    else
                    {
                        var attrs = System.IO.File.GetAttributes(path);
                        if (readOnly)
                            System.IO.File.SetAttributes(path, attrs | System.IO.FileAttributes.ReadOnly);
                        else
                            System.IO.File.SetAttributes(path, attrs & ~System.IO.FileAttributes.ReadOnly);
                    }
                }
                else
                {
                    var mode = System.IO.File.GetUnixFileMode(path);
                    if (readOnly)
                    {
                        mode &= ~(System.IO.UnixFileMode.UserWrite |
                                   System.IO.UnixFileMode.GroupWrite |
                                   System.IO.UnixFileMode.OtherWrite);
                    }
                    else
                    {
                        mode |= System.IO.UnixFileMode.UserWrite;
                    }
                    System.IO.File.SetUnixFileMode(path, mode);
                }
            }
            catch (RuntimeError) { throw; }
            catch (System.UnauthorizedAccessException e) { throw new RuntimeError($"Cannot set read-only on '{path}': {e.Message}"); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot set read-only on '{path}': {e.Message}"); }
            return null;
        }, returnType: "null", documentation: "Sets or clears the read-only state of a file. On Unix, toggles write bits. On Windows, sets the ReadOnly file attribute.\n@param path The file path to modify.\n@param readOnly True to make the file read-only, false to make it writable.");

        // fs.setExecutable(path, executable) — Sets or clears the executable bit (Unix) or is a no-op on Windows.
        ns.Function("setExecutable", [Param("path", "string"), Param("executable", "bool")], (ctx, args) =>
        {
            var path = Args.String(args, 0, "fs.setExecutable");
            var executable = Args.Bool(args, 1, "fs.setExecutable");
            path = ctx.ExpandTilde(path);

            try
            {
                if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                    throw new RuntimeError($"Path does not exist: '{path}'.");

                if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    var mode = System.IO.File.GetUnixFileMode(path);
                    if (executable)
                    {
                        mode |= System.IO.UnixFileMode.UserExecute;
                    }
                    else
                    {
                        mode &= ~(System.IO.UnixFileMode.UserExecute |
                                   System.IO.UnixFileMode.GroupExecute |
                                   System.IO.UnixFileMode.OtherExecute);
                    }
                    System.IO.File.SetUnixFileMode(path, mode);
                }
                // On Windows, executable status is determined by file extension — no action needed.
            }
            catch (RuntimeError) { throw; }
            catch (System.UnauthorizedAccessException e) { throw new RuntimeError($"Cannot set executable on '{path}': {e.Message}"); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot set executable on '{path}': {e.Message}"); }
            return null;
        }, returnType: "null", documentation: "Sets or clears the executable permission on a file. On Unix, toggles the user execute bit (adds on true, clears all execute bits on false). On Windows, this is a no-op since executability is determined by file extension.\n@param path The file path to modify.\n@param executable True to make executable, false to remove execute permission.");

        // ── Built-in structs for file permissions ─────────────────────────────
        ns.Struct("FilePermission", [
            new BuiltInField("read", "bool"),
            new BuiltInField("write", "bool"),
            new BuiltInField("execute", "bool"),
        ]);

        ns.Struct("FilePermissions", [
            new BuiltInField("owner", "FilePermission"),
            new BuiltInField("group", "FilePermission"),
            new BuiltInField("others", "FilePermission"),
        ]);

        return ns.Build();
    }
}
