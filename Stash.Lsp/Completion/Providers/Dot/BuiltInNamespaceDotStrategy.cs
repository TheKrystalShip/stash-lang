namespace Stash.Lsp.Completion.Providers.Dot;

using System.Collections.Generic;
using System.Linq;
using Stash.Stdlib;
using Stash.Lsp.Handlers;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;

/// <summary>
/// Strategy 1 — Dot-access on a built-in namespace (e.g., <c>arr.</c>, <c>fs.</c>).
/// </summary>
/// <remarks>
/// <para>
/// Matches when <c>StdlibRegistry.IsBuiltInNamespace(prefix)</c> is true.
/// Emits functions, data members (properties), constants, and nested enums in that order.
/// </para>
/// <para>
/// <strong>Gating semantics:</strong> This strategy always produces results when the prefix
/// is a known built-in namespace, and the <see cref="DotCompletionProvider"/> performs a
/// <em>hard early-return</em> after this strategy if it emits anything — subsequent
/// strategies are not invoked.
/// </para>
/// <para>
/// <see cref="CompletionCandidate.SourcePriority"/>: <c>100</c> (strategy 1 of 6).
/// </para>
/// </remarks>
public sealed class BuiltInNamespaceDotStrategy : IDotStrategy
{
    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Apply(
        CompletionContext ctx,
        string prefix,
        DotResolutionContext resolution)
    {
        if (!StdlibRegistry.IsBuiltInNamespace(prefix))
            yield break;

        // Functions — LspCompletionItemKind.Function
        foreach (var fn in StdlibRegistry.GetNamespaceMembers(prefix))
        {
            string? docValue = fn.Documentation;
            var throwsSection = ThrowsRenderer.Render(fn.Throws);
            if (throwsSection != null) docValue = (docValue ?? "") + throwsSection;

            yield return new CompletionCandidate(
                Label: fn.Name,
                Kind: LspCompletionItemKind.Function,
                Detail: fn.Detail,
                Documentation: docValue,
                SourcePriority: 100,
                SourceTag: nameof(BuiltInNamespaceDotStrategy));
        }

        // Data members ([StashMember]) — Property kind to distinguish from callable functions
        foreach (var m in StdlibRegistry.GetNamespaceDataMembers(prefix))
        {
            string? docValue = m.Documentation;
            var throwsSection = ThrowsRenderer.Render(m.Throws);
            if (throwsSection != null) docValue = (docValue ?? "") + throwsSection;

            yield return new CompletionCandidate(
                Label: m.Name,
                Kind: LspCompletionItemKind.Property,
                Detail: m.Detail,
                Documentation: docValue,
                SourcePriority: 100,
                SourceTag: nameof(BuiltInNamespaceDotStrategy));
        }

        // Constants
        foreach (var c in StdlibRegistry.GetNamespaceConstants(prefix))
        {
            yield return new CompletionCandidate(
                Label: c.Name,
                Kind: LspCompletionItemKind.Constant,
                Detail: c.Detail,
                SourcePriority: 100,
                SourceTag: nameof(BuiltInNamespaceDotStrategy));
        }

        // Nested enums (e.g., fs.WatchEventType after arr., or task.Status)
        foreach (var e in StdlibRegistry.Enums.Where(e => e.Namespace == prefix))
        {
            yield return new CompletionCandidate(
                Label: e.Name,
                Kind: LspCompletionItemKind.Enum,
                Detail: e.Detail,
                SourcePriority: 100,
                SourceTag: nameof(BuiltInNamespaceDotStrategy));
        }
    }
}
