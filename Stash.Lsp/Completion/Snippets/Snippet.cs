namespace Stash.Lsp.Completion.Snippets;

/// <summary>
/// A validated, canonical snippet ready for use by <c>SnippetCompletionProvider</c>.
/// </summary>
/// <param name="Id">
/// Unique diagnostic key: <c>"&lt;source&gt;:&lt;prefix&gt;:&lt;scope&gt;"</c>.
/// Constructed by <c>SnippetValidator</c> per Decision Log Q3.
/// </param>
/// <param name="Prefix">Completion trigger (e.g. <c>"for"</c>). Validated non-empty, matches identifier shape.</param>
/// <param name="DisplayName">Human-readable label shown alongside the prefix in the IDE dropdown.</param>
/// <param name="Body">Snippet body with LSP tab-stop placeholders (<c>$1</c>, <c>${2:name}</c>, etc.).</param>
/// <param name="Description">Optional documentation shown in the detail panel, or <see langword="null"/>.</param>
/// <param name="Scope">Scope gate for P4 context-aware filtering. All bundled snippets use <see cref="SnippetScope.Any"/> in P2.</param>
/// <param name="Source">Which registry contributed this snippet.</param>
public sealed record Snippet(
    string Id,
    string Prefix,
    string DisplayName,
    string Body,
    string? Description,
    SnippetScope Scope,
    SnippetSourceKind Source);
