namespace Stash.Lsp.Completion;

using Stash.Analysis;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using LspInsertTextFormat = OmniSharp.Extensions.LanguageServer.Protocol.Models.InsertTextFormat;

/// <summary>
/// A pre-LSP completion candidate carrying the metadata that the
/// <see cref="CompletionItemSink"/> needs to deduplicate, order, and convert
/// to an OmniSharp <c>CompletionItem</c> on materialization.
/// </summary>
/// <param name="Label">
/// The completion text that appears in the IDE dropdown and is used as the dedup key.
/// </param>
/// <param name="Kind">The LSP completion item kind (function, keyword, variable, …).</param>
/// <param name="Detail">
/// Optional human-readable signature or type hint shown alongside the label, or <see langword="null"/>.
/// </param>
/// <param name="Documentation">
/// Optional documentation text shown in the completion detail panel, or <see langword="null"/>.
/// </param>
/// <param name="SourcePriority">
/// Ordering weight within a mode's pipeline. Lower values win when two providers contribute
/// the same label. Defaults to <c>100</c>.
/// </param>
/// <param name="SourceTag">
/// Opaque tag identifying which provider produced this candidate.
/// Always tracked per the Q1 decision in the feature Decision Log — invaluable for
/// diagnosing "wrong completion appeared" reports. Must not be <see langword="null"/>;
/// providers must supply a non-empty string (e.g. <c>nameof(MyProvider)</c>).
/// </param>
/// <param name="Accessibility">
/// Carries the originating <see cref="SymbolAccessibility"/> so the sink can enforce
/// defence-in-depth filtering (e.g., reject <see cref="SymbolAccessibility.RequiresQualification"/>
/// in <see cref="CompletionMode.Default"/> mode).
/// <see langword="null"/> means "unclassified" — the sink does not filter unclassified candidates.
/// </param>
/// <param name="InsertText">
/// Optional insertion template that overrides the label when the IDE commits the item.
/// When <see langword="null"/> (the default), the IDE inserts <see cref="Label"/> as plain
/// text — preserving the existing behaviour for all non-snippet providers.
/// When non-null, the sink copies this value to <c>CompletionItem.InsertText</c> and sets
/// <c>CompletionItem.InsertTextFormat</c> accordingly.
/// </param>
/// <param name="InsertTextFormat">
/// The format of <see cref="InsertText"/>. Defaults to <see cref="LspInsertTextFormat.PlainText"/>,
/// which matches the LSP default and preserves backward-compatibility. Set to
/// <see cref="LspInsertTextFormat.Snippet"/> when <see cref="InsertText"/> contains
/// tab-stop placeholders (<c>$1</c>, <c>${2:name}</c>, etc.).
/// Ignored when <see cref="InsertText"/> is <see langword="null"/>.
/// </param>
public sealed record CompletionCandidate(
    string Label,
    LspCompletionItemKind Kind,
    string? Detail = null,
    string? Documentation = null,
    int SourcePriority = 100,
    string SourceTag = "",
    SymbolAccessibility? Accessibility = null,
    string? InsertText = null,
    LspInsertTextFormat InsertTextFormat = LspInsertTextFormat.PlainText);
