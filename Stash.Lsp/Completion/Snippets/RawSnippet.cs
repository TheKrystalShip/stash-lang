namespace Stash.Lsp.Completion.Snippets;

using System.Text.Json.Serialization;

/// <summary>
/// JSON deserialization shape for one entry in a snippet source file.
/// Mirrors the VS Code snippet schema with an optional <c>scope</c> extension.
/// </summary>
/// <remarks>
/// The object-key (display name) is injected separately by <c>BundledSnippetRegistry</c>
/// when iterating the top-level JSON object. <c>body</c> may be a single string
/// or an array of strings; the registry joins array entries with <c>"\n"</c> before
/// passing to <c>SnippetValidator</c>.
/// </remarks>
public sealed class RawSnippet
{
    /// <summary>Completion trigger. Required; validated non-empty with identifier shape.</summary>
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = "";

    /// <summary>
    /// Snippet body. May be a single string or an array of strings.
    /// Deserialized as <see cref="System.Text.Json.JsonElement"/> so both forms
    /// can be handled uniformly; the registry resolves to a single string before validation.
    /// </summary>
    [JsonPropertyName("body")]
    public System.Text.Json.JsonElement Body { get; set; }

    /// <summary>Optional documentation. Absent → <see langword="null"/>.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Optional scope string: <c>"any"</c> (default), <c>"top-level"</c>,
    /// <c>"fn-body"</c>, <c>"loop-body"</c>. Case-insensitive.
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}
