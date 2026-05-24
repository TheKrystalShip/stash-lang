namespace Stash.Lsp.Completion.Providers;

using System.Collections.Generic;
using Stash.Lsp.Completion.Snippets;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using LspInsertTextFormat = OmniSharp.Extensions.LanguageServer.Protocol.Models.InsertTextFormat;

/// <summary>
/// Emits snippet completion candidates for all valid snippets in the registered
/// <see cref="ISnippetRegistry"/> instances.
/// Applies only in <see cref="CompletionMode.Default"/> mode.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SourcePriority"/> is <c>50</c> — strictly greater than
/// <see cref="ScopedSymbolCompletionProvider"/>'s priority of <c>40</c>. In the sink's
/// first-wins label dedup race this means a user-scoped symbol that shares a label with
/// a snippet prefix wins (the symbol provider runs earlier in the pipeline, lower number =
/// higher precedence). Decision Log Q9.
/// </para>
/// <para>
/// In P2, every bundled snippet has <c>Scope = Any</c>, so no cursor-position gating is
/// applied. P4 introduces <c>SnippetContext.Classify</c> to restrict scope-gated snippets.
/// </para>
/// <para>
/// <b>Note on keyword-prefix collisions:</b> several bundled snippets share a prefix with
/// a Stash keyword (e.g. <c>fn</c>, <c>let</c>, <c>for</c>, <c>if</c>, <c>struct</c>).
/// Because <see cref="KeywordCompletionProvider"/> runs first in the Default pipeline
/// (priority 10) and the sink does label-based first-wins dedup, these snippet candidates
/// are silently dropped. Only non-keyword prefixes (e.g. <c>fori</c>, <c>ife</c>) surface
/// in Default mode. This is intentional per Decision Log Q9: user-visible symbols — and
/// keywords — shadow snippets sharing their label. Full snippet surfacing for keyword-prefix
/// snippets requires P4's context-aware pipeline ordering or a separate snippet-only
/// resolution path.
/// </para>
/// </remarks>
public sealed class SnippetCompletionProvider : ICompletionProvider
{
    /// <summary>
    /// Priority value for snippet candidates.
    /// Strictly greater than <see cref="ScopedSymbolCompletionProvider"/>'s priority (40),
    /// so user symbols win the dedup race over same-prefixed snippets.
    /// </summary>
    public const int SourcePriority = 50;

    private readonly ISnippetRegistry _registry;

    /// <summary>
    /// Initialises the provider with the given snippet registry.
    /// </summary>
    public SnippetCompletionProvider(ISnippetRegistry registry)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public bool AppliesTo(CompletionContext ctx) => ctx.Mode == CompletionMode.Default;

    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        // P2: no cursor-scope gating — all snippets have Scope=Any.
        // P4 will add: var cursorScope = SnippetContext.Classify(ctx.Analysis.ScopeTree, ctx.LspLine, ctx.LspColumn);
        foreach (var snippet in _registry.Snapshot())
        {
            yield return new CompletionCandidate(
                Label: snippet.Prefix,
                Kind: LspCompletionItemKind.Snippet,
                Detail: snippet.DisplayName,
                Documentation: snippet.Description,
                SourcePriority: SourcePriority,
                SourceTag: nameof(SnippetCompletionProvider),
                InsertText: snippet.Body,
                InsertTextFormat: LspInsertTextFormat.Snippet);
        }
    }
}
