namespace Stash.Lsp.Completion.Providers.Dot;

using System.Collections.Generic;
using System.Linq;
using Stash.Analysis;
using Stash.Stdlib;
using Stash.Lsp.Handlers;
using LspCompletionItemKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.CompletionItemKind;
using StashSymbolKind = Stash.Analysis.SymbolKind;

/// <summary>
/// Strategy 4 — UFCS (Uniform Function Call Syntax) dot-access for built-in type receivers.
/// </summary>
/// <remarks>
/// <para>
/// When the resolved receiver type is UFCS-eligible (e.g., <c>"string"</c> or
/// <c>"array"</c>), <see cref="StdlibRegistry.GetUfcsNamespace"/> returns the backing
/// namespace whose functions are offered as method-style completions. The arity-adjusted
/// signature omits the implicit first parameter (the receiver).
/// </para>
/// <para>
/// <strong>Skipped when:</strong> the prefix resolves to a user-defined struct type
/// (<c>prefixDef.Kind == Struct</c>). User-defined structs are not UFCS-eligible.
/// </para>
/// <para>
/// <strong>Gating semantics:</strong> Runs <em>in parallel</em> with strategy 3
/// (StructOrUserEnum) — both contribute to the same accumulated candidate list in the
/// provider, regardless of whether strategy 3 produced anything. The provider does
/// NOT short-circuit between strategies 3 and 4.
/// </para>
/// <para>
/// <see cref="CompletionCandidate.SourcePriority"/>: <c>130</c> (strategy 4 of 6).
/// </para>
/// </remarks>
public sealed class UfcsDotStrategy : IDotStrategy
{
    /// <inheritdoc />
    public IEnumerable<CompletionCandidate> Apply(
        CompletionContext ctx,
        string prefix,
        DotResolutionContext resolution)
    {
        var prefixDef = resolution.PrefixDef;
        var structName = resolution.StructName;

        // Skip UFCS for user-defined struct receivers — they are not UFCS-eligible.
        if (prefixDef?.Kind == StashSymbolKind.Struct)
            yield break;

        var ufcsNamespace = StdlibRegistry.GetUfcsNamespace(structName);
        if (ufcsNamespace is null)
            yield break;

        foreach (var fn in StdlibRegistry.GetNamespaceMembers(ufcsNamespace))
        {
            // Arity-adjusted signature: remove the first parameter (implicit receiver).
            var adjustedParams = fn.Parameters.Skip(1).ToArray();
            var paramParts = adjustedParams.Select(p =>
                p.Type != null ? $"{p.Name}: {p.Type}" : p.Name);
            string sig = $"{fn.Name}({string.Join(", ", paramParts)})";
            if (fn.ReturnType != null)
                sig += $" → {fn.ReturnType}";

            string? docValue = null;
            if (fn.Documentation != null || fn.Throws is { Length: > 0 })
            {
                docValue = (fn.Documentation ?? "") + (ThrowsRenderer.Render(fn.Throws) ?? "");
            }

            yield return new CompletionCandidate(
                Label: fn.Name,
                Kind: LspCompletionItemKind.Method,
                Detail: $"{sig}  (UFCS: {ufcsNamespace}.{fn.Name})",
                Documentation: docValue,
                SourcePriority: 130,
                SourceTag: nameof(UfcsDotStrategy),
                Accessibility: SymbolAccessibility.BareIdentifier);
        }
    }
}
