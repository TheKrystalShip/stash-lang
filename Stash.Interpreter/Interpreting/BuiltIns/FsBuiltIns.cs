namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'fs' namespace built-in functions.
/// </summary>
public static class FsBuiltIns
{
    private static string ExpandTilde(string path) => Interpreter.ExpandTilde(path);

    public static void Register(Environment globals)
    {
        // ── fs namespace ─────────────────────────────────────────────────
        var fs = new StashNamespace("fs");

        fs.Define("readFile", new BuiltInFunction("fs.readFile", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.readFile' must be a string.");
            }
            path = ExpandTilde(path);

            try { return System.IO.File.ReadAllText(path); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot read file '{path}': {e.Message}"); }
        }));

        fs.Define("writeFile", new BuiltInFunction("fs.writeFile", 2, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("First argument to 'fs.writeFile' must be a string.");
            }

            if (args[1] is not string content)
            {
                throw new RuntimeError("Second argument to 'fs.writeFile' must be a string.");
            }
            path = ExpandTilde(path);

            try { System.IO.File.WriteAllText(path, content); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot write file '{path}': {e.Message}"); }
            return null;
        }));

        fs.Define("exists", new BuiltInFunction("fs.exists", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.exists' must be a string.");
            }
            path = ExpandTilde(path);

            return System.IO.File.Exists(path);
        }));

        fs.Define("dirExists", new BuiltInFunction("fs.dirExists", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.dirExists' must be a string.");
            }
            path = ExpandTilde(path);

            return System.IO.Directory.Exists(path);
        }));

        fs.Define("pathExists", new BuiltInFunction("fs.pathExists", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.pathExists' must be a string.");
            }
            path = ExpandTilde(path);

            return System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
        }));

        fs.Define("createDir", new BuiltInFunction("fs.createDir", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.createDir' must be a string.");
            }
            path = ExpandTilde(path);

            try { System.IO.Directory.CreateDirectory(path); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot create directory '{path}': {e.Message}"); }
            return null;
        }));

        fs.Define("delete", new BuiltInFunction("fs.delete", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.delete' must be a string.");
            }
            path = ExpandTilde(path);

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
        }));

        fs.Define("copy", new BuiltInFunction("fs.copy", 2, (_, args) =>
        {
            if (args[0] is not string src)
            {
                throw new RuntimeError("First argument to 'fs.copy' must be a string.");
            }

            if (args[1] is not string dst)
            {
                throw new RuntimeError("Second argument to 'fs.copy' must be a string.");
            }
            src = ExpandTilde(src);
            dst = ExpandTilde(dst);

            try { System.IO.File.Copy(src, dst, overwrite: true); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot copy '{src}' to '{dst}': {e.Message}"); }
            return null;
        }));

        fs.Define("move", new BuiltInFunction("fs.move", 2, (_, args) =>
        {
            if (args[0] is not string src)
            {
                throw new RuntimeError("First argument to 'fs.move' must be a string.");
            }

            if (args[1] is not string dst)
            {
                throw new RuntimeError("Second argument to 'fs.move' must be a string.");
            }
            src = ExpandTilde(src);
            dst = ExpandTilde(dst);

            try { System.IO.File.Move(src, dst, overwrite: true); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot move '{src}' to '{dst}': {e.Message}"); }
            return null;
        }));

        fs.Define("size", new BuiltInFunction("fs.size", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.size' must be a string.");
            }
            path = ExpandTilde(path);

            try { return new System.IO.FileInfo(path).Length; }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot get size of '{path}': {e.Message}"); }
        }));

        fs.Define("listDir", new BuiltInFunction("fs.listDir", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.listDir' must be a string.");
            }
            path = ExpandTilde(path);

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
        }));

        fs.Define("appendFile", new BuiltInFunction("fs.appendFile", 2, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("First argument to 'fs.appendFile' must be a string.");
            }

            if (args[1] is not string content)
            {
                throw new RuntimeError("Second argument to 'fs.appendFile' must be a string.");
            }
            path = ExpandTilde(path);

            try { System.IO.File.AppendAllText(path, content); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot append to file '{path}': {e.Message}"); }
            return null;
        }));

        fs.Define("readLines", new BuiltInFunction("fs.readLines", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.readLines' must be a string.");
            }
            path = ExpandTilde(path);

            try
            {
                var lines = System.IO.File.ReadAllLines(path);
                return new List<object?>(lines);
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot read file '{path}': {e.Message}"); }
        }));

        fs.Define("glob", new BuiltInFunction("fs.glob", 1, (_, args) =>
        {
            if (args[0] is not string pattern)
            {
                throw new RuntimeError("Argument to 'fs.glob' must be a string.");
            }
            pattern = ExpandTilde(pattern);

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
        }));

        fs.Define("isFile", new BuiltInFunction("fs.isFile", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.isFile' must be a string.");
            }
            path = ExpandTilde(path);

            return System.IO.File.Exists(path);
        }));

        fs.Define("isDir", new BuiltInFunction("fs.isDir", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.isDir' must be a string.");
            }
            path = ExpandTilde(path);

            return System.IO.Directory.Exists(path);
        }));

        fs.Define("isSymlink", new BuiltInFunction("fs.isSymlink", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.isSymlink' must be a string.");
            }
            path = ExpandTilde(path);

            try
            {
                var info = new System.IO.FileInfo(path);
                return info.Exists && info.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint);
            }
            catch (System.IO.IOException)
            {
                return false;
            }
        }));

        fs.Define("tempFile", new BuiltInFunction("fs.tempFile", 0, (_, _) =>
        {
            return System.IO.Path.GetTempFileName();
        }));

        fs.Define("tempDir", new BuiltInFunction("fs.tempDir", 0, (_, _) =>
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }));

        fs.Define("modifiedAt", new BuiltInFunction("fs.modifiedAt", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.modifiedAt' must be a string.");
            }
            path = ExpandTilde(path);

            try
            {
                var info = new System.IO.FileInfo(path);
                return (double)new System.DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0;
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot get modified time for '{path}': {e.Message}"); }
        }));

        fs.Define("walk", new BuiltInFunction("fs.walk", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.walk' must be a string.");
            }
            path = ExpandTilde(path);

            try
            {
                var files = System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories);
                return new List<object?>(files);
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"fs.walk failed: {e.Message}"); }
        }));

        fs.Define("readable", new BuiltInFunction("fs.readable", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.readable' must be a string.");
            }
            path = ExpandTilde(path);

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
        }));

        fs.Define("writable", new BuiltInFunction("fs.writable", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.writable' must be a string.");
            }
            path = ExpandTilde(path);

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
        }));

        fs.Define("executable", new BuiltInFunction("fs.executable", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.executable' must be a string.");
            }
            path = ExpandTilde(path);

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
        }));

        fs.Define("createFile", new BuiltInFunction("fs.createFile", 1, (_, args) =>
        {
            if (args[0] is not string path)
                throw new RuntimeError("Argument to 'fs.createFile' must be a string.");
            path = ExpandTilde(path);

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
        }));

        fs.Define("symlink", new BuiltInFunction("fs.symlink", 2, (_, args) =>
        {
            if (args[0] is not string target)
                throw new RuntimeError("First argument to 'fs.symlink' must be a string.");
            if (args[1] is not string linkPath)
                throw new RuntimeError("Second argument to 'fs.symlink' must be a string.");
            target = ExpandTilde(target);
            linkPath = ExpandTilde(linkPath);

            try
            {
                System.IO.File.CreateSymbolicLink(linkPath, target);
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot create symlink '{linkPath}': {e.Message}"); }
            return null;
        }));

        fs.Define("stat", new BuiltInFunction("fs.stat", 1, (_, args) =>
        {
            if (args[0] is not string path)
                throw new RuntimeError("Argument to 'fs.stat' must be a string.");
            path = ExpandTilde(path);

            try
            {
                var info = new System.IO.FileInfo(path);
                if (!info.Exists && !System.IO.Directory.Exists(path))
                    throw new RuntimeError($"Path does not exist: '{path}'.");

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
        }));

        globals.Define("fs", fs);
    }
}
