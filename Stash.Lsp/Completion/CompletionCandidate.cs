namespace Stash.Lsp.Completion;

using Stash.Analysis;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

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
public sealed record CompletionCandidate(
    string Label,
    LspCompletionItemKind Kind,
    string? Detail = null,
    string? Documentation = null,
    int SourcePriority = 100,
    string SourceTag = "",
    SymbolAccessibility? Accessibility = null);
