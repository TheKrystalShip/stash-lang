namespace Stash.Lsp.Completion.Providers;

using System.Collections.Generic;
using Stash.Stdlib;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Provides type-name completion candidates for expressions of the form <c>value is &lt;type&gt;</c>.
/// Emits one candidate per entry in <see cref="StdlibRegistry.TypeDescriptions"/>, matching the
/// monolithic <c>BuildTypeCompletionList</c> implementation exactly.
/// </summary>
/// <remarks>
/// Applies exclusively to <see cref="CompletionMode.AfterIs"/> mode.
/// </remarks>
public sealed class IsTypeCompletionProvider : ICompletionProvider
{
    /// <inheritdoc />
    public bool AppliesTo(CompletionContext ctx) => ctx.Mode == CompletionMode.AfterIs;

    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        foreach (var (name, desc) in StdlibRegistry.TypeDescriptions)
        {
            yield return new CompletionCandidate(
                Label: name,
                Kind: LspCompletionItemKind.TypeParameter,
                Detail: desc.Signature,
                Documentation: desc.Description,
                SourcePriority: 10,
                SourceTag: nameof(IsTypeCompletionProvider));
        }
    }
}
