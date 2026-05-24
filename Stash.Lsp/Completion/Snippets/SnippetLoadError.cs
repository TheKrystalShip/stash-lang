namespace Stash.Lsp.Completion.Snippets;

/// <summary>
/// Describes a validation failure for a single snippet entry.
/// Collected by <c>SnippetValidator</c> and exposed via <see cref="ISnippetRegistry.LoadErrors"/>.
/// </summary>
/// <param name="SnippetIdOrName">
/// The snippet's composite ID (<c>"&lt;source&gt;:&lt;prefix&gt;:&lt;scope&gt;"</c>) when available,
/// or the raw display name when ID construction failed (e.g. missing prefix).
/// </param>
/// <param name="SourceLocation">
/// Human-readable description of where the snippet came from (e.g. <c>"bundled"</c>
/// or a filesystem path for future project/user sources).
/// </param>
/// <param name="Reason">
/// Short message naming the violated rule (e.g. <c>"prefix is empty"</c>,
/// <c>"body failed to parse: unexpected token 'in'"</c>).
/// </param>
public sealed record SnippetLoadError(
    string SnippetIdOrName,
    string SourceLocation,
    string Reason);
