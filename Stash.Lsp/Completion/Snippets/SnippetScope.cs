namespace Stash.Lsp.Completion.Snippets;

/// <summary>
/// Scope vocabulary for snippet context gating.
/// Derived from <c>ScopeKind</c> values; no new analysis pass required.
/// </summary>
/// <remarks>
/// Only <see cref="Any"/> is used in P2. Scope-gated firing (<see cref="TopLevel"/>,
/// <see cref="FnBody"/>, <see cref="LoopBody"/>) is introduced in P4 via
/// <c>SnippetContext.Classify</c> + <c>SnippetContext.Matches</c>.
/// </remarks>
public enum SnippetScope
{
    /// <summary>Fires in every Default-mode cursor position.</summary>
    Any,

    /// <summary>Fires only when the cursor's immediate enclosing scope is <c>ScopeKind.Global</c>.</summary>
    TopLevel,

    /// <summary>Fires when the cursor is inside any transitive <c>ScopeKind.Function</c> ancestor.</summary>
    FnBody,

    /// <summary>Fires when the cursor is inside any transitive <c>ScopeKind.Loop</c> ancestor.</summary>
    LoopBody,
}
