namespace Stash.Lsp.Completion.Providers;

using System.Collections.Generic;
using Stash.Analysis;
using Stash.Lsp.Handlers;
using Stash.Stdlib;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Emits <see cref="CompletionCandidate"/> instances for user-defined and user-visible
/// symbols at the cursor position, obtained via
/// <see cref="ScopeTree.GetVisibleSymbols(int, int)"/>.
/// Applies only in <see cref="CompletionMode.Default"/> mode.
/// <see cref="CompletionCandidate.SourcePriority"/>: <c>40</c> — lowest priority in the Default pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Two categories of symbols are filtered out before emitting candidates:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       Symbols with <see cref="SymbolAccessibility.RequiresQualification"/> (struct fields,
///       methods, enum members). These are always accessed via dot notation and are never
///       valid as bare identifiers. The <see cref="CompletionItemSink"/> provides a
///       defence-in-depth secondary filter for this category.
///     </description>
///   </item>
///   <item>
///     <description>
///       Symbols with <see cref="SymbolOrigin.BuiltinStdlib"/>. These are injected by
///       <c>SymbolCollector.RegisterBuiltIns</c> so that hover/goto-def can resolve them,
///       but they are already surfaced with richer detail by
///       <see cref="StdlibFunctionCompletionProvider"/> (priority 20) and
///       <see cref="StdlibNamespaceCompletionProvider"/> (priority 30). Emitting them here
///       again would cause duplicates — this filter is the architectural fix for the
///       namespace-duplication regression (commit 7c3f098, scenario 2).
///     </description>
///   </item>
/// </list>
/// <para>
///   Documentation for user functions with <c>@throws</c> entries is rendered via
///   <see cref="CompletionInterop.AdaptThrows"/> and <see cref="ThrowsRenderer.Render"/>.
/// </para>
/// </remarks>
public sealed class ScopedSymbolCompletionProvider : ICompletionProvider
{
    /// <inheritdoc />
    public bool AppliesTo(CompletionContext ctx) =>
        ctx.Mode == CompletionMode.Default && ctx.Analysis != null;

    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Provide(CompletionContext ctx)
    {
        if (ctx.Analysis is null) yield break;

        // Convert 0-based LSP coordinates to 1-based for GetVisibleSymbols.
        var symbols = ctx.Analysis.Symbols.GetVisibleSymbols(ctx.LspLine + 1, ctx.LspColumn + 1);

        foreach (var sym in symbols)
        {
            // Member-style symbols are never valid as bare identifiers.
            if (sym.Accessibility == SymbolAccessibility.RequiresQualification)
                continue;

            // Stdlib-injected symbols are covered with richer detail by the providers
            // at priority 20 and 30; skip them here to prevent duplicates.
            if (sym.Origin == SymbolOrigin.BuiltinStdlib)
                continue;

            // Render @throws entries into the documentation block.
            string? doc = sym.Documentation;
            if (sym.Throws != null)
            {
                var adapted = CompletionInterop.AdaptThrows(sym.Throws);
                var throwsSection = ThrowsRenderer.Render(adapted);
                if (throwsSection != null)
                    doc = (doc ?? "") + throwsSection;
            }

            yield return new CompletionCandidate(
                Label: sym.Name,
                Kind: CompletionInterop.MapCompletionKind(sym.Kind),
                Detail: sym.Detail,
                Documentation: doc,
                SourcePriority: 40,
                SourceTag: nameof(ScopedSymbolCompletionProvider),
                Accessibility: sym.Accessibility);
        }
    }
}
