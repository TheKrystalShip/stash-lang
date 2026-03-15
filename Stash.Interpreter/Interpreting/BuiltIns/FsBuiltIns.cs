namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'fs' namespace built-in functions.
/// </summary>
public static class FsBuiltIns
{
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

            return System.IO.File.Exists(path);
        }));

        fs.Define("dirExists", new BuiltInFunction("fs.dirExists", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.dirExists' must be a string.");
            }

            return System.IO.Directory.Exists(path);
        }));

        fs.Define("pathExists", new BuiltInFunction("fs.pathExists", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.pathExists' must be a string.");
            }

            return System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
        }));

        fs.Define("createDir", new BuiltInFunction("fs.createDir", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.createDir' must be a string.");
            }

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

            try { return new System.IO.FileInfo(path).Length; }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot get size of '{path}': {e.Message}"); }
        }));

        fs.Define("listDir", new BuiltInFunction("fs.listDir", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.listDir' must be a string.");
            }

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

            return System.IO.File.Exists(path);
        }));

        fs.Define("isDir", new BuiltInFunction("fs.isDir", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.isDir' must be a string.");
            }

            return System.IO.Directory.Exists(path);
        }));

        fs.Define("isSymlink", new BuiltInFunction("fs.isSymlink", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.isSymlink' must be a string.");
            }

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

            try
            {
                var files = System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories);
                return new List<object?>(files);
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"fs.walk failed: {e.Message}"); }
        }));

        globals.Define("fs", fs);
    }
}
