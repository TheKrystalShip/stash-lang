namespace Stash.Lsp.Analysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;

/// <summary>
/// Resolves import statements to their target files, parses imported files,
/// and provides exported symbols for cross-file analysis.
/// </summary>
public class ImportResolver
{
    // Cache of parsed module symbols, keyed by absolute file path
    private readonly ConcurrentDictionary<string, ModuleInfo> _moduleCache = new();
    private readonly HashSet<string> _loadingModules = new();

    /// <summary>
    /// Represents the exported symbols from a parsed module file.
    /// </summary>
    public class ModuleInfo
    {
        public Uri Uri { get; }
        public string AbsolutePath { get; }
        public ScopeTree Symbols { get; }
        public List<DiagnosticError> Errors { get; }

        public ModuleInfo(Uri uri, string absolutePath, ScopeTree symbols, List<DiagnosticError> errors)
        {
            Uri = uri;
            AbsolutePath = absolutePath;
            Symbols = symbols;
            Errors = errors;
        }
    }

    /// <summary>
    /// Result of resolving all imports in a file.
    /// </summary>
    public class ImportResolution
    {
        /// <summary>
        /// Symbols to inject into the importing file's global scope.
        /// These are the resolved versions of imported names.
        /// </summary>
        public List<SymbolInfo> ResolvedSymbols { get; } = new();

        /// <summary>
        /// Diagnostics generated during import resolution (missing files, missing names).
        /// </summary>
        public List<SemanticDiagnostic> Diagnostics { get; } = new();

        /// <summary>
        /// Map from imported namespace alias to its module info (for import-as).
        /// </summary>
        public Dictionary<string, ModuleInfo> NamespaceImports { get; } = new();
    }

    /// <summary>
    /// Resolves all imports in a file's AST. Returns resolved symbols and diagnostics.
    /// </summary>
    /// <param name="documentUri">URI of the document being analyzed.</param>
    /// <param name="statements">The parsed AST statements of the document.</param>
    /// <returns>Import resolution results.</returns>
    public ImportResolution ResolveImports(Uri documentUri, List<Stmt> statements)
    {
        var resolution = new ImportResolution();
        var documentDir = documentUri.IsFile ? Path.GetDirectoryName(documentUri.LocalPath) : null;

        if (documentDir == null)
        {
            return resolution;
        }

        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case ImportStmt importStmt:
                    ResolveSelectiveImport(importStmt, documentDir, resolution);
                    break;
                case ImportAsStmt importAsStmt:
                    ResolveNamespaceImport(importAsStmt, documentDir, resolution);
                    break;
            }
        }

        return resolution;
    }

    /// <summary>
    /// Gets the cached module info for an absolute file path, or null.
    /// </summary>
    public ModuleInfo? GetModule(string absolutePath)
    {
        return _moduleCache.TryGetValue(absolutePath, out var info) ? info : null;
    }

    /// <summary>
    /// Invalidates the cache for a specific file path. Called when a file changes.
    /// </summary>
    public void InvalidateCache(string absolutePath)
    {
        _moduleCache.TryRemove(absolutePath, out _);
    }

    /// <summary>
    /// Resolves a selective import: import { name1, name2 } from "path.stash";
    /// </summary>
    private void ResolveSelectiveImport(ImportStmt stmt, string documentDir, ImportResolution resolution)
    {
        var importPath = stmt.Path.Literal as string;
        if (string.IsNullOrEmpty(importPath))
        {
            return;
        }

        var absolutePath = Path.GetFullPath(importPath, documentDir);

        if (!File.Exists(absolutePath))
        {
            resolution.Diagnostics.Add(new SemanticDiagnostic(
                $"Cannot find module '{importPath}'.",
                DiagnosticLevel.Error,
                stmt.Path.Span));
            return;
        }

        var moduleInfo = LoadModule(absolutePath);

        // For each imported name, check if it exists in the module's top-level exports
        foreach (var nameToken in stmt.Names)
        {
            var exportedSymbol = moduleInfo.Symbols.GetTopLevel()
                .FirstOrDefault(s => s.Name == nameToken.Lexeme);

            if (exportedSymbol == null)
            {
                resolution.Diagnostics.Add(new SemanticDiagnostic(
                    $"Module '{importPath}' does not export '{nameToken.Lexeme}'.",
                    DiagnosticLevel.Error,
                    nameToken.Span));
                continue;
            }

            // Create a resolved symbol that points back to the original definition
            var resolvedSymbol = new SymbolInfo(
                nameToken.Lexeme,
                exportedSymbol.Kind,
                nameToken.Span,
                exportedSymbol.FullSpan,
                exportedSymbol.Detail,
                exportedSymbol.ParentName,
                exportedSymbol.TypeHint,
                moduleInfo.Uri);

            resolution.ResolvedSymbols.Add(resolvedSymbol);

            // Also add child symbols (struct fields, enum members) for dot-completion
            if (exportedSymbol.Kind == SymbolKind.Struct || exportedSymbol.Kind == SymbolKind.Enum)
            {
                var allChildren = moduleInfo.Symbols.All
                    .Where(s => s.ParentName == nameToken.Lexeme && (s.Kind == SymbolKind.Field || s.Kind == SymbolKind.EnumMember));

                foreach (var child in allChildren)
                {
                    resolution.ResolvedSymbols.Add(new SymbolInfo(
                        child.Name,
                        child.Kind,
                        child.Span,
                        child.FullSpan,
                        child.Detail,
                        child.ParentName,
                        child.TypeHint,
                        moduleInfo.Uri));
                }
            }
        }
    }

    /// <summary>
    /// Resolves a namespace import: import "path.stash" as alias;
    /// </summary>
    private void ResolveNamespaceImport(ImportAsStmt stmt, string documentDir, ImportResolution resolution)
    {
        var importPath = stmt.Path.Literal as string;
        if (string.IsNullOrEmpty(importPath))
        {
            return;
        }

        var absolutePath = Path.GetFullPath(importPath, documentDir);

        if (!File.Exists(absolutePath))
        {
            resolution.Diagnostics.Add(new SemanticDiagnostic(
                $"Cannot find module '{importPath}'.",
                DiagnosticLevel.Error,
                stmt.Path.Span));
            return;
        }

        var moduleInfo = LoadModule(absolutePath);
        resolution.NamespaceImports[stmt.Alias.Lexeme] = moduleInfo;
    }

    /// <summary>
    /// Loads and parses a module file, caching the result.
    /// </summary>
    private ModuleInfo LoadModule(string absolutePath)
    {
        if (_moduleCache.TryGetValue(absolutePath, out var cached))
        {
            return cached;
        }

        // Guard against circular imports
        if (!_loadingModules.Add(absolutePath))
        {
            var emptyScope = new Scope(ScopeKind.Global, null, new SourceSpan(absolutePath, 1, 1, 1, 1));
            return new ModuleInfo(new Uri(absolutePath), absolutePath, new ScopeTree(emptyScope), new List<DiagnosticError>());
        }

        var uri = new Uri(absolutePath);
        var errors = new List<DiagnosticError>();

        try
        {
            var source = File.ReadAllText(absolutePath);

            var lexer = new Lexer(source, absolutePath);
            var tokens = lexer.ScanTokens();

            var parser = new Parser(tokens);
            var statements = parser.ParseProgram();

            var collector = new SymbolCollector { IncludeBuiltIns = false };
            var scopeTree = collector.Collect(statements);

            errors.AddRange(lexer.StructuredErrors);
            errors.AddRange(parser.StructuredErrors);

            var moduleInfo = new ModuleInfo(uri, absolutePath, scopeTree, errors);
            _moduleCache[absolutePath] = moduleInfo;
            return moduleInfo;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Return empty module info for files that can't be read
            var emptyScope = new Scope(ScopeKind.Global, null, new SourceSpan(absolutePath, 1, 1, 1, 1));
            var emptyTree = new ScopeTree(emptyScope);
            var moduleInfo = new ModuleInfo(uri, absolutePath, emptyTree, errors);
            _moduleCache[absolutePath] = moduleInfo;
            return moduleInfo;
        }
        finally
        {
            _loadingModules.Remove(absolutePath);
        }
    }

    /// <summary>
    /// Resolves a relative import path to an absolute path.
    /// Returns null if the path cannot be resolved or the file doesn't exist.
    /// </summary>
    public static string? ResolveImportPath(string importPath, string documentDir)
    {
        if (string.IsNullOrEmpty(importPath) || string.IsNullOrEmpty(documentDir))
        {
            return null;
        }

        var absolutePath = Path.GetFullPath(importPath, documentDir);
        return File.Exists(absolutePath) ? absolutePath : null;
    }
}
