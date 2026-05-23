namespace Stash.Lsp.Completion;

using System.Collections.Generic;

/// <summary>
/// Represents a single source of completion candidates for a specific cursor context.
/// Implementations are stateless and safe to share across concurrent requests.
/// One instance per provider class is registered per server lifetime.
/// </summary>
public interface ICompletionProvider
{
    /// <summary>
    /// Returns <see langword="true"/> if this provider can contribute candidates
    /// for the given <paramref name="ctx"/>. Evaluated before
    /// <see cref="Provide"/> to skip providers that do not apply — implementations
    /// should be cheap (no I/O, no analysis queries).
    /// </summary>
    /// <param name="ctx">The completion context for the current request.</param>
    /// <returns>
    /// <see langword="true"/> if the provider has candidates to offer; otherwise <see langword="false"/>.
    /// </returns>
    bool AppliesTo(CompletionContext ctx);

    /// <summary>
    /// Enumerates the completion candidates this provider contributes for the given
    /// <paramref name="ctx"/>. Called only when <see cref="AppliesTo"/> returns
    /// <see langword="true"/>. May yield an empty sequence.
    /// </summary>
    /// <param name="ctx">The completion context for the current request.</param>
    /// <returns>A (possibly empty) sequence of candidates.</returns>
    IEnumerable<CompletionCandidate> Provide(CompletionContext ctx);
}
