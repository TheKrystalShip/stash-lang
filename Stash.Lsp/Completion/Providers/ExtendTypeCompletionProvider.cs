namespace Stash.Lsp.Completion.Providers;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Common;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Provides type-name completion candidates for <c>extend</c> declarations.
/// Emits built-in extendable types first (derived from <see cref="PrimitiveTypes"/>),
/// then user-defined struct names from the current analysis result, with deduplication
/// that prevents built-in names from appearing twice if a user defines a struct with
/// the same name.
/// </summary>
/// <remarks>
/// <para>
/// Applies exclusively to <see cref="CompletionMode.AfterExtend"/> mode.
/// </para>
/// <para>
/// Dedup behaviour mirrors <c>BuildExtendTypeCompletionList</c>: the built-in type names
/// are seeded into a <see cref="HashSet{T}"/> before user structs are enumerated, so
/// built-ins always win on collision.
/// </para>
/// <para>
/// The extendable built-in types are derived from <see cref="PrimitiveTypes.Names"/>
/// by excluding meta/structural types (<c>bool</c>, <c>null</c>, <c>struct</c>,
/// <c>enum</c>, <c>function</c>, <c>namespace</c>) and typed-array variants (names
/// containing <c>[]</c>). This matches the monolith's <c>BuildExtendTypeCompletionList</c>
/// list exactly while keeping the source of truth in <see cref="PrimitiveTypes"/>.
/// </para>
/// </remarks>
public sealed class ExtendTypeCompletionProvider : ICompletionProvider
{
    /// <summary>
    /// Primitive type names that are NOT extendable — structural or meta types.
    /// Derived from <see cref="PrimitiveTypes.Names"/> by exclusion.
    /// </summary>
    private static readonly FrozenSet<string> _nonExtendable =
        new HashSet<string>(StringComparer.Ordinal) { "bool", "null", "struct", "enum", "function", "namespace" }
            .ToFrozenSet();

    /// <summary>
    /// The built-in extendable type names, derived from <see cref="PrimitiveTypes.Names"/>
    /// by excluding non-extendable types and typed-array variants (e.g., <c>int[]</c>).
    /// </summary>
    private static readonly IReadOnlyList<string> _builtInExtendableTypes =
        PrimitiveTypes.Names
            .Where(static n => !_nonExtendable.Contains(n) && !n.Contains('['))
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray();

    /// <inheritdoc />
    public bool AppliesTo(CompletionContext ctx) => ctx.Mode == CompletionMode.AfterExtend;

    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        // Built-in extendable types — always emitted first
        foreach (var typeName in _builtInExtendableTypes)
        {
            yield return new CompletionCandidate(
                Label: typeName,
                Kind: LspCompletionItemKind.TypeParameter,
                Detail: "built-in type",
                SourcePriority: 10,
                SourceTag: nameof(ExtendTypeCompletionProvider));
        }

        // User-defined struct names from analysis (dedup against built-in names)
        if (ctx.Analysis == null)
        {
            yield break;
        }

        var seen = new HashSet<string>(_builtInExtendableTypes, StringComparer.Ordinal);
        foreach (var sym in ctx.Analysis.Symbols.All.Where(s => s.Kind == StashSymbolKind.Struct))
        {
            if (!seen.Add(sym.Name))
            {
                continue;
            }

            yield return new CompletionCandidate(
                Label: sym.Name,
                Kind: LspCompletionItemKind.Struct,
                Detail: sym.Detail,
                SourcePriority: 10,
                SourceTag: nameof(ExtendTypeCompletionProvider));
        }
    }
}
