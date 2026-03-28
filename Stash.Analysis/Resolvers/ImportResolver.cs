namespace Stash.Analysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Common;
using Stash.Parsing.AST;

/// <summary>
/// Resolves import statements to their target files, parses imported files,
/// and provides exported symbols for cross-file analysis.
/// </summary>
/// <remarks>
/// <para>
/// Two import forms are handled:
/// </para>
/// <list type="bullet">
///   <item><description><c>import { name1, name2 } from "path.stash"</c> — selective import, resolved via <see cref="ResolveSelectiveImport"/>.</description></item>
///   <item><description><c>import "path.stash" as alias</c> — namespace import, resolved via <see cref="ResolveNamespaceImport"/>.</description></item>
/// </list>
/// <para>
/// Parsed module symbols are cached in a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>
/// keyed by absolute file path. Use <see cref="InvalidateCache"/> to evict a file when it changes.
/// </para>
/// <para>
/// Reverse dependency tracking (file → importers) is maintained by <see cref="TrackDependency"/>
/// and exposed via <see cref="GetDependents"/>. <see cref="AnalysisEngine"/> uses this to
/// re-analyse all files that depend on a changed module.
/// </para>
/// <para>
/// Circular imports are detected via a <c>_loadingModules</c> guard set; a circular dependency
/// returns an empty module to break the cycle without throwing.
/// </para>
/// </remarks>
public class ImportResolver
{
    // Cache of parsed module symbols, keyed by absolute file path
    private readonly ConcurrentDictionary<string, ModuleInfo> _moduleCache = new();

    /// <summary>Guard set preventing infinite recursion on circular imports.</summary>
    private readonly HashSet<string> _loadingModules = new();
    // Map from imported file path → set of document URIs that import it
    private readonly ConcurrentDictionary<string, HashSet<Uri>> _dependents = new();

    /// <summary>
    /// Delegate signature for a function that parses a Stash source file at the given absolute
    /// path and returns its exported <see cref="ModuleInfo"/>. Provided by <see cref="AnalysisEngine"/>
    /// so the resolver does not need to depend on the engine directly.
    /// </summary>
    /// <param name="absolutePath">Absolute path to the Stash file to parse.</param>
    /// <returns>The parsed module information.</returns>
    public delegate ModuleInfo ModuleParser(string absolutePath);

    /// <summary>
    /// Represents the exported symbols from a parsed module file.
    /// </summary>
    /// <remarks>
    /// Instances are created by the <see cref="ModuleParser"/> delegate and stored in the module
    /// cache. They are also stored in <see cref="ImportResolution.NamespaceImports"/> for
    /// namespace-import aliases to enable dot-completion on the alias.
    /// </remarks>
    public class ModuleInfo
    {
        /// <summary>Gets the URI of the module file.</summary>
        public Uri Uri { get; }

        /// <summary>Gets the absolute file-system path of the module.</summary>
        public string AbsolutePath { get; }

        /// <summary>Gets the scope tree containing all top-level symbols exported by the module.</summary>
        public ScopeTree Symbols { get; }

        /// <summary>Gets any parse errors encountered when loading the module.</summary>
        public List<DiagnosticError> Errors { get; }

        /// <summary>
        /// Initializes a new <see cref="ModuleInfo"/> with the given URI, path, symbol tree, and errors.
        /// </summary>
        /// <param name="uri">The URI of the module file.</param>
        /// <param name="absolutePath">The absolute file-system path of the module.</param>
        /// <param name="symbols">The parsed symbol tree for the module.</param>
        /// <param name="errors">Any parse errors encountered.</param>
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
    /// <param name="parseModule">Delegate used to parse a module file.</param>
    /// <returns>Import resolution results.</returns>
    public ImportResolution ResolveImports(Uri documentUri, List<Stmt> statements, ModuleParser parseModule)
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
                    ResolveSelectiveImport(importStmt, documentDir, resolution, parseModule, documentUri);
                    break;
                case ImportAsStmt importAsStmt:
                    ResolveNamespaceImport(importAsStmt, documentDir, resolution, parseModule, documentUri);
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
    private void ResolveSelectiveImport(ImportStmt stmt, string documentDir, ImportResolution resolution, ModuleParser parseModule, Uri documentUri)
    {
        var importPath = stmt.Path.Literal as string;
        if (string.IsNullOrEmpty(importPath))
        {
            return;
        }

        var absolutePath = ResolveImportToAbsolutePath(importPath, documentDir, resolution, stmt.Path.Span);
        if (absolutePath == null)
        {
            return;
        }

        TrackDependency(absolutePath, documentUri);
        var moduleInfo = LoadModule(absolutePath, parseModule);

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

            // Also add child symbols (struct fields, enum members, interface members) for dot-completion
            if (exportedSymbol.Kind == SymbolKind.Struct || exportedSymbol.Kind == SymbolKind.Enum || exportedSymbol.Kind == SymbolKind.Interface)
            {
                var allChildren = moduleInfo.Symbols.All
                    .Where(s => s.ParentName == nameToken.Lexeme && (s.Kind == SymbolKind.Field || s.Kind == SymbolKind.EnumMember || s.Kind == SymbolKind.Method));

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
    private void ResolveNamespaceImport(ImportAsStmt stmt, string documentDir, ImportResolution resolution, ModuleParser parseModule, Uri documentUri)
    {
        var importPath = stmt.Path.Literal as string;
        if (string.IsNullOrEmpty(importPath))
        {
            return;
        }

        var absolutePath = ResolveImportToAbsolutePath(importPath, documentDir, resolution, stmt.Path.Span);
        if (absolutePath == null)
        {
            return;
        }

        TrackDependency(absolutePath, documentUri);
        var moduleInfo = LoadModule(absolutePath, parseModule);
        resolution.NamespaceImports[stmt.Alias.Lexeme] = moduleInfo;
    }

    /// <summary>
    /// Resolves an import path to an absolute file path, handling both relative files and bare specifiers.
    /// Returns the absolute path on success, or null if resolution failed (diagnostic already added).
    /// </summary>
    private static string? ResolveImportToAbsolutePath(string importPath, string documentDir, ImportResolution resolution, SourceSpan span)
    {
        if (ModuleResolver.IsBareSpecifier(importPath))
        {
            // Try relative file first (handles "module.stash" without ./ prefix)
            string relativePath = Path.GetFullPath(importPath, documentDir);
            if (File.Exists(relativePath))
            {
                return relativePath;
            }

            if (Path.HasExtension(importPath))
            {
                // Has file extension — treat as missing file, not missing package
                resolution.Diagnostics.Add(new SemanticDiagnostic(
                    $"Cannot find module '{importPath}'.",
                    DiagnosticLevel.Error,
                    span));
                return null;
            }

            string? packagePath = ModuleResolver.ResolvePackageImport(importPath, documentDir);
            if (packagePath == null)
            {
                resolution.Diagnostics.Add(new SemanticDiagnostic(
                    $"Package '{importPath}' not found. Run: stash pkg install",
                    DiagnosticLevel.Warning,
                    span));
                return null;
            }

            return packagePath;
        }

        string absolutePath = Path.GetFullPath(importPath, documentDir);
        if (!File.Exists(absolutePath))
        {
            resolution.Diagnostics.Add(new SemanticDiagnostic(
                $"Cannot find module '{importPath}'.",
                DiagnosticLevel.Error,
                span));
            return null;
        }

        return absolutePath;
    }

    /// <summary>
    /// Tracks an import dependency: records that <paramref name="importerUri"/> imports the file
    /// at <paramref name="importedPath"/>. Thread-safe via per-set locking.
    /// </summary>
    /// <param name="importedPath">The absolute path of the imported file.</param>
    /// <param name="importerUri">The URI of the file that contains the import statement.</param>
    private void TrackDependency(string importedPath, Uri importerUri)
    {
        var dependents = _dependents.GetOrAdd(importedPath, _ => new HashSet<Uri>());
        lock (dependents)
        {
            dependents.Add(importerUri);
        }
    }

    /// <summary>
    /// Returns all document URIs that import the given file path.
    /// </summary>
    public IReadOnlyCollection<Uri> GetDependents(string absolutePath)
    {
        if (_dependents.TryGetValue(absolutePath, out var dependents))
        {
            lock (dependents)
            {
                return dependents.ToArray();
            }
        }
        return Array.Empty<Uri>();
    }

    /// <summary>
    /// Loads and parses a module file, caching the result.
    /// </summary>
    private ModuleInfo LoadModule(string absolutePath, ModuleParser parseModule)
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

        try
        {
            var moduleInfo = parseModule(absolutePath);
            _moduleCache[absolutePath] = moduleInfo;
            return moduleInfo;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var uri = new Uri(absolutePath);
            var emptyScope = new Scope(ScopeKind.Global, null, new SourceSpan(absolutePath, 1, 1, 1, 1));
            var emptyTree = new ScopeTree(emptyScope);
            var moduleInfo = new ModuleInfo(uri, absolutePath, emptyTree, new List<DiagnosticError>());
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
