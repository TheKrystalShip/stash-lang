namespace Stash.Lsp.Completion.Providers.Dot;

using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Strategy 6 — Dot-access on an enum that was exported by a namespace-imported module
/// (e.g., <c>utils.LogLevel.</c> where <c>LogLevel</c> is an enum in the imported <c>utils</c>
/// module).
/// </summary>
/// <remarks>
/// <para>
/// Searches each namespace-imported module for an enum named <paramref name="prefix"/>.
/// When found, emits that enum's members. Stops at the first matching enum (mirrors
/// the monolith's <c>break</c> after the first hit).
/// </para>
/// <para>
/// <strong>Gating semantics:</strong> This strategy is only attempted when the accumulated
/// candidate list from strategies 3–5 is empty. It does not itself short-circuit — the
/// provider accumulates its output and returns at the end.
/// Mirrors monolith lines 789–810: <c>if (items.Count == 0)</c> gate.
/// </para>
/// <para>
/// <see cref="CompletionCandidate.SourcePriority"/>: <c>150</c> (strategy 6 of 6).
/// </para>
/// </remarks>
public sealed class NamespaceImportEnumDotStrategy : IDotStrategy
{
    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Apply(
        CompletionContext ctx,
        string prefix,
        DotResolutionContext resolution)
    {
        if (ctx.Analysis is null) yield break;

        foreach (var (_, modInfo) in ctx.Analysis.NamespaceImports)
        {
            var importedEnum = modInfo.Symbols.All
                .FirstOrDefault(s => s.Name == prefix && s.Kind == StashSymbolKind.Enum);

            if (importedEnum is null) continue;

            foreach (var member in modInfo.Symbols.All
                .Where(s => s.ParentName == prefix && s.Kind == StashSymbolKind.EnumMember))
            {
                yield return new CompletionCandidate(
                    Label: member.Name,
                    Kind: LspCompletionItemKind.EnumMember,
                    Detail: member.Detail,
                    SourcePriority: 150,
                    SourceTag: nameof(NamespaceImportEnumDotStrategy),
                    Accessibility: SymbolAccessibility.BareIdentifier);
            }

            // Stop at the first matching enum — mirrors the monolith's `break`.
            yield break;
        }
    }
}
