namespace Stash.Analysis;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Stash.Common;
using Stash.Parsing.AST;

/// <summary>
/// Describes the export role of a single top-level name in a module.
/// </summary>
/// <param name="Kind">The declaration kind of the exported symbol.</param>
/// <param name="DeclSpan">The source span of the underlying declaration's name token.</param>
/// <param name="ExportSpan">The source span of the export annotation (the export keyword or block entry) that introduced this name into the export set.</param>
public sealed record ExportEntry(SymbolKind Kind, SourceSpan DeclSpan, SourceSpan ExportSpan);

/// <summary>
/// Represents the explicit export set of a Stash module.
/// </summary>
/// <remarks>
/// When <see cref="HasExplicitExports"/> is <see langword="false"/>, the module uses the
/// legacy "export everything" semantics and <see cref="Names"/> is empty.
/// When <see langword="true"/>, only names in <see cref="Names"/> are visible to importers.
/// </remarks>
public sealed class ModuleExports
{
    /// <summary>
    /// Gets whether the module contains at least one <c>export</c> annotation.
    /// When <see langword="false"/>, all top-level bindings are exported (legacy semantics).
    /// </summary>
    public bool HasExplicitExports { get; }

    /// <summary>
    /// Gets the set of exported names and their entries.
    /// Empty when <see cref="HasExplicitExports"/> is <see langword="false"/>.
    /// </summary>
    public IReadOnlyDictionary<string, ExportEntry> Names { get; }

    private ModuleExports(bool hasExplicitExports, IReadOnlyDictionary<string, ExportEntry> names)
    {
        HasExplicitExports = hasExplicitExports;
        Names = names;
    }

    /// <summary>
    /// Walks the top-level statement list once, collects all export annotations, validates
    /// names against the top-level declaration set, and emits SA0805–SA0808 for violations.
    /// </summary>
    /// <param name="topLevel">The top-level statement list of the module.</param>
    /// <param name="scopeTree">The scope tree produced by <see cref="SymbolCollector"/>.</param>
    /// <param name="diagnostics">The diagnostic list to which violations are appended.</param>
    /// <returns>
    /// A <see cref="ModuleExports"/> record. When no export annotation is found,
    /// <see cref="HasExplicitExports"/> is <see langword="false"/> and <see cref="Names"/>
    /// is empty. Otherwise the exact export set is returned.
    /// </returns>
    public static ModuleExports Build(
        IReadOnlyList<Stmt> topLevel,
        ScopeTree scopeTree,
        List<SemanticDiagnostic> diagnostics)
    {
        // Fast path: check whether any export annotations exist.
        bool hasAny = false;
        foreach (var stmt in topLevel)
        {
            if (stmt is ExportDeclStmt or ExportBlockStmt)
            {
                hasAny = true;
                break;
            }
        }

        if (!hasAny)
        {
            return new ModuleExports(false, new ReadOnlyDictionary<string, ExportEntry>(
                new Dictionary<string, ExportEntry>()));
        }

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
            }
        }

        return new ModuleExports(true, new ReadOnlyDictionary<string, ExportEntry>(names));
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
