namespace Stash.Lsp.Completion.Providers.Dot;

using System.Collections.Generic;

/// <summary>
/// One branch in the ordered dot-completion resolution pipeline.
/// Strategies are invoked in a fixed order by <see cref="DotCompletionProvider"/>;
/// each strategy contributes candidates for a specific category of dot-access prefix.
/// </summary>
/// <remarks>
/// <para>
/// Ordering is load-bearing — it mirrors the fall-through branches in the monolith's
/// <c>HandleDotCompletion</c> method. Do not reorder without consulting the Decision Log
/// in <c>.kanban/2-in-progress/lsp-completion-providers/brief.md</c>.
/// </para>
/// <para>
/// Some strategies short-circuit the pipeline by returning a non-empty list that causes
/// subsequent strategies to be skipped. The specific gating semantics for each strategy
/// are documented on the concrete implementation.
/// </para>
/// </remarks>
public interface IDotStrategy
{
    /// <summary>
    /// Enumerates the candidates this strategy contributes for the given <paramref name="ctx"/>
    /// and resolved dot <paramref name="prefix"/>.
    /// </summary>
    /// <param name="ctx">The completion context for the current request.</param>
    /// <param name="prefix">The identifier before the dot (e.g., <c>"arr"</c> for <c>arr.</c>).</param>
    /// <param name="resolution">Shared prefix-resolution data computed once by <see cref="DotCompletionProvider"/>.</param>
    /// <returns>
    /// A sequence of candidates, possibly empty.
    /// Returning a non-empty sequence may gate subsequent strategies — see the provider's
    /// <c>Provide</c> method for the exact gating policy.
    /// </returns>
    IEnumerable<CompletionCandidate> Apply(
        CompletionContext ctx,
        string prefix,
        DotResolutionContext resolution);
}
