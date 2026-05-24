namespace Stash.Lsp.Completion.Snippets;

using Stash.Analysis;

/// <summary>
/// Classifies the snippet scope at a given cursor position using the existing
/// <see cref="ScopeTree"/> — no new analysis pass required.
/// </summary>
/// <remarks>
/// <para>
/// Scope classification walks from the innermost scope enclosing the cursor position
/// upward via <see cref="Scope.Parent"/>, looking for the nearest
/// <see cref="ScopeKind.Function"/> or <see cref="ScopeKind.Loop"/> ancestor.
/// <see cref="ScopeKind.Block"/> scopes are traversed through — a block inside a
/// function still classifies as <see cref="SnippetScope.FnBody"/>.
/// </para>
/// <para>
/// A null <see cref="ScopeTree"/> (no cached analysis) returns <see cref="SnippetScope.Any"/>
/// so snippets are not hidden during initial document load.
/// </para>
/// </remarks>
public static class SnippetContext
{
    /// <summary>
    /// Classifies the cursor position <paramref name="line"/>, <paramref name="column"/>
    /// into one of the <see cref="SnippetScope"/> vocabulary values.
    /// </summary>
    /// <param name="tree">
    /// The scope tree from the cached analysis result, or <see langword="null"/>
    /// when no analysis is available.
    /// </param>
    /// <param name="line">0-based LSP line.</param>
    /// <param name="column">0-based LSP column.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description><see cref="SnippetScope.LoopBody"/> — innermost non-block ancestor is a Loop.</description></item>
    ///   <item><description><see cref="SnippetScope.FnBody"/> — innermost non-block ancestor is a Function.</description></item>
    ///   <item><description><see cref="SnippetScope.TopLevel"/> — cursor is in Global scope (no Function/Loop ancestor).</description></item>
    ///   <item><description><see cref="SnippetScope.Any"/> — returned only when <paramref name="tree"/> is null.</description></item>
    /// </list>
    /// </returns>
    public static SnippetScope Classify(ScopeTree? tree, int line, int column)
    {
        if (tree is null)
            return SnippetScope.Any;

        // The analysis engine uses 1-based line/column internally; LSP uses 0-based.
        var scope = tree.FindScopeAt(line + 1, column + 1);

        while (scope is not null)
        {
            if (scope.Kind == ScopeKind.Loop)
                return SnippetScope.LoopBody;

            if (scope.Kind == ScopeKind.Function)
                return SnippetScope.FnBody;

            if (scope.Kind == ScopeKind.Global)
                return SnippetScope.TopLevel;

            // ScopeKind.Block — walk up to the enclosing scope
            scope = scope.Parent;
        }

        // Fallback: shouldn't be reached when tree is non-null (Global scope is root).
        return SnippetScope.TopLevel;
    }

    /// <summary>
    /// Returns <see langword="true"/> when a snippet declared with <paramref name="snippetScope"/>
    /// should fire at a cursor that is classified as <paramref name="cursorScope"/>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description><see cref="SnippetScope.Any"/> — fires everywhere.</description></item>
    ///   <item><description>Any other value — fires only when it equals <paramref name="cursorScope"/>.</description></item>
    /// </list>
    /// </remarks>
    public static bool Matches(SnippetScope snippetScope, SnippetScope cursorScope)
        => snippetScope == SnippetScope.Any || snippetScope == cursorScope;
}
