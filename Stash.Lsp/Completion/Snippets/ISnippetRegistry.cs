namespace Stash.Lsp.Completion.Snippets;

using System.Collections.Generic;

/// <summary>
/// Abstraction over a single snippet source (bundled, project-local, or user-global).
/// </summary>
/// <remarks>
/// <para>
/// The interface is designed for future multi-source composition. The v1 implementation
/// (<see cref="BundledSnippetRegistry"/>) is the only concrete type registered in DI.
/// <c>ProjectSnippetRegistry</c> and <c>UserSnippetRegistry</c> will follow without
/// requiring changes to <c>SnippetCompletionProvider</c>.
/// </para>
/// <para>
/// Cross-source precedence is <c>User &gt; Project &gt; Bundled</c>, resolved per
/// <c>(prefix, scope)</c> pair (Decision Log Q4). The provider or a composite registry
/// enforces this ordering.
/// </para>
/// </remarks>
public interface ISnippetRegistry
{
    /// <summary>Source kind for precedence ordering.</summary>
    SnippetSourceKind Kind { get; }

    /// <summary>
    /// Returns a snapshot of the currently-loaded <em>valid</em> snippets.
    /// The returned list is stable for the lifetime of the call; callers must not
    /// assume identity stability across <see cref="Reload"/> boundaries.
    /// </summary>
    IReadOnlyList<Snippet> Snapshot();

    /// <summary>
    /// Errors collected during the last <see cref="Reload"/> pass.
    /// An empty list means all snippets in this source are valid.
    /// Exposed so callers (e.g. the P3 failure-surfacing path) can report them
    /// without re-running validation.
    /// </summary>
    IReadOnlyList<SnippetLoadError> LoadErrors { get; }

    /// <summary>
    /// Re-reads and re-validates the snippet source.
    /// In v1, called exactly once at construction time by <see cref="BundledSnippetRegistry"/>.
    /// File-watching implementations will call this on source-file changes.
    /// </summary>
    void Reload();
}
