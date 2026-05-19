namespace Stash.Analysis;

using System.Collections.Generic;
using System.Collections.Immutable;
using Stash.Common;
using Stash.Core.Resolution;
using Stash.Parsing.AST;

/// <summary>
/// Describes the export role of a single top-level name in a module.
/// </summary>
/// <param name="Kind">The declaration kind of the exported symbol.</param>
/// <param name="DeclSpan">The source span of the underlying declaration's name token.</param>
/// <param name="ExportSpan">The source span of the export annotation (the export keyword or block entry) that introduced this name into the export set.</param>
/// <param name="OriginPath">
/// For re-exported names (<c>export { x } from "p";</c> or <c>export "p" as x;</c>), the resolved
/// absolute path of the source module. <see langword="null"/> for locally-declared exports.
/// Lives only on the Analysis side — not propagated to <see cref="Stash.Core.Resolution.ModuleExports"/>.
/// </param>
internal sealed record ExportEntry(SymbolKind Kind, SourceSpan DeclSpan, SourceSpan ExportSpan, string? OriginPath = null);

/// <summary>
/// Builds the lightweight <see cref="Stash.Core.Resolution.ModuleExports"/> record that is
/// attached to the compiled <c>Chunk</c> and used by the runtime filter at module-load time.
/// </summary>
public static class ModuleExportsBuilder
{
    /// <summary>
    /// Walks the top-level statement list once, collects all export annotations, validates
    /// names against the top-level declaration set, emits SA0805–SA0808 for violations,
    /// and produces the lightweight <see cref="Stash.Core.Resolution.ModuleExports"/> record
    /// suitable for attaching to a compiled <c>Chunk</c>.
    /// </summary>
    /// <param name="topLevel">The top-level statement list of the module.</param>
    /// <param name="diagnostics">
    /// The diagnostic list to which SA0805–SA0808 violations are appended.
    /// </param>
    /// <returns>
    /// A <see cref="Stash.Core.Resolution.ModuleExports"/> instance.
    /// When no export annotation is found, <see cref="Stash.Core.Resolution.ModuleExports.Names"/> is
    /// empty, meaning the module exports nothing to importers.
    /// </returns>
    public static Stash.Core.Resolution.ModuleExports Build(
        IReadOnlyList<Stmt> topLevel,
        List<SemanticDiagnostic> diagnostics)
    {
        return Build(topLevel, diagnostics, out _);
    }

    /// <summary>
    /// Builds the <see cref="Stash.Core.Resolution.ModuleExports"/> record and also exposes the
    /// per-name <see cref="ExportEntry"/> map for analysis-time use (e.g. <see cref="ImportResolver"/>
    /// needs <see cref="ExportEntry.OriginPath"/> to detect re-export cycles and enrich the
    /// re-export graph).
    /// </summary>
    /// <param name="topLevel">The top-level statement list of the module.</param>
    /// <param name="diagnostics">
    /// The diagnostic list to which SA0805–SA0808 violations are appended.
    /// </param>
    /// <param name="entries">
    /// When this method returns, contains the per-name export-entry map produced during the walk.
    /// Entries for re-exported names carry a non-null <see cref="ExportEntry.OriginPath"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Stash.Core.Resolution.ModuleExports"/> instance.
    /// </returns>
    internal static Stash.Core.Resolution.ModuleExports Build(
        IReadOnlyList<Stmt> topLevel,
        List<SemanticDiagnostic> diagnostics,
        out Dictionary<string, ExportEntry> entries)
    {
        // Build an index of top-level declarations for fast lookup.
        var topLevelIndex = BuildTopLevelIndex(topLevel);

        var names = new Dictionary<string, ExportEntry>();

        foreach (var stmt in topLevel)
        {
            switch (stmt)
            {
                case ExportDeclStmt exportDecl:
                    ProcessExportDecl(exportDecl, names, diagnostics);
                    break;

                case ExportBlockStmt exportBlock:
                    ProcessExportBlock(exportBlock, topLevelIndex, names, diagnostics);
                    break;

                case ExportModuleAsStmt exportModuleAs:
                    ProcessExportModuleAs(exportModuleAs, names, diagnostics);
                    break;

                case ExportFromStmt exportFrom:
                    ProcessExportFrom(exportFrom, names, diagnostics);
                    break;
            }
        }

        entries = names;

        if (names.Count == 0)
            return Stash.Core.Resolution.ModuleExports.Empty;

        return Stash.Core.Resolution.ModuleExports.Create(ImmutableHashSet.CreateRange(names.Keys));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a lookup from declaration name to its descriptor for all top-level statements.
    /// Does not include <see cref="ExportDeclStmt"/> wrappers — those are unwrapped to their inner.
    /// </summary>
    private static Dictionary<string, TopLevelEntry> BuildTopLevelIndex(IReadOnlyList<Stmt> topLevel)
    {
        var index = new Dictionary<string, TopLevelEntry>();

        foreach (var stmt in topLevel)
        {
            var effective = stmt is ExportDeclStmt ed ? ed.Inner : stmt;

            switch (effective)
            {
                case FnDeclStmt fn:
                    index.TryAdd(fn.Name.Lexeme, new TopLevelEntry(SymbolKind.Function, fn.Name.Span, false));
                    break;
                case ConstDeclStmt c:
                    index.TryAdd(c.Name.Lexeme, new TopLevelEntry(SymbolKind.Constant, c.Name.Span, false));
                    break;
                case StructDeclStmt s:
                    index.TryAdd(s.Name.Lexeme, new TopLevelEntry(SymbolKind.Struct, s.Name.Span, false));
                    break;
                case EnumDeclStmt e:
                    index.TryAdd(e.Name.Lexeme, new TopLevelEntry(SymbolKind.Enum, e.Name.Span, false));
                    break;
                case InterfaceDeclStmt iface:
                    index.TryAdd(iface.Name.Lexeme, new TopLevelEntry(SymbolKind.Interface, iface.Name.Span, false));
                    break;
                case VarDeclStmt v:
                    // let bindings — tracked so we can emit SA0805
                    index.TryAdd(v.Name.Lexeme, new TopLevelEntry(SymbolKind.Variable, v.Name.Span, true));
                    break;
                case ImportStmt importStmt:
                    // Selective import: each imported name is a binding
                    foreach (var nameTok in importStmt.Names)
                    {
                        index.TryAdd(nameTok.Lexeme, new TopLevelEntry(SymbolKind.Namespace, nameTok.Span, false, isImport: true));
                    }
                    break;
                case ImportAsStmt importAs:
                    index.TryAdd(importAs.Alias.Lexeme, new TopLevelEntry(SymbolKind.Namespace, importAs.Alias.Span, false, isImport: true));
                    break;
                case ExportModuleAsStmt exportModuleAs:
                    // The alias is both a local namespace binding (like ImportAsStmt) and an export.
                    index.TryAdd(exportModuleAs.Alias.Lexeme, new TopLevelEntry(SymbolKind.Namespace, exportModuleAs.Alias.Span, false, isImport: true));
                    break;
                case ExportFromStmt exportFrom:
                    // Each name is both a local binding (like ImportStmt) and an export.
                    foreach (var nameTok in exportFrom.Names)
                    {
                        index.TryAdd(nameTok.Lexeme, new TopLevelEntry(SymbolKind.Namespace, nameTok.Span, false, isImport: true));
                    }
                    break;
            }
        }

        return index;
    }

    private static void ProcessExportDecl(
        ExportDeclStmt exportDecl,
        Dictionary<string, ExportEntry> names,
        List<SemanticDiagnostic> diagnostics)
    {
        var (name, kind, declSpan) = GetDeclInfo(exportDecl.Inner);
        var exportSpan = exportDecl.ExportKeyword.Span;

        if (names.TryGetValue(name, out var existing))
        {
            // SA0808: duplicate export
            diagnostics.Add(DiagnosticDescriptors.SA0808.CreateDiagnosticWithRelated(
                exportSpan,
                [new RelatedLocation("First exported here.", existing.ExportSpan)],
                name));
        }
        else
        {
            names[name] = new ExportEntry(kind, declSpan, exportSpan);
        }
    }

    private static void ProcessExportBlock(
        ExportBlockStmt exportBlock,
        Dictionary<string, TopLevelEntry> topLevelIndex,
        Dictionary<string, ExportEntry> names,
        List<SemanticDiagnostic> diagnostics)
    {
        foreach (var nameTok in exportBlock.Names)
        {
            var lexeme = nameTok.Lexeme;

            if (!topLevelIndex.TryGetValue(lexeme, out var entry))
            {
                // SA0807: unknown name
                diagnostics.Add(DiagnosticDescriptors.SA0807.CreateDiagnostic(nameTok.Span, lexeme));
                continue;
            }

            if (entry.IsMutable)
            {
                // SA0805: cannot export let binding
                diagnostics.Add(DiagnosticDescriptors.SA0805.CreateDiagnostic(nameTok.Span, lexeme));
                continue;
            }

            if (entry.IsImport)
            {
                // SA0806: cannot export import binding
                diagnostics.Add(DiagnosticDescriptors.SA0806.CreateDiagnostic(nameTok.Span, lexeme));
                continue;
            }

            if (names.TryGetValue(lexeme, out var existing))
            {
                // SA0808: duplicate export
                diagnostics.Add(DiagnosticDescriptors.SA0808.CreateDiagnosticWithRelated(
                    nameTok.Span,
                    [new RelatedLocation("First exported here.", existing.ExportSpan)],
                    lexeme));
            }
            else
            {
                names[lexeme] = new ExportEntry(entry.Kind, entry.DeclSpan, nameTok.Span);
            }
        }
    }

    private static void ProcessExportModuleAs(
        ExportModuleAsStmt exportModuleAs,
        Dictionary<string, ExportEntry> names,
        List<SemanticDiagnostic> diagnostics)
    {
        var aliasLexeme = exportModuleAs.Alias.Lexeme;
        var exportSpan = exportModuleAs.Alias.Span;
        // Capture the raw path string so LSP hover/go-to-def can follow the chain
        // via HoverHandler.ResolveReExportChain (matches the pattern used by ProcessExportFrom).
        var originPath = (exportModuleAs.Path as LiteralExpr)?.Value as string;

        if (names.TryGetValue(aliasLexeme, out var existing))
        {
            // SA0808: duplicate export
            diagnostics.Add(DiagnosticDescriptors.SA0808.CreateDiagnosticWithRelated(
                exportSpan,
                [new RelatedLocation("First exported here.", existing.ExportSpan)],
                aliasLexeme));
        }
        else
        {
            names[aliasLexeme] = new ExportEntry(SymbolKind.Namespace, exportModuleAs.Alias.Span, exportSpan, originPath);
        }
    }

    private static void ProcessExportFrom(
        ExportFromStmt exportFrom,
        Dictionary<string, ExportEntry> names,
        List<SemanticDiagnostic> diagnostics)
    {
        // SA0823 is emitted by the SemanticValidator, not here; the builder simply skips empty lists.
        // OriginPath is the raw path string from the statement (relative or bare specifier).
        // Full resolution to an absolute path is deferred to ImportResolver (Phase 2G).
        var originPath = (exportFrom.Path as LiteralExpr)?.Value as string;

        foreach (var nameTok in exportFrom.Names)
        {
            var lexeme = nameTok.Lexeme;
            var exportSpan = nameTok.Span;

            if (names.TryGetValue(lexeme, out var existing))
            {
                // SA0808: duplicate export
                diagnostics.Add(DiagnosticDescriptors.SA0808.CreateDiagnosticWithRelated(
                    exportSpan,
                    [new RelatedLocation("First exported here.", existing.ExportSpan)],
                    lexeme));
            }
            else
            {
                names[lexeme] = new ExportEntry(SymbolKind.Namespace, nameTok.Span, exportSpan, originPath);
            }
        }
    }

    private static (string Name, SymbolKind Kind, SourceSpan DeclSpan) GetDeclInfo(Stmt inner)
    {
        return inner switch
        {
            FnDeclStmt fn => (fn.Name.Lexeme, SymbolKind.Function, fn.Name.Span),
            ConstDeclStmt c => (c.Name.Lexeme, SymbolKind.Constant, c.Name.Span),
            StructDeclStmt s => (s.Name.Lexeme, SymbolKind.Struct, s.Name.Span),
            EnumDeclStmt e => (e.Name.Lexeme, SymbolKind.Enum, e.Name.Span),
            InterfaceDeclStmt iface => (iface.Name.Lexeme, SymbolKind.Interface, iface.Name.Span),
            _ => throw new System.InvalidOperationException(
                $"Unexpected inner declaration type in ExportDeclStmt: {inner.GetType().Name}")
        };
    }

    /// <summary>Internal descriptor for a top-level declaration entry.</summary>
    private readonly struct TopLevelEntry
    {
        public readonly SymbolKind Kind;
        public readonly SourceSpan DeclSpan;
        public readonly bool IsMutable;
        public readonly bool IsImport;

        public TopLevelEntry(SymbolKind kind, SourceSpan declSpan, bool isMutable, bool isImport = false)
        {
            Kind = kind;
            DeclSpan = declSpan;
            IsMutable = isMutable;
            IsImport = isImport;
        }
    }
}
