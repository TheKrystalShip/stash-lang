namespace Stash.Lsp.Completion;

using System.Collections.Generic;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Shared interop helpers for mapping Stash analysis types to LSP completion types.
/// Relocated from <c>CompletionHandler</c> in Phase 5; consumed by completion providers
/// and the handler itself.
/// </summary>
public static class CompletionInterop
{
    /// <summary>
    /// Maps a Stash <see cref="StashSymbolKind"/> to the LSP completion kind appropriate
    /// for a <strong>bare identifier</strong> context (Default-mode completion).
    /// </summary>
    /// <param name="kind">The Stash symbol kind to map.</param>
    /// <returns>
    /// The equivalent LSP completion item kind, or <see langword="null"/> when
    /// <paramref name="kind"/> is a member-only kind (<c>Field</c>, <c>EnumMember</c>)
    /// that must never appear as a bare identifier.
    /// Callers in Default-mode providers should skip the candidate when <see langword="null"/>
    /// is returned — this indicates a provider bug (member kind bypassed the accessibility
    /// pre-filter).
    /// </returns>
    public static LspCompletionItemKind? MapBareIdentifierKind(StashSymbolKind kind) => kind switch
    {
        StashSymbolKind.Function     => LspCompletionItemKind.Function,
        StashSymbolKind.Variable     => LspCompletionItemKind.Variable,
        StashSymbolKind.Constant     => LspCompletionItemKind.Constant,
        StashSymbolKind.Struct       => LspCompletionItemKind.Struct,
        StashSymbolKind.Enum         => LspCompletionItemKind.Enum,
        StashSymbolKind.Parameter    => LspCompletionItemKind.Variable,
        StashSymbolKind.LoopVariable => LspCompletionItemKind.Variable,
        StashSymbolKind.Namespace    => LspCompletionItemKind.Module,
        // Field and EnumMember are member-only kinds — not valid as bare identifiers.
        _                            => null
    };

    /// <summary>
    /// Maps a Stash <see cref="StashSymbolKind"/> to the LSP completion kind appropriate
    /// for a <strong>dot-access</strong> context (Dot-mode completion strategies).
    /// </summary>
    /// <param name="kind">The Stash symbol kind to map.</param>
    /// <returns>The equivalent LSP completion item kind.</returns>
    public static LspCompletionItemKind MapMemberKind(StashSymbolKind kind) => kind switch
    {
        StashSymbolKind.Function     => LspCompletionItemKind.Function,
        StashSymbolKind.Variable     => LspCompletionItemKind.Variable,
        StashSymbolKind.Constant     => LspCompletionItemKind.Constant,
        StashSymbolKind.Struct       => LspCompletionItemKind.Struct,
        StashSymbolKind.Enum         => LspCompletionItemKind.Enum,
        StashSymbolKind.EnumMember   => LspCompletionItemKind.EnumMember,
        StashSymbolKind.Field        => LspCompletionItemKind.Field,
        StashSymbolKind.Parameter    => LspCompletionItemKind.Variable,
        StashSymbolKind.LoopVariable => LspCompletionItemKind.Variable,
        StashSymbolKind.Namespace    => LspCompletionItemKind.Module,
        _                            => LspCompletionItemKind.Text
    };

    /// <summary>
    /// Converts AST throws entries to stdlib model throws entries so they can be
    /// rendered by <see cref="ThrowsRenderer.Render"/>.
    /// </summary>
    /// <param name="throws">The AST-level throws entries, or <see langword="null"/>.</param>
    /// <returns>
    /// Converted throws entries, or <see langword="null"/> when <paramref name="throws"/>
    /// is <see langword="null"/> or empty.
    /// </returns>
    public static Stash.Stdlib.Models.ThrowsEntry[]? AdaptThrows(
        IReadOnlyList<Stash.Parsing.AST.ThrowsEntry>? throws)
    {
        if (throws == null || throws.Count == 0) return null;
        var result = new Stash.Stdlib.Models.ThrowsEntry[throws.Count];
        for (int i = 0; i < throws.Count; i++)
            result[i] = new Stash.Stdlib.Models.ThrowsEntry(throws[i].ErrorType, throws[i].Description);
        return result;
    }
}
