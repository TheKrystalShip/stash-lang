namespace Stash.Lsp.Analysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

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

    public string? ApplyIncrementalChanges(Uri uri, int version,
        IEnumerable<TextDocumentContentChangeEvent> changes)
    {
        if (!_documents.TryGetValue(uri, out var state))
        {
            return null;
        }

        var text = state.Text;

        foreach (var change in changes)
        {
            if (change.Range != null)
            {
                // Incremental change — apply the range edit
                int startOffset = GetOffset(text, (int)change.Range.Start.Line, (int)change.Range.Start.Character);
                int endOffset = GetOffset(text, (int)change.Range.End.Line, (int)change.Range.End.Character);
                text = string.Concat(text.AsSpan(0, startOffset), change.Text, text.AsSpan(endOffset));
            }
            else
            {
                // Full replacement
                text = change.Text;
            }
        }

        _documents[uri] = new DocumentState(text, version);
        return text;
    }

    private static int GetOffset(string text, int line, int character)
    {
        int offset = 0;
        int currentLine = 0;

        while (currentLine < line && offset < text.Length)
        {
            if (text[offset] == '\n')
            {
                currentLine++;
            }

            offset++;
        }

        return Math.Min(offset + character, text.Length);
    }

    public record DocumentState(string Text, int Version);
}
