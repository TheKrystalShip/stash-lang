namespace Stash.Lsp.Completion.Providers;

using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Lsp.Completion.Providers.Dot;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Handles dot-mode completions (cursor immediately after a <c>.</c>) by running six
/// ordered strategies, each targeting a specific category of dot-access receiver.
/// </summary>
/// <remarks>
/// <para>
/// The six strategies are invoked in a fixed, load-bearing order that mirrors the
/// <c>HandleDotCompletion</c> branch sequence in the pre-refactor monolith:
/// </para>
/// <list type="number">
///   <item><see cref="BuiltInNamespaceDotStrategy"/> — built-in namespace prefix (e.g., <c>arr.</c>). Hard early-return on match.</item>
///   <item><see cref="ImportAliasDotStrategy"/> — namespace-import alias (e.g., <c>mymod.</c>). Hard early-return on match.</item>
///   <item><see cref="StructOrUserEnumDotStrategy"/> — variable/struct-instance/enum (e.g., <c>point.</c>). Runs in parallel with strategy 4.</item>
///   <item><see cref="UfcsDotStrategy"/> — UFCS-eligible receiver (e.g., <c>"hello".</c>). Skipped for user-defined struct receivers; runs in parallel with strategy 3.</item>
///   <item><see cref="CliSchemaDotStrategy"/> — CLI parse-result variable (e.g., <c>parsed.</c>). Gated: only if strategies 3+4 emitted nothing. Early-return on match.</item>
///   <item><see cref="NamespaceImportEnumDotStrategy"/> — enum from a namespace-imported module (e.g., <c>utils.LogLevel.</c>). Gated: only if strategies 3–5 emitted nothing.</item>
/// </list>
/// <para>
/// Prefix resolution (visible-symbol lookup, narrowed-type resolution) is done once per
/// request and distributed to strategies via <see cref="DotResolutionContext"/>.
/// </para>
/// <para>
/// Strategies are sub-objects rather than top-level providers because their ordering is
/// load-bearing and they share the same prefix-resolution work. Making them peer providers
/// would duplicate that work or force shared mutable state (Decision Log, 2026-05-23).
/// </para>
/// </remarks>
public sealed class DotCompletionProvider : ICompletionProvider
{
    private static readonly IDotStrategy[] Strategies =
    [
        new BuiltInNamespaceDotStrategy(),
        new ImportAliasDotStrategy(),
        new StructOrUserEnumDotStrategy(),
        new UfcsDotStrategy(),
        new CliSchemaDotStrategy(),
        new NamespaceImportEnumDotStrategy(),
    ];

    /// <inheritdoc />
    public bool AppliesTo(CompletionContext ctx) =>
        ctx.Mode == CompletionMode.Dot && ctx.DotPrefix is not null;

    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        if (ctx.DotPrefix is null) yield break;

        var prefix = ctx.DotPrefix;
        var resolution = ResolvePrefix(ctx, prefix);

        // ── Strategy 1: BuiltInNamespaceDotStrategy — hard early-return ──────
        var s1 = Strategies[0].Apply(ctx, prefix, resolution).ToList();
        if (s1.Count > 0)
        {
            foreach (var c in s1) yield return c;
            yield break;
        }

        // ── Strategy 2: ImportAliasDotStrategy — hard early-return ───────────
        var s2 = Strategies[1].Apply(ctx, prefix, resolution).ToList();
        if (s2.Count > 0)
        {
            foreach (var c in s2) yield return c;
            yield break;
        }

        // ── Strategies 3+4: StructOrUserEnum + UFCS — run in parallel ────────
        // Both contribute to the accumulated list regardless of each other's output.
        var accumulated = new List<CompletionCandidate>();
        accumulated.AddRange(Strategies[2].Apply(ctx, prefix, resolution));
        accumulated.AddRange(Strategies[3].Apply(ctx, prefix, resolution));

        // ── Strategy 5: CliSchemaDotStrategy — gated on empty accumulated ────
        if (accumulated.Count == 0)
        {
            var s5 = Strategies[4].Apply(ctx, prefix, resolution).ToList();
            if (s5.Count > 0)
            {
                foreach (var c in s5) yield return c;
                yield break;
            }
        }

        // ── Strategy 6: NamespaceImportEnumDotStrategy — gated on empty accumulated ──
        if (accumulated.Count == 0)
        {
            accumulated.AddRange(Strategies[5].Apply(ctx, prefix, resolution));
        }

        foreach (var c in accumulated) yield return c;
    }

    /// <summary>
    /// Resolves the dot prefix against the visible symbol table to produce a
    /// <see cref="DotResolutionContext"/> shared across strategies 3–6.
    /// </summary>
    /// <param name="ctx">The completion context.</param>
    /// <param name="prefix">The identifier before the dot.</param>
    /// <returns>A <see cref="DotResolutionContext"/> with the resolved symbol and struct name.</returns>
    private static DotResolutionContext ResolvePrefix(CompletionContext ctx, string prefix)
    {
        if (ctx.Analysis is null)
            return new DotResolutionContext(PrefixDef: null, StructName: prefix);

        // Convert 0-based LSP coordinates to 1-based for GetVisibleSymbols.
        var symbols = ctx.Analysis.Symbols.GetVisibleSymbols(ctx.LspLine + 1, ctx.LspColumn + 1);
        var prefixDef = symbols.FirstOrDefault(s => s.Name == prefix);

        // If the prefix is a variable/parameter/loop-var, resolve its struct/type name
        // via narrowed type hint or declared type hint.
        var structName = prefix;
        if (prefixDef != null &&
            (prefixDef.Kind == StashSymbolKind.Variable ||
             prefixDef.Kind == StashSymbolKind.Constant ||
             prefixDef.Kind == StashSymbolKind.Parameter ||
             prefixDef.Kind == StashSymbolKind.LoopVariable))
        {
            var narrowedType = ctx.Analysis.Symbols.GetNarrowedTypeHint(prefix, ctx.LspLine + 1, ctx.LspColumn + 1);
            structName = narrowedType ?? prefixDef.TypeHint ?? prefix;
        }

        return new DotResolutionContext(PrefixDef: prefixDef, StructName: structName);
    }
}
