namespace Stash.Lsp.Completion.Providers.Dot;

using System.Collections.Generic;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Strategy 2 — Dot-access on a namespace-import alias (e.g., <c>mymod.</c> where
/// <c>import * from "mymod"</c> was declared in the current file).
/// </summary>
/// <remarks>
/// <para>
/// Matches when <c>result.NamespaceImports[prefix]</c> resolves to a <c>ModuleInfo</c>.
/// Emits the module's top-level exported symbols using <see cref="ScopeTree.GetTopLevel"/>.
/// </para>
/// <para>
/// <strong>Gating semantics:</strong> This strategy short-circuits the pipeline on a
/// match — the <see cref="DotCompletionProvider"/> performs a <em>hard early-return</em>
/// after this strategy if it emits anything. Mirrors the monolith's
/// <c>return new CompletionList(items);</c> at line 621.
/// </para>
/// <para>
/// <see cref="CompletionCandidate.SourcePriority"/>: <c>110</c> (strategy 2 of 6).
/// </para>
/// </remarks>
public sealed class ImportAliasDotStrategy : IDotStrategy
{
    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Apply(
        CompletionContext ctx,
        string prefix,
        DotResolutionContext resolution)
    {
        if (ctx.Analysis is null) yield break;
        if (!ctx.Analysis.NamespaceImports.TryGetValue(prefix, out var moduleInfo)) yield break;

        foreach (var sym in moduleInfo.Symbols.GetTopLevel())
        {
            yield return new CompletionCandidate(
                Label: sym.Name,
                Kind: CompletionInterop.MapCompletionKind(sym.Kind),
                Detail: sym.Detail,
                SourcePriority: 110,
                SourceTag: nameof(ImportAliasDotStrategy));
        }
    }
}
