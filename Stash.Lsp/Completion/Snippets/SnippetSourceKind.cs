namespace Stash.Lsp.Completion.Snippets;

/// <summary>
/// Identifies which registry contributed a snippet.
/// Used for cross-source precedence resolution: higher ordinal value = higher precedence.
/// </summary>
public enum SnippetSourceKind
{
    /// <summary>Embedded in the LSP binary; lowest precedence.</summary>
    Bundled = 0,

    /// <summary>Per-workspace <c>stash-snippets.json</c>; overrides bundled.</summary>
    Project = 1,

    /// <summary>User-global <c>~/.stash/snippets.json</c>; highest precedence.</summary>
    User = 2,
}
