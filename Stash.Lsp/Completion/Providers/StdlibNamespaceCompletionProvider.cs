namespace Stash.Lsp.Completion.Providers;

using System.Collections.Generic;
using Stash.Stdlib;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Emits one <see cref="CompletionCandidate"/> for every built-in stdlib namespace
/// (from <see cref="StdlibRegistry.NamespaceNames"/>).
/// Applies only in <see cref="CompletionMode.Default"/> mode.
/// <see cref="CompletionCandidate.SourcePriority"/>: <c>30</c>.
/// </summary>
/// <remarks>
/// Running at priority 30 — before <see cref="ScopedSymbolCompletionProvider"/> at 40 — ensures
/// that the sink's first-wins dedup prevents duplicate entries for namespace symbols that
/// <c>SymbolCollector.RegisterBuiltIns</c> also injects into the global scope for hover/goto-def.
/// The <see cref="ScopedSymbolCompletionProvider"/> additionally filters those injected symbols
/// by <c>Origin == BuiltinStdlib</c>, providing belt-and-braces protection against the
/// namespace-duplication bug (commit 7c3f098 regression scenario 2).
/// </remarks>
public sealed class StdlibNamespaceCompletionProvider : ICompletionProvider
{
    /// <inheritdoc />
    public bool AppliesTo(CompletionContext ctx) => ctx.Mode == CompletionMode.Default;

    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        foreach (var ns in StdlibRegistry.NamespaceNames)
        {
            yield return new CompletionCandidate(
                Label: ns,
                Kind: LspCompletionItemKind.Module,
                Detail: $"namespace {ns}",
                SourcePriority: 30,
                SourceTag: "StdlibNamespaceCompletionProvider");
        }
    }
}
