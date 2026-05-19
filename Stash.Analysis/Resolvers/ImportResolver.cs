namespace Stash.Analysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Stash.Common;
using Stash.Core.Resolution;
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

    /// <summary>
    /// Guard set preventing infinite recursion on circular imports.
    /// All mutations and membership checks MUST be performed inside <c>lock (_loadingModules)</c>
    /// so that concurrent LSP requests cannot race on the Add/Remove pair.
    /// <see cref="LoadModule"/> and the <see cref="AnalysisEngine.EnsureModuleLoaded"/> path are
    /// safe for concurrent calls after this guard is in place.
    /// </summary>
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
        /// Gets the export set for the module.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For modules compiled from source this is always non-null.
        /// Only names present in <see cref="Stash.Core.Resolution.ModuleExports.Names"/>
        /// are visible to importers; an empty <see cref="Stash.Core.Resolution.ModuleExports.Names"/>
        /// means the module exports nothing (e.g. a file with zero <c>export</c> annotations).
        /// </para>
        /// <para>
        /// <see langword="null"/> is reserved for v3 on-disk <c>.stashc</c> chunks that pre-date
        /// the export-set feature; the VM exposes their full globals as a legacy fallback.
        /// </para>
        /// </remarks>
        public Stash.Core.Resolution.ModuleExports? Exports { get; }

        /// <summary>
        /// Gets the per-name export entries, including <see cref="ExportEntry.OriginPath"/> for
        /// re-exported names. Used by <see cref="ImportResolver"/> for cycle detection and
        /// SA0825 source-export checking. <see langword="null"/> when the module has no explicit exports.
        /// </summary>
        internal IReadOnlyDictionary<string, ExportEntry>? ExportEntries { get; }

        /// <summary>
        /// Gets the list of static re-export target paths declared by this module.
        /// Each entry is the resolved absolute path of a source module referenced in
        /// <c>export { … } from "p";</c> or <c>export "p" as x;</c> statements.
        /// Used to build the re-export graph for SA0826 cycle detection.
        /// Contains only paths that were statically resolvable (dynamic paths are skipped).
        /// </summary>
        internal IReadOnlyList<string> ReExportTargets { get; }

        /// <summary>
        /// Initializes a new <see cref="ModuleInfo"/> with the given URI, path, symbol tree, errors,
        /// and optional explicit export set.
        /// </summary>
        /// <param name="uri">The URI of the module file.</param>
        /// <param name="absolutePath">The absolute file-system path of the module.</param>
        /// <param name="symbols">The parsed symbol tree for the module.</param>
        /// <param name="errors">Any parse errors encountered.</param>
        /// <param name="exports">
        /// The explicit export set, or <see langword="null"/> for legacy modules that export everything.
        /// </param>
        public ModuleInfo(Uri uri, string absolutePath, ScopeTree symbols, List<DiagnosticError> errors,
            Stash.Core.Resolution.ModuleExports? exports = null)
        {
            Uri = uri;
            AbsolutePath = absolutePath;
            Symbols = symbols;
            Errors = errors;
            Exports = exports;
            ExportEntries = null;
            ReExportTargets = Array.Empty<string>();
        }

        /// <summary>
        /// Initializes a new <see cref="ModuleInfo"/> with full re-export metadata.
        /// Called from <see cref="ImportResolver"/> after resolving re-export paths.
        /// </summary>
        internal ModuleInfo(Uri uri, string absolutePath, ScopeTree symbols, List<DiagnosticError> errors,
            Stash.Core.Resolution.ModuleExports? exports,
            IReadOnlyDictionary<string, ExportEntry>? exportEntries,
            IReadOnlyList<string>? reExportTargets)
        {
            Uri = uri;
            AbsolutePath = absolutePath;
            Symbols = symbols;
            Errors = errors;
            Exports = exports;
            ExportEntries = exportEntries;
            ReExportTargets = reExportTargets ?? Array.Empty<string>();
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

        /// <summary>
        /// Set of (importPath, name) pairs used by re-export statements in this module.
        /// Used by SA0827 detection: if the same (importPath, name) pair appears in both
        /// an ImportStmt and an ExportBlockStmt.Names entry, that is a redundant pair.
        /// Key: the resolved absolute path of the source module; Value: set of names re-exported from it.
        /// </summary>
        internal Dictionary<string, HashSet<string>> ReExportedNamesByPath { get; } = new();
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
        if (!string.IsNullOrEmpty(documentDir) && !Path.IsPathFullyQualified(documentDir))
        {
            documentDir = Path.GetFullPath(documentDir);
        }

        if (documentDir == null)
        {
            return resolution;
        }

        // First pass: collect import sources so we can detect SA0827 redundant pairs.
        // Key: resolved absolute path; Value: set of selectively-imported names from that path.
        var importedNamesByPath = new Dictionary<string, HashSet<string>>();
        foreach (var stmt in statements)
        {
            if (stmt is ImportStmt importStmt)
            {
                var importPath = importStmt.StaticPathValue;
                if (!string.IsNullOrEmpty(importPath))
                {
                    var absPath = ResolveImportToAbsolutePathSilent(importPath, documentDir);
                    if (absPath != null)
                    {
                        if (!importedNamesByPath.TryGetValue(absPath, out var names))
                        {
                            names = new HashSet<string>();
                            importedNamesByPath[absPath] = names;
                        }
                        foreach (var nameToken in importStmt.Names)
                        {
                            names.Add(nameToken.Lexeme);
                        }
                    }
                }
            }
        }

        // Build the re-export adjacency graph (absolute path → set of re-export target absolute paths)
        // for SA0826 cycle detection. Edges are only from re-export statements, not plain imports.
        // We build this across the cached modules as well, so we must record edges when we process
        // re-export statements below.
        var reExportGraph = new Dictionary<string, HashSet<string>>();
        var documentAbsPath = documentUri.IsFile ? documentUri.LocalPath : null;

        if (documentAbsPath != null)
        {
            reExportGraph[documentAbsPath] = new HashSet<string>();
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
                case ExportFromStmt exportFromStmt:
                    ResolveExportFrom(exportFromStmt, documentDir, resolution, parseModule, documentUri,
                        importedNamesByPath, reExportGraph, documentAbsPath);
                    break;
                case ExportModuleAsStmt exportModuleAsStmt:
                    ResolveExportModuleAs(exportModuleAsStmt, documentDir, resolution, parseModule, documentUri,
                        reExportGraph, documentAbsPath);
                    break;
            }
        }

        // SA0826: check for cycles in the re-export subgraph using three-color DFS.
        if (documentAbsPath != null && reExportGraph.ContainsKey(documentAbsPath))
        {
            DetectReExportCycles(reExportGraph, documentAbsPath, resolution, statements, documentDir);
        }

        // SA0827: emit information-level hint for redundant import+export pairs.
        // Pattern: `import { x } from "p"; export { x } from "p";` — the explicit
        // re-export form makes the separate ImportStmt redundant.
        foreach (var (absPath, reExportedNames) in resolution.ReExportedNamesByPath)
        {
            if (!importedNamesByPath.TryGetValue(absPath, out var importedNames))
            {
                continue;
            }

            foreach (var name in reExportedNames)
            {
                if (!importedNames.Contains(name))
                {
                    continue;
                }

                // Find the ExportFromStmt name token span for the diagnostic
                var span = FindExportFromNameSpan(statements, absPath, documentDir, name);
                if (span.HasValue)
                {
                    // Reconstruct the relative path string for the message
                    var relPath = FindExportFromPathString(statements, name);
                    resolution.Diagnostics.Add(
                        DiagnosticDescriptors.SA0827.CreateDiagnostic(span.Value, name, relPath ?? absPath));
                }
            }
        }

        return resolution;
    }

    /// <summary>
    /// Resolves a path string to an absolute path without emitting diagnostics.
    /// Returns null if the path cannot be resolved or file does not exist.
    /// </summary>
    private static string? ResolveImportToAbsolutePathSilent(string importPath, string documentDir)
    {
        if (ModuleResolver.IsBareSpecifier(importPath))
        {
            string relativePath = Path.GetFullPath(importPath, documentDir);
            if (File.Exists(relativePath))
            {
                return relativePath;
            }
            if (Path.HasExtension(importPath) && !importPath.StartsWith('@'))
            {
                return null;
            }
            return ModuleResolver.ResolvePackageImport(importPath, documentDir);
        }

        string absolutePath = Path.GetFullPath(importPath, documentDir);
        return File.Exists(absolutePath) ? absolutePath : null;
    }

    /// <summary>
    /// Resolves a selective re-export: export { name1, name2 } from "path.stash";
    /// </summary>
    private void ResolveExportFrom(ExportFromStmt stmt, string documentDir, ImportResolution resolution,
        ModuleParser parseModule, Uri documentUri,
        Dictionary<string, HashSet<string>> importedNamesByPath,
        Dictionary<string, HashSet<string>> reExportGraph, string? documentAbsPath)
    {
        var exportPath = (stmt.Path as LiteralExpr)?.Value as string;
        if (string.IsNullOrEmpty(exportPath))
        {
            if (stmt.Path is not LiteralExpr { Value: string })
            {
                // Dynamic path — cannot resolve statically; emit SA0801-style hint
                resolution.Diagnostics.Add(DiagnosticDescriptors.SA0801.CreateDiagnostic(stmt.Path.Span));
            }
            return;
        }

        var absolutePath = ResolveImportToAbsolutePath(exportPath, documentDir, resolution, stmt.Path.Span);
        if (absolutePath == null)
        {
            return;
        }

        TrackDependency(absolutePath, documentUri);
        var moduleInfo = LoadModule(absolutePath, parseModule);

        // Record re-export edge for cycle detection
        if (documentAbsPath != null)
        {
            if (!reExportGraph.TryGetValue(documentAbsPath, out var edges))
            {
                edges = new HashSet<string>();
                reExportGraph[documentAbsPath] = edges;
            }
            edges.Add(absolutePath);

            // Also seed the target module's edges from its cached ReExportTargets
            if (!reExportGraph.ContainsKey(absolutePath))
            {
                reExportGraph[absolutePath] = new HashSet<string>(moduleInfo.ReExportTargets);
            }
        }

        foreach (var nameToken in stmt.Names)
        {
            var lexeme = nameToken.Lexeme;

            // SA0825: check that the source module actually exports this name
            if (moduleInfo.Exports != null)
            {
                if (!moduleInfo.Exports.Names.Contains(lexeme))
                {
                    resolution.Diagnostics.Add(
                        DiagnosticDescriptors.SA0825.CreateDiagnostic(nameToken.Span, exportPath, lexeme));
                    continue;
                }
            }

            // SA0827: detect redundant import+export pair
            // `import { x } from "p"; export { x };` is equivalent to `export { x } from "p";`
            // We detect the case where ExportFromStmt itself is used but the same name
            // was also imported via ImportStmt from the same resolved path AND is listed in
            // ExportBlockStmt. That SA0827 case is handled separately below in the SA0827 scanner.

            // Track for potential SA0827 detection (re-exported names by path)
            if (!resolution.ReExportedNamesByPath.TryGetValue(absolutePath, out var reExportedNames))
            {
                reExportedNames = new HashSet<string>();
                resolution.ReExportedNamesByPath[absolutePath] = reExportedNames;
            }
            reExportedNames.Add(lexeme);

            // D-12 IDE parity: push a fully-resolved SymbolInfo so that hover/go-to-def works
            // in the re-exporting file itself (not only in downstream importers).
            // Mirror the ResolveSelectiveImport pattern: Span = name-token span in this file,
            // FullSpan/SourceUri = the origin declaration's location.
            var exportedSymbol = moduleInfo.Symbols.GetTopLevel()
                .FirstOrDefault(s => s.Name == lexeme);

            if (exportedSymbol != null)
            {
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

                // Also push child symbols (struct fields, enum members, interface methods) so that
                // member-access expressions like `Color.Red` resolve correctly in the re-exporting file.
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
    }

    /// <summary>
    /// Resolves a namespace re-export: export "path.stash" as alias;
    /// </summary>
    private void ResolveExportModuleAs(ExportModuleAsStmt stmt, string documentDir, ImportResolution resolution,
        ModuleParser parseModule, Uri documentUri,
        Dictionary<string, HashSet<string>> reExportGraph, string? documentAbsPath)
    {
        var exportPath = (stmt.Path as LiteralExpr)?.Value as string;
        if (string.IsNullOrEmpty(exportPath))
        {
            if (stmt.Path is not LiteralExpr { Value: string })
            {
                resolution.Diagnostics.Add(DiagnosticDescriptors.SA0801.CreateDiagnostic(stmt.Path.Span));
            }
            return;
        }

        var absolutePath = ResolveImportToAbsolutePath(exportPath, documentDir, resolution, stmt.Path.Span);
        if (absolutePath == null)
        {
            return;
        }

        TrackDependency(absolutePath, documentUri);
        var moduleInfo = LoadModule(absolutePath, parseModule);

        // Record re-export edge for cycle detection
        if (documentAbsPath != null)
        {
            if (!reExportGraph.TryGetValue(documentAbsPath, out var edges))
            {
                edges = new HashSet<string>();
                reExportGraph[documentAbsPath] = edges;
            }
            edges.Add(absolutePath);

            // Also seed the target module's edges from its cached ReExportTargets
            if (!reExportGraph.ContainsKey(absolutePath))
            {
                reExportGraph[absolutePath] = new HashSet<string>(moduleInfo.ReExportTargets);
            }
        }

        // Expose the module under its alias for SA0825 isn't applicable here (it's a namespace alias).
        if (moduleInfo.Exports != null)
        {
            resolution.NamespaceImports[stmt.Alias.Lexeme] = BuildFilteredModuleInfo(moduleInfo);
        }
        else
        {
            resolution.NamespaceImports[stmt.Alias.Lexeme] = moduleInfo;
        }
    }

    /// <summary>
    /// Detects cycles in the re-export subgraph using three-color DFS.
    /// White = 0 (unvisited), Gray = 1 (in-progress), Black = 2 (done).
    /// Emits SA0826 on the first cycle found that includes <paramref name="startPath"/>.
    /// </summary>
    private void DetectReExportCycles(
        Dictionary<string, HashSet<string>> reExportGraph,
        string startPath,
        ImportResolution resolution,
        List<Stmt> statements,
        string documentDir)
    {
        var color = new Dictionary<string, int>();
        var path = new List<string>();
        var reported = new HashSet<string>();

        void Dfs(string current)
        {
            if (!reExportGraph.TryGetValue(current, out var neighbors))
            {
                return;
            }

            color[current] = 1; // gray
            path.Add(current);

            foreach (var neighbor in neighbors)
            {
                var neighborColor = color.TryGetValue(neighbor, out var c) ? c : 0;
                if (neighborColor == 1)
                {
                    // Found a cycle — build the cycle path string
                    int cycleStart = path.IndexOf(neighbor);
                    if (cycleStart < 0)
                    {
                        cycleStart = 0;
                    }

                    var cycleSegment = path.GetRange(cycleStart, path.Count - cycleStart);
                    cycleSegment.Add(neighbor);

                    // Use file names for readability
                    var cycleNames = cycleSegment.ConvertAll(p => Path.GetFileName(p));
                    var cycleKey = string.Join(" → ", cycleNames);

                    if (reported.Add(cycleKey))
                    {
                        // Find a span to attach the diagnostic to.
                        // First try: find the re-export stmt in the current document that references the
                        // neighbor (cycle-closing node). For direct cycles (A→A) this will be found.
                        // For transitive cycles (A→B→A), the current document only references B — so fall
                        // back to finding the re-export stmt that points to any node in the cycle path.
                        SourceSpan? diagnosticSpan = FindReExportSpanForPath(statements, neighbor, documentDir);

                        if (!diagnosticSpan.HasValue)
                        {
                            // Transitive cycle: find any re-export stmt in the current document
                            // that is part of the detected cycle path.
                            foreach (var cyclePath in cycleSegment)
                            {
                                diagnosticSpan = FindReExportSpanForPath(statements, cyclePath, documentDir);
                                if (diagnosticSpan.HasValue) break;
                            }
                        }

                        if (diagnosticSpan.HasValue)
                        {
                            resolution.Diagnostics.Add(
                                DiagnosticDescriptors.SA0826.CreateDiagnostic(diagnosticSpan.Value, cycleKey));
                        }
                    }
                }
                else if (neighborColor == 0)
                {
                    Dfs(neighbor);
                }
            }

            path.RemoveAt(path.Count - 1);
            color[current] = 2; // black
        }

        Dfs(startPath);
    }

    /// <summary>
    /// Finds the source span of the name token in an <see cref="ExportFromStmt"/> for a given
    /// resolved absolute source path and name. Used to attach SA0827 to the right token.
    /// </summary>
    private static SourceSpan? FindExportFromNameSpan(List<Stmt> statements, string targetAbsPath, string documentDir, string name)
    {
        foreach (var stmt in statements)
        {
            if (stmt is not ExportFromStmt ef)
            {
                continue;
            }

            var pathValue = (ef.Path as LiteralExpr)?.Value as string;
            if (pathValue == null)
            {
                continue;
            }

            var absPath = ResolveImportToAbsolutePathSilent(pathValue, documentDir);
            if (absPath == null || !string.Equals(absPath, targetAbsPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var nameTok in ef.Names)
            {
                if (nameTok.Lexeme == name)
                {
                    return nameTok.Span;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the raw path string from the first <see cref="ExportFromStmt"/> that exports the
    /// given name. Used to format the SA0827 message with the original path string.
    /// </summary>
    private static string? FindExportFromPathString(List<Stmt> statements, string name)
    {
        foreach (var stmt in statements)
        {
            if (stmt is not ExportFromStmt ef)
            {
                continue;
            }

            foreach (var nameTok in ef.Names)
            {
                if (nameTok.Lexeme == name)
                {
                    return (ef.Path as LiteralExpr)?.Value as string;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the source span of a re-export statement that references the given target path.
    /// Resolves each statement's path to an absolute path and compares case-insensitively to
    /// avoid false matches when two files share a basename in different directories.
    /// Returns null if not found.
    /// </summary>
    private static SourceSpan? FindReExportSpanForPath(List<Stmt> statements, string targetAbsPath, string documentDir)
    {
        foreach (var stmt in statements)
        {
            string? pathValue = stmt switch
            {
                ExportFromStmt ef => (ef.Path as LiteralExpr)?.Value as string,
                ExportModuleAsStmt em => (em.Path as LiteralExpr)?.Value as string,
                _ => null,
            };
            if (pathValue == null)
            {
                continue;
            }

            // Resolve the statement's path to absolute and compare case-insensitively.
            // This prevents wrong-span attribution when two .stash files share a basename
            // in different subdirectories.
            var stmtAbsPath = ResolveImportToAbsolutePathSilent(pathValue, documentDir);
            if (stmtAbsPath != null &&
                string.Equals(stmtAbsPath, targetAbsPath, StringComparison.OrdinalIgnoreCase))
            {
                return stmt.Span;
            }
        }
        return null;
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
        var importPath = stmt.StaticPathValue;
        if (string.IsNullOrEmpty(importPath))
        {
            if (!stmt.IsStaticPath)
            {
                resolution.Diagnostics.Add(DiagnosticDescriptors.SA0801.CreateDiagnostic(stmt.Path.Span));
            }
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
            var lexeme = nameToken.Lexeme;

            // Check that the name is in the module's export set.
            if (moduleInfo.Exports != null)
            {
                if (!moduleInfo.Exports.Names.Contains(lexeme))
                {
                    // SA0828: plain import references a name not in the module's export set.
                    resolution.Diagnostics.Add(
                        DiagnosticDescriptors.SA0828.CreateDiagnostic(nameToken.Span, importPath, lexeme));

                    // SA0809: if the name exists as a private top-level declaration, hint the author.
                    var privateSymbol = moduleInfo.Symbols.GetTopLevel()
                        .FirstOrDefault(s => s.Name == lexeme);
                    if (privateSymbol != null)
                    {
                        resolution.Diagnostics.Add(
                            DiagnosticDescriptors.SA0809.CreateDiagnostic(nameToken.Span, lexeme, importPath));
                    }

                    continue;
                }
            }

            var exportedSymbol = moduleInfo.Symbols.GetTopLevel()
                .FirstOrDefault(s => s.Name == lexeme);

            if (exportedSymbol == null)
            {
                // SA0828: name not found in the module's top-level symbols.
                resolution.Diagnostics.Add(
                    DiagnosticDescriptors.SA0828.CreateDiagnostic(nameToken.Span, importPath, lexeme));
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
        var importPath = stmt.StaticPathValue;
        if (string.IsNullOrEmpty(importPath))
        {
            if (!stmt.IsStaticPath)
            {
                resolution.Diagnostics.Add(DiagnosticDescriptors.SA0801.CreateDiagnostic(stmt.Path.Span));
            }
            return;
        }

        var absolutePath = ResolveImportToAbsolutePath(importPath, documentDir, resolution, stmt.Path.Span);
        if (absolutePath == null)
        {
            return;
        }

        TrackDependency(absolutePath, documentUri);
        var moduleInfo = LoadModule(absolutePath, parseModule);

        // Expose only the exported symbols for dot-completion on the alias
        // (keeps LSP completion consistent with runtime behaviour).
        if (moduleInfo.Exports != null)
        {
            resolution.NamespaceImports[stmt.Alias.Lexeme] = BuildFilteredModuleInfo(moduleInfo);
        }
        else
        {
            resolution.NamespaceImports[stmt.Alias.Lexeme] = moduleInfo;
        }
    }

    /// <summary>
    /// Builds a <see cref="ModuleInfo"/> whose symbol tree contains only the exported symbols
    /// (and their children — struct fields, enum members, interface methods).
    /// </summary>
    private static ModuleInfo BuildFilteredModuleInfo(ModuleInfo moduleInfo)
    {
        var exports = moduleInfo.Exports!;
        var originalScope = moduleInfo.Symbols.GlobalScope;

        var filteredScope = new Scope(originalScope.Kind, null, originalScope.Span);

        // Collect the names of exported parent types so we can include their children too.
        var exportedParentNames = new HashSet<string>(exports.Names);

        foreach (var sym in originalScope.Symbols)
        {
            // Include exported top-level symbols.
            if (sym.ParentName == null && exports.Names.Contains(sym.Name))
            {
                filteredScope.AddSymbol(sym);
                continue;
            }

            // Include child symbols (fields, enum members, methods) whose parent is exported.
            if (sym.ParentName != null && exportedParentNames.Contains(sym.ParentName))
            {
                filteredScope.AddSymbol(sym);
            }
        }

        var filteredTree = new ScopeTree(filteredScope);
        return new ModuleInfo(moduleInfo.Uri, moduleInfo.AbsolutePath, filteredTree, moduleInfo.Errors, exports);
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

            if (Path.HasExtension(importPath) && !importPath.StartsWith('@'))
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

        // Guard against circular imports.  All _loadingModules mutations are serialised through a
        // lock so that concurrent LSP requests cannot race on the Add/Remove pair — two threads
        // would otherwise both pass the TryGetValue check above and then both attempt Add,
        // leaving one of them seeing a false "already loading" signal.
        bool added;
        lock (_loadingModules)
        {
            added = _loadingModules.Add(absolutePath);
        }

        if (!added)
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
            lock (_loadingModules)
            {
                _loadingModules.Remove(absolutePath);
            }
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
