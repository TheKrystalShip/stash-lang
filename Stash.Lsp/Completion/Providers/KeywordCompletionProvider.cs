namespace Stash.Lsp.Completion.Providers;

using System.Collections.Generic;
using Stash.Lexing;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Emits one <see cref="CompletionCandidate"/> for every Stash keyword.
/// Applies only in <see cref="CompletionMode.Default"/> mode.
/// <see cref="CompletionCandidate.SourcePriority"/>: <c>10</c> — highest priority in the Default pipeline.
/// </summary>
public sealed class KeywordCompletionProvider : ICompletionProvider
{
    /// <inheritdoc />
    public bool AppliesTo(CompletionContext ctx) => ctx.Mode == CompletionMode.Default;

    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        foreach (var kw in Keywords.All)
        {
            yield return new CompletionCandidate(
                Label: kw,
                Kind: LspCompletionItemKind.Keyword,
                Detail: "keyword",
                SourcePriority: 10,
                SourceTag: "KeywordCompletionProvider");
        }
    }
}
