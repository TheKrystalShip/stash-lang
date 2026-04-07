using System;
using System.Collections.Generic;
using System.IO;
using Stash.Common;
using Stash.Debugging;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Module import and package path resolution.
/// </summary>
public sealed partial class VirtualMachine
{
    private Dictionary<string, object?> LoadModule(string modulePath, SourceSpan? span)
    {
        // Use the context's current file, or fall back to the span's source file
        // (populated when source is compiled with a real file path, e.g. via RunWithFile).
        string? currentFile = _context.CurrentFile;
        if (currentFile == null && span?.File is { Length: > 0 } src && !src.StartsWith('<'))
        {
            currentFile = src;
        }

        string resolvedPath;

        if (Path.IsPathRooted(modulePath))
        {
            resolvedPath = modulePath;
        }
        else if (currentFile != null)
        {
            string? dir = Path.GetDirectoryName(currentFile);
            resolvedPath = Path.GetFullPath(Path.Combine(dir ?? ".", modulePath));
        }
        else
        {
            resolvedPath = Path.GetFullPath(modulePath);
        }

        // Try package resolution for non-path module specifiers
        if (!File.Exists(resolvedPath) && !File.Exists(resolvedPath + ".stash"))
        {
            string? packagePath = ResolvePackagePath(modulePath, span);
            if (packagePath != null)
            {
                resolvedPath = packagePath;
            }
        }

        // Auto-append .stash extension if missing
        if (!resolvedPath.EndsWith(".stash", StringComparison.OrdinalIgnoreCase))
        {
            resolvedPath += ".stash";
        }

        if (ModuleCache.TryGetValue(resolvedPath, out Dictionary<string, object?>? cached))
        {
            return cached;
        }

        object moduleLock = ModuleLocks.GetOrAdd(resolvedPath, _ => new object());
        lock (moduleLock)
        {
            // Double-check after acquiring lock
            if (ModuleCache.TryGetValue(resolvedPath, out Dictionary<string, object?>? cached2))
            {
                return cached2;
            }

            if (_importStack.Contains(resolvedPath))
            {
                throw new RuntimeError($"Circular import detected: {resolvedPath}", span);
            }

            _importStack.Add(resolvedPath);
            try
            {
                Chunk moduleChunk;
                try
                {
                    if (_moduleLoader != null)
                    {
                        moduleChunk = _moduleLoader(resolvedPath, currentFile);
                    }
                    else
                    {
                        // Built-in file-based loader: compile the module from disk.
                        string moduleSource = File.ReadAllText(resolvedPath);
                        var lex = new Lexer(moduleSource, resolvedPath);
                        var tokens = lex.ScanTokens();
                        if (lex.Errors.Count > 0)
                        {
                            throw new RuntimeError($"Syntax error in module '{resolvedPath}': {lex.Errors[0]}", span);
                        }

                        var par = new Parser(tokens);
                        var stmts = par.ParseProgram();
                        if (par.Errors.Count > 0)
                        {
                            throw new RuntimeError($"Parse error in module '{resolvedPath}': {par.Errors[0]}", span);
                        }

                        SemanticResolver.Resolve(stmts);
                        moduleChunk = Compiler.Compile(stmts);
                    }
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    throw new RuntimeError($"Cannot find module '{modulePath}'.", span);
                }

                var moduleGlobals = new Dictionary<string, object?>(_globals);
                var moduleVM = new VirtualMachine(moduleGlobals, _ct)
                {
                    _moduleLoader = _moduleLoader,
                    ModuleCache = ModuleCache,
                    _importStack = _importStack,
                    ModuleLocks = ModuleLocks,
                };
                moduleVM._context.CurrentFile = resolvedPath;
                moduleVM._context.Output = _context.Output;
                moduleVM._context.ErrorOutput = _context.ErrorOutput;
                moduleVM._context.Input = _context.Input;
                moduleVM.Debugger = _debugger;
                moduleVM._debugThreadId = _debugThreadId;
                moduleVM.EmbeddedMode = EmbeddedMode;
                moduleVM.ScriptArgs = ScriptArgs;
                moduleVM.TestHarness = TestHarness;
                moduleVM.TestFilter = TestFilter;

                // Notify debugger of newly loaded source
                _debugger?.OnSourceLoaded(resolvedPath);

                moduleVM.Execute(moduleChunk);

                ModuleCache[resolvedPath] = moduleVM.Globals;
                return moduleVM.Globals;
            }
            finally
            {
                _importStack.Remove(resolvedPath);
            }
        }
    }

    private string? ResolvePackagePath(string modulePath, SourceSpan? span)
    {
        // Only attempt package resolution for non-path module specifiers
        if (modulePath.StartsWith(".", StringComparison.Ordinal) ||
            modulePath.StartsWith("/", StringComparison.Ordinal) ||
            modulePath.StartsWith("\\", StringComparison.Ordinal) ||
            modulePath.EndsWith(".stash", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Find project root by walking up to find stash.json
        string? startDir = _context.CurrentFile != null
            ? Path.GetDirectoryName(Path.GetFullPath(_context.CurrentFile))
            : null;

        if (startDir == null)
        {
            return null;
        }

        string? projectRoot = null;
        string? dir = startDir;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "stash.json")))
            {
                projectRoot = dir;
                break;
            }
            string? parent = Path.GetDirectoryName(dir);
            if (parent == dir)
            {
                break;
            }

            dir = parent;
        }

        if (projectRoot == null)
        {
            return null;
        }

        string stashesDir = Path.Combine(projectRoot, "stashes");

        // Parse package name and subpath
        string packageName;
        string? subPath = null;

        if (modulePath.StartsWith("@", StringComparison.Ordinal))
        {
            // Scoped package: @scope/name or @scope/name/subpath
            int secondSlash = modulePath.IndexOf('/', modulePath.IndexOf('/') + 1);
            if (secondSlash >= 0)
            {
                packageName = modulePath[..secondSlash];
                subPath = modulePath[(secondSlash + 1)..];
            }
            else
            {
                packageName = modulePath;
            }
        }
        else if (modulePath.Contains('/'))
        {
            // Unscoped with subpath: pkg/lib/helpers
            int firstSlash = modulePath.IndexOf('/');
            packageName = modulePath[..firstSlash];
            subPath = modulePath[(firstSlash + 1)..];
        }
        else
        {
            packageName = modulePath;
        }

        string packageDir = Path.Combine(stashesDir, packageName.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(packageDir))
        {
            throw new RuntimeError(
                $"Package '{packageName}' not found. Run `stash pkg install {packageName}` to install it.",
                span);
        }

        if (subPath != null)
        {
            // Resolve subpath within package
            string subFilePath = Path.Combine(packageDir, subPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(subFilePath))
            {
                return subFilePath;
            }

            if (!subFilePath.EndsWith(".stash", StringComparison.OrdinalIgnoreCase))
            {
                string withExt = subFilePath + ".stash";
                if (File.Exists(withExt))
                {
                    return withExt;
                }
            }
            throw new RuntimeError($"Module '{modulePath}' not found in package '{packageName}'.", span);
        }

        // Resolve entry point: check stash.json for "main" field
        string pkgJsonPath = Path.Combine(packageDir, "stash.json");
        if (File.Exists(pkgJsonPath))
        {
            try
            {
                string json = File.ReadAllText(pkgJsonPath);
                // Simple JSON parsing for "main" field
                int mainIdx = json.IndexOf("\"main\"", StringComparison.Ordinal);
                if (mainIdx >= 0)
                {
                    int colonIdx = json.IndexOf(':', mainIdx);
                    if (colonIdx >= 0)
                    {
                        int quoteStart = json.IndexOf('"', colonIdx + 1);
                        if (quoteStart >= 0)
                        {
                            int quoteEnd = json.IndexOf('"', quoteStart + 1);
                            if (quoteEnd > quoteStart)
                            {
                                string mainFile = json[(quoteStart + 1)..quoteEnd];
                                string mainPath = Path.Combine(packageDir, mainFile.Replace('/', Path.DirectorySeparatorChar));
                                if (File.Exists(mainPath))
                                {
                                    return mainPath;
                                }
                            }
                        }
                    }
                }
            }
            catch { /* ignore parse errors, fall through to default */ }
        }

        // Default entry point: index.stash
        string indexPath = Path.Combine(packageDir, "index.stash");
        if (File.Exists(indexPath))
        {
            return indexPath;
        }

        throw new RuntimeError($"Package '{packageName}' has no entry point (no index.stash or main field).", span);
    }

    private void ExecuteImport(ref CallFrame frame)
    {
        ushort metaImportIdx = ReadU16(ref frame);
        SourceSpan? span = GetCurrentSpan(ref frame);
        var importMeta = (ImportMetadata)frame.Chunk.Constants[metaImportIdx].AsObj!;

        StashValue pathVal = Pop();
        string modulePath = pathVal.AsObj is string mp
            ? mp
            : throw new RuntimeError("Module path must be a string.", span);

        Dictionary<string, object?> moduleEnv = LoadModule(modulePath, span);

        foreach (string importName in importMeta.Names)
        {
            if (moduleEnv.TryGetValue(importName, out object? importedValue))
            {
                Push(StashValue.FromObject(importedValue));
            }
            else
            {
                throw new RuntimeError($"Module does not export '{importName}'.", span);
            }
        }
    }

    private void ExecuteImportAs(ref CallFrame frame)
    {
        ushort metaImportAsIdx = ReadU16(ref frame);
        SourceSpan? span = GetCurrentSpan(ref frame);
        var importAsMeta = (ImportAsMetadata)frame.Chunk.Constants[metaImportAsIdx].AsObj!;

        StashValue pathVal = Pop();
        string modulePath = pathVal.AsObj is string mp
            ? mp
            : throw new RuntimeError("Module path must be a string.", span);

        Dictionary<string, object?> moduleEnv = LoadModule(modulePath, span);

        var ns = new StashNamespace(importAsMeta.AliasName);
        foreach (KeyValuePair<string, object?> kvp in moduleEnv)
        {
            if (kvp.Value is StashNamespace sn && sn.IsBuiltIn)
            {
                continue; // skip inherited built-in namespaces
            }

            ns.Define(kvp.Key, kvp.Value);
        }
        ns.Freeze();
        Push(StashValue.FromObj(ns));
    }
}
