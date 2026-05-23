namespace Stash.Lsp.Completion.Providers;

using System.Collections.Generic;
using Stash.Stdlib;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Emits one <see cref="CompletionCandidate"/> for every built-in global stdlib function
/// (i.e., functions in the global namespace from <see cref="StdlibRegistry.Functions"/>).
/// Applies only in <see cref="CompletionMode.Default"/> mode.
/// <see cref="CompletionCandidate.SourcePriority"/>: <c>20</c>.
/// </summary>
public sealed class StdlibFunctionCompletionProvider : ICompletionProvider
{
    /// <inheritdoc />
    public bool AppliesTo(CompletionContext ctx) => ctx.Mode == CompletionMode.Default;

    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        foreach (var fn in StdlibRegistry.Functions)
        {
            yield return new CompletionCandidate(
                Label: fn.Name,
                Kind: LspCompletionItemKind.Function,
                Detail: fn.Detail,
                Documentation: fn.Documentation,
                SourcePriority: 20,
                SourceTag: "StdlibFunctionCompletionProvider");
        }
    }
}
