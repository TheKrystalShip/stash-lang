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
/// <see cref="SourcePriority"/> is <c>1000</c> — deliberately deprioritised far below every
/// other Default-mode provider (keywords=10, stdlib fns=20, stdlib namespaces=30, scoped
/// symbols=40) so snippets never get in the way of normal typing. Users see them only after
/// scrolling past the higher-signal candidates. This matches the project policy that
/// completion should not surprise users with workflow templates when they know what they
/// want to type. (Decision Log: user feedback after P2.)
/// </para>
/// <para>
/// In P2, every bundled snippet has <c>Scope = Any</c>, so no cursor-position gating is
/// applied. P4 introduces <c>SnippetContext.Classify</c> to restrict scope-gated snippets.
/// </para>
/// <para>
/// <b>Note on keyword-prefix collisions:</b> snippet prefixes must NOT shadow Stash
/// keywords (<c>fn</c>, <c>let</c>, <c>for</c>, <c>if</c>, <c>struct</c>, etc.). The bundled
/// seed renames them to non-keyword forms (<c>fnd</c>, <c>letv</c>, <c>fore</c>, <c>ifc</c>,
/// <c>strc</c>) so all 7 snippets actually surface. Project / user snippet sources added in
/// later phases inherit this rule via <c>SnippetValidator</c> — see Decision Log Q1.
/// </para>
/// </remarks>
public sealed class SnippetCompletionProvider : ICompletionProvider
{
    /// <summary>
    /// Priority value for snippet candidates. Set deliberately high (lower precedence) at
    /// <c>1000</c> so snippets land below every other Default-mode provider — users have to
    /// scroll past keywords / stdlib / scoped symbols to find them.
    /// </summary>
    public const int SourcePriority = 1000;

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
        var cursorScope = SnippetContext.Classify(ctx.Analysis?.Symbols, ctx.LspLine, ctx.LspColumn);

        foreach (var snippet in _registry.Snapshot())
        {
            if (!SnippetContext.Matches(snippet.Scope, cursorScope))
                continue;

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
