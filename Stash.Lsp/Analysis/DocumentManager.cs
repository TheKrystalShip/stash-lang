namespace Stash.Lsp.Analysis;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Thread-safe in-memory store for the text content of all open Stash documents.
/// </summary>
/// <remarks>
/// <para>
/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by document <see cref="Uri"/>
/// to allow safe concurrent reads and writes from multiple OmniSharp handler threads.
/// Each entry is an immutable <see cref="DocumentState"/> record pairing the current
/// source text with its LSP version number.
/// </para>
/// <para>
/// Registered as a singleton by <see cref="StashLanguageServer"/> and injected into
/// <c>TextDocumentSyncHandler</c>, which calls <see cref="Open"/>, <see cref="Update"/>,
/// <see cref="ApplyIncrementalChanges"/>, and <see cref="Close"/> in response to LSP
/// <c>textDocument/didOpen</c>, <c>didChange</c>, and <c>didClose</c> notifications.
/// </para>
/// </remarks>
public class DocumentManager
{
    private readonly ILogger<DocumentManager> _logger;

    /// <summary>Backing store mapping each open document URI to its current text and version.</summary>
    private readonly ConcurrentDictionary<Uri, DocumentState> _documents = new();

    public DocumentManager(ILogger<DocumentManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Records a newly opened document and stores its initial text and LSP version.
    /// </summary>
    /// <param name="uri">The document URI received in the <c>textDocument/didOpen</c> notification.</param>
    /// <param name="text">The full initial text of the document.</param>
    /// <param name="version">The LSP document version number.</param>
    public void Open(Uri uri, string text, int version)
    {
        _documents[uri] = new DocumentState(text, version);
        _logger.LogDebug("Document opened in store: {Uri}", uri);
    }

    /// <summary>
    /// Replaces the stored text and version for an already-open document with a full replacement text.
    /// </summary>
    /// <param name="uri">The document URI.</param>
    /// <param name="text">The new full text of the document.</param>
    /// <param name="version">The new LSP document version number.</param>
    public void Update(Uri uri, string text, int version)
    {
        _documents[uri] = new DocumentState(text, version);
        _logger.LogTrace("Document updated in store: {Uri}", uri);
    }

    /// <summary>
    /// Removes the document from the store when the editor closes it.
    /// </summary>
    /// <param name="uri">The document URI received in the <c>textDocument/didClose</c> notification.</param>
    public void Close(Uri uri)
    {
        _documents.TryRemove(uri, out _);
        _logger.LogDebug("Document removed from store: {Uri}", uri);
    }

    /// <summary>
    /// Returns the current source text for an open document, or <c>null</c> if the
    /// document is not in the store.
    /// </summary>
    /// <param name="uri">The document URI to look up.</param>
    /// <returns>The current source text, or <c>null</c>.</returns>
    public string? GetText(Uri uri)
    {
        return _documents.TryGetValue(uri, out var state) ? state.Text : null;
    }

    /// <summary>
    /// Returns the URIs of all documents currently tracked in the store.
    /// </summary>
    /// <returns>An enumerable of <see cref="Uri"/> values for each open document.</returns>
    public IEnumerable<Uri> GetOpenDocumentUris() => _documents.Keys;

    /// <summary>
    /// Applies a sequence of incremental or full-replacement text changes to an open document,
    /// updates the store, and returns the resulting text.
    /// </summary>
    /// <remarks>
    /// Each <see cref="TextDocumentContentChangeEvent"/> is applied in order.
    /// If <c>change.Range</c> is set, the edit is applied as a range replacement using
    /// <see cref="GetOffset"/> to convert LSP line/character positions to string offsets.
    /// If <c>change.Range</c> is <c>null</c>, the entire document text is replaced with <c>change.Text</c>.
    /// </remarks>
    /// <param name="uri">The document URI to update.</param>
    /// <param name="version">The new LSP version number.</param>
    /// <param name="changes">The ordered list of changes from the <c>textDocument/didChange</c> notification.</param>
    /// <returns>The new full document text, or <c>null</c> if the document is not open.</returns>
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

    /// <summary>
    /// Converts a 0-based LSP line/character position to an absolute character offset in the document text.
    /// Clamps the result to the length of the text to avoid out-of-bounds access.
    /// </summary>
    /// <param name="text">The document text to scan.</param>
    /// <param name="line">The 0-based line number.</param>
    /// <param name="character">The 0-based character offset within the line.</param>
    /// <returns>The absolute character offset, clamped to <c>[0, text.Length]</c>.</returns>
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

    /// <summary>
    /// Immutable snapshot of an open document's text and LSP version number.
    /// </summary>
    /// <param name="Text">The full source text of the document at this version.</param>
    /// <param name="Version">The LSP integer version counter incremented on each change.</param>
    public record DocumentState(string Text, int Version);
}
