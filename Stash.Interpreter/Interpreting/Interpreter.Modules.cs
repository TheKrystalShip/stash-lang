using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Interpreting.Types;

namespace Stash.Interpreting;

public partial class Interpreter
{
    /// <inheritdoc />
    public object? VisitImportStmt(ImportStmt stmt)
    {
        string modulePath = (string)stmt.Path.Literal!;
        string resolvedPath = ResolveModulePath(modulePath, stmt.Path.Span);

        // Check for circular imports
        if (_importStack.Contains(resolvedPath))
        {
            throw new RuntimeError(
                $"Circular import detected: '{modulePath}' is already being imported.",
                stmt.Span);
        }

        // Get or load the module
        Environment moduleEnv = LoadModule(resolvedPath, stmt.Path.Span);

        // Bind imported names into the current scope
        foreach (Token name in stmt.Names)
        {
            object? value = moduleEnv.Get(name.Lexeme, name.Span);
            _environment.Define(name.Lexeme, value);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitImportAsStmt(ImportAsStmt stmt)
    {
        string modulePath = (string)stmt.Path.Literal!;
        string resolvedPath = ResolveModulePath(modulePath, stmt.Path.Span);

        // Check for circular imports
        if (_importStack.Contains(resolvedPath))
        {
            throw new RuntimeError(
                $"Circular import detected: '{modulePath}' is already being imported.",
                stmt.Span);
        }

        // Get or load the module
        Environment moduleEnv = LoadModule(resolvedPath, stmt.Path.Span);

        // Wrap all module-level bindings in a namespace
        var ns = new StashNamespace(stmt.Alias.Lexeme);
        foreach (var (name, value) in moduleEnv.GetAllBindings())
        {
            ns.Define(name, value);
        }

        ns.Freeze();
        _environment.Define(stmt.Alias.Lexeme, ns);
        return null;
    }

    /// <summary>Resolves a module specifier to an absolute path. Bare specifiers (package imports) are resolved via <see cref="ModuleResolver.ResolvePackageImport"/>; relative/absolute specifiers are resolved against the importing file's directory with auto-.stash-extension and index.stash fallback.</summary>
    /// <param name="modulePath">The module path string as written in the import statement.</param>
    /// <param name="span">The source span of the path token, used for error reporting.</param>
    /// <returns>The resolved absolute file path.</returns>
    private string ResolveModulePath(string modulePath, SourceSpan span)
    {
        string importingFileDir = _currentFile is not null
            ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_currentFile))!
            : System.IO.Directory.GetCurrentDirectory();

        // Try relative/absolute file resolution first (works for both relative paths
        // and legacy bare filenames like "math.stash" that resolve relative to CWD).
        string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(importingFileDir, modulePath));
        string? resolvedFilePath = ModuleResolver.ResolveFilePath(fullPath);
        if (resolvedFilePath is not null)
        {
            return resolvedFilePath;
        }

        // If that didn't work and it's a bare specifier, try package resolution.
        if (ModuleResolver.IsBareSpecifier(modulePath))
        {
            string? resolved = ModuleResolver.ResolvePackageImport(modulePath, importingFileDir);
            if (resolved is not null)
            {
                return resolved;
            }

            var (packageName, _) = ModuleResolver.ParsePackageSpecifier(modulePath);
            throw new RuntimeError($"Package '{packageName}' not found. Run: stash pkg install", span);
        }

        throw new RuntimeError($"Cannot find module '{modulePath}'.", span);
    }

    /// <summary>Loads and executes a module file, returning its top-level environment. Results are cached in a shared, thread-safe cache to prevent re-execution on repeated imports — even across parallel tasks.</summary>
    /// <param name="resolvedPath">The absolute file path of the module to load.</param>
    /// <param name="span">The source span of the import statement, used for error reporting.</param>
    /// <returns>The top-level <see cref="Environment"/> of the executed module.</returns>
    private Environment LoadModule(string resolvedPath, SourceSpan span)
    {
        // Fast path: module already loaded (by this interpreter or another fork)
        if (_sharedModuleCache.TryGetValue(resolvedPath, out Environment? cached))
        {
            return cached;
        }

        // Serialize first-time loading per module path to prevent double-execution
        var moduleLock = _moduleLocks.GetOrAdd(resolvedPath, static _ => new object());
        lock (moduleLock)
        {
            // Double-check after acquiring lock
            if (_sharedModuleCache.TryGetValue(resolvedPath, out cached))
            {
                return cached;
            }

            string source;
            try
            {
                source = System.IO.File.ReadAllText(resolvedPath);
            }
            catch (System.IO.IOException e)
            {
                throw new RuntimeError($"Cannot read module '{resolvedPath}': {e.Message}", span);
            }

            // Lex
            var lexer = new Lexer(source, resolvedPath);
            var tokens = lexer.ScanTokens();
            if (lexer.Errors.Count > 0)
            {
                throw new RuntimeError($"Lex errors in module '{resolvedPath}': {lexer.Errors[0]}", span);
            }

            // Parse
            var parser = new Parser(tokens);
            var statements = parser.ParseProgram();
            if (parser.Errors.Count > 0)
            {
                throw new RuntimeError($"Parse errors in module '{resolvedPath}': {parser.Errors[0]}", span);
            }

            // Execute in isolated environment
            var moduleEnv = new Environment(_globals);

            // Save current state
            Environment previousEnv = _environment;
            string? previousFile = _currentFile;

            try
            {
                _importStack.Add(resolvedPath);
                _currentFile = resolvedPath;
                _environment = moduleEnv;

                // Track loaded source for DAP
                if (_loadedSources.TryAdd(resolvedPath, 0))
                {
                    _debugger?.OnSourceLoaded(resolvedPath);
                }

                // Resolve variable references for O(1) lookup
                var resolver = new Resolver(this);
                resolver.Resolve(statements);

                // Execute the module
                foreach (Stmt statement in statements)
                {
                    Execute(statement);
                }

                _sharedModuleCache[resolvedPath] = moduleEnv;
                return moduleEnv;
            }
            finally
            {
                _environment = previousEnv;
                _currentFile = previousFile;
                _importStack.Remove(resolvedPath);
            }
        }
    }
}
