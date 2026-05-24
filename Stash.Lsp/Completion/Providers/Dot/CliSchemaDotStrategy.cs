namespace Stash.Lsp.Completion.Providers.Dot;

using System.Collections.Generic;
using Stash.Analysis;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Strategy 5 — Dot-access on a variable bound to a <c>cli.parse(schema)</c> result.
/// </summary>
/// <remarks>
/// <para>
/// When the analysis result associates <paramref name="prefix"/> with a CLI schema (i.e.,
/// the variable was bound to <c>cli.parse(cli.schema({...}))</c> with a literal schema
/// argument), the declared field names of that schema are offered as completions.
/// </para>
/// <para>
/// <strong>Gating semantics:</strong> This strategy is only attempted when the accumulated
/// candidate list from strategies 3 and 4 is empty. If it finds a CLI schema and emits
/// candidates, the <see cref="DotCompletionProvider"/> performs an early-return (mirrors
/// monolith lines 766–786: <c>if (items.Count == 0) … if (items.Count > 0) return …</c>).
/// </para>
/// <para>
/// <see cref="CompletionCandidate.SourcePriority"/>: <c>140</c> (strategy 5 of 6).
/// </para>
/// </remarks>
public sealed class CliSchemaDotStrategy : IDotStrategy
{
    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Apply(
        CompletionContext ctx,
        string prefix,
        DotResolutionContext resolution)
    {
        if (ctx.Analysis is null) yield break;

        var cliInfo = ctx.Analysis.CliSchema.TryGet(prefix);
        if (cliInfo is null) yield break;

        foreach (var field in cliInfo.Fields)
        {
            var fieldDetail = field.TypeTag != null
                ? $"cli field: {field.TypeTag}"
                : "cli field";

            yield return new CompletionCandidate(
                Label: field.Name,
                Kind: LspCompletionItemKind.Field,
                Detail: fieldDetail,
                SourcePriority: 140,
                SourceTag: nameof(CliSchemaDotStrategy),
                Accessibility: SymbolAccessibility.BareIdentifier);
        }
    }
}
