namespace Stash.Lsp.Completion.Snippets;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

/// <summary>
/// Loads and validates the set of snippets embedded in the LSP binary as
/// <c>Stash.Lsp.Completion.Snippets.bundled.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The constructor invokes <see cref="Reload"/> immediately so snippets are ready
/// before the first completion request. No exception escapes the constructor — if
/// every bundled snippet is invalid the registry returns an empty <see cref="Snapshot"/>
/// and exposes all errors via <see cref="LoadErrors"/> for the P3 failure-surfacing path.
/// </para>
/// <para>
/// The embedded resource name is: <c>Stash.Lsp.Completion.Snippets.bundled.json</c>.
/// The <c>.csproj</c> registers it with
/// <c>&lt;EmbeddedResource Include="Completion/Snippets/bundled.json" /&gt;</c>.
/// </para>
/// </remarks>
public sealed class BundledSnippetRegistry : ISnippetRegistry
{
    private const string ResourceName = "Stash.Lsp.Completion.Snippets.bundled.json";

    private IReadOnlyList<Snippet> _snapshot = Array.Empty<Snippet>();
    private IReadOnlyList<SnippetLoadError> _loadErrors = Array.Empty<SnippetLoadError>();

    /// <summary>
    /// Initialises the registry and immediately loads and validates the bundled snippet set.
    /// </summary>
    public BundledSnippetRegistry()
    {
        Reload();
    }

    /// <inheritdoc />
    public SnippetSourceKind Kind => SnippetSourceKind.Bundled;

    /// <inheritdoc />
    public IReadOnlyList<Snippet> Snapshot() => _snapshot;

    /// <inheritdoc />
    public IReadOnlyList<SnippetLoadError> LoadErrors => _loadErrors;

    /// <inheritdoc />
    public void Reload()
    {
        try
        {
            var rawEntries = LoadRawEntries();
            var result = SnippetValidator.Validate(rawEntries, SnippetSourceKind.Bundled);
            _snapshot = result.Valid;
            _loadErrors = result.Errors;
        }
        catch (Exception ex)
        {
            // Surface the load failure as a synthetic error; never throw out of Reload.
            _snapshot = Array.Empty<Snippet>();
            _loadErrors = new[]
            {
                new SnippetLoadError(
                    "bundled",
                    "bundled",
                    $"failed to load bundled snippet resource: {ex.Message}"),
            };
        }
    }

    private static IEnumerable<(string DisplayName, RawSnippet Raw)> LoadRawEntries()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. " +
                "Ensure Stash.Lsp.csproj contains: " +
                "<EmbeddedResource Include=\"Completion/Snippets/bundled.json\" />");

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var json = reader.ReadToEnd();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("bundled.json root must be a JSON object.");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Yield after the document is consumed (materialise to list first).
        var entries = new List<(string, RawSnippet)>();
        foreach (var property in root.EnumerateObject())
        {
            var raw = JsonSerializer.Deserialize<RawSnippet>(property.Value.GetRawText(), options)
                      ?? new RawSnippet();
            entries.Add((property.Name, raw));
        }

        return entries;
    }
}
