namespace Stash.Lsp.Completion;

using System.Collections.Generic;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Collects <see cref="CompletionCandidate"/> instances from one or more providers
/// and produces a deduplicated, insertion-ordered <see cref="CompletionList"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deduplication is label-based and first-wins: the first call to <see cref="Add"/>
/// for a given <see cref="CompletionCandidate.Label"/> is kept; subsequent calls for
/// the same label are silently ignored. Providers must therefore be invoked in
/// descending priority order (highest-priority provider first) so that its candidate
/// wins the dedup race.
/// </para>
/// <para>
/// Defence-in-depth filtering: when <see cref="CompletionMode.Default"/> mode is
/// active, any candidate whose <see cref="CompletionCandidate.Accessibility"/> is
/// <see cref="SymbolAccessibility.RequiresQualification"/> is rejected outright,
/// even if no provider-level filter has already excluded it. This catches candidates
/// that a future provider accidentally emits without the correct provider-side guard.
/// </para>
/// <para>
/// <see cref="CompletionCandidate.SourceTag"/> is always stored and round-tripped
/// through the materialized <see cref="CompletionItem.Data"/> field as a JSON string
/// so that diagnostics and snapshot-test attribution remain available after
/// materialization. (Decision Log Q1: always track SourceTag.)
/// </para>
/// <para>
/// The sink is per-request and not thread-safe. Providers are stateless and the
/// dispatcher creates a new sink for each call to <see cref="CompletionDispatcher.Run"/>.
/// </para>
/// </remarks>
public sealed class CompletionItemSink
{
    private readonly CompletionMode _mode;
    private readonly HashSet<string> _seen = new(System.StringComparer.Ordinal);
    private readonly List<CompletionItem> _items = new();

    /// <summary>
    /// Initialises the sink for the given completion mode.
    /// The mode determines whether defence-in-depth accessibility filtering is applied.
    /// </summary>
    /// <param name="mode">
    /// The dispatcher-assigned mode for the current request.
    /// When <see cref="CompletionMode.Default"/>, candidates with
    /// <see cref="SymbolAccessibility.RequiresQualification"/> are rejected.
    /// </param>
    public CompletionItemSink(CompletionMode mode)
    {
        _mode = mode;
    }

    /// <summary>
    /// Attempts to add <paramref name="candidate"/> to the sink.
    /// </summary>
    /// <remarks>
    /// The call is a no-op when:
    /// <list type="bullet">
    ///   <item>A candidate with the same <see cref="CompletionCandidate.Label"/> has already been added (first-wins dedup).</item>
    ///   <item>The sink is in <see cref="CompletionMode.Default"/> mode and the candidate's
    ///         <see cref="CompletionCandidate.Accessibility"/> is
    ///         <see cref="SymbolAccessibility.RequiresQualification"/> (defence-in-depth guard).</item>
    /// </list>
    /// </remarks>
    /// <param name="candidate">The candidate to add.</param>
    public void Add(CompletionCandidate candidate)
    {
        // Defence-in-depth: reject member-style candidates in Default mode.
        if (_mode == CompletionMode.Default &&
            candidate.Accessibility == SymbolAccessibility.RequiresQualification)
        {
            return;
        }

        // First-wins dedup on label.
        if (!_seen.Add(candidate.Label))
        {
            return;
        }

        var item = new CompletionItem
        {
            Label = candidate.Label,
            Kind = candidate.Kind,
            Detail = candidate.Detail,
            Documentation = candidate.Documentation is not null
                ? new StringOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = candidate.Documentation
                })
                : null,
            // Round-trip SourceTag through Data so diagnostics and snapshot tests can
            // inspect which provider contributed each item after materialization.
            // SourceTag is non-nullable on CompletionCandidate (Decision Log Q1: always track).
            Data = candidate.SourceTag,
            // Copy insertion-template fields only when InsertText is explicitly provided.
            // When null, the IDE falls back to inserting Label (existing behaviour unchanged).
            // InsertTextFormat defaults to PlainText on CompletionItem, so omitting it when
            // InsertText is null preserves the pre-existing wire format.
            InsertText = candidate.InsertText,
            InsertTextFormat = candidate.InsertText is not null
                ? candidate.InsertTextFormat
                : InsertTextFormat.PlainText
        };

        _items.Add(item);
    }

    /// <summary>
    /// Materialises all accepted candidates into an OmniSharp <see cref="CompletionList"/>.
    /// Item order matches insertion order (i.e., provider priority order).
    /// </summary>
    /// <returns>A <see cref="CompletionList"/> ready to return from the LSP handler.</returns>
    public CompletionList Materialize()
    {
        return new CompletionList(_items);
    }
}
