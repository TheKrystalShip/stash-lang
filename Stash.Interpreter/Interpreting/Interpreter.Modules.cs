using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Interpreting.Types;

namespace Stash.Interpreting;

public partial class Interpreter
{
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

    private string ResolveModulePath(string modulePath, SourceSpan span)
    {
        string basePath;
        if (_currentFile is not null)
        {
            basePath = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_currentFile))!;
        }
        else
        {
            basePath = System.IO.Directory.GetCurrentDirectory();
        }

        string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath, modulePath));

        if (!System.IO.File.Exists(fullPath))
        {
            throw new RuntimeError($"Cannot find module '{modulePath}'.", span);
        }

        return fullPath;
    }

    private Environment LoadModule(string resolvedPath, SourceSpan span)
    {
        if (_moduleCache.TryGetValue(resolvedPath, out Environment? cached))
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
            if (_loadedSources.Add(resolvedPath))
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

            _moduleCache[resolvedPath] = moduleEnv;
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
