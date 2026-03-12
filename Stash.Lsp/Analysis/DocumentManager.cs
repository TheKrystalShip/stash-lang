namespace Stash.Lsp.Analysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

public class DocumentManager
{
    private readonly ConcurrentDictionary<Uri, DocumentState> _documents = new();

    public void Open(Uri uri, string text, int version)
    {
        _documents[uri] = new DocumentState(text, version);
    }

    public void Update(Uri uri, string text, int version)
    {
        _documents[uri] = new DocumentState(text, version);
    }

    public void Close(Uri uri)
    {
        _documents.TryRemove(uri, out _);
    }

    public string? GetText(Uri uri)
    {
        return _documents.TryGetValue(uri, out var state) ? state.Text : null;
    }

    public IEnumerable<Uri> GetOpenDocumentUris() => _documents.Keys;

    public record DocumentState(string Text, int Version);
}
