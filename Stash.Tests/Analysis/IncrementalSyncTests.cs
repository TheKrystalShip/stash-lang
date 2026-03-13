using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Lsp.Analysis;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Stash.Tests.Analysis;

public class IncrementalSyncTests
{
    private static readonly Uri TestUri = new("file:///test.stash");

    private static DocumentManager CreateWithDocument(string text)
    {
        var mgr = new DocumentManager();
        mgr.Open(TestUri, text, 1);
        return mgr;
    }

    private static TextDocumentContentChangeEvent RangeChange(
        int startLine, int startChar, int endLine, int endChar, string text)
        => new TextDocumentContentChangeEvent
        {
            Range = new LspRange(new Position(startLine, startChar), new Position(endLine, endChar)),
            Text = text
        };

    // ──────────────────────────────────────────────────────────
    // 1. Returns null when document is not open
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_ReturnsNull_WhenDocumentNotOpen()
    {
        var mgr = new DocumentManager();
        var unknownUri = new Uri("file:///unknown.stash");

        var result = mgr.ApplyIncrementalChanges(unknownUri, 2,
            [RangeChange(0, 0, 0, 1, "x")]);

        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────
    // 2. Single character insertion in the middle of a line
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_SingleCharacterInsertion()
    {
        // "let x = 1;\nlet y = 2;\n"
        //       ^ insert 'z' at (0,4)
        var mgr = CreateWithDocument("let x = 1;\nlet y = 2;\n");

        var result = mgr.ApplyIncrementalChanges(TestUri, 2, [RangeChange(0, 4, 0, 4, "z")]);

        Assert.Equal("let zx = 1;\nlet y = 2;\n", result);
        Assert.Equal("let zx = 1;\nlet y = 2;\n", mgr.GetText(TestUri));
    }

    // ──────────────────────────────────────────────────────────
    // 3. Single character deletion
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_SingleCharacterDeletion()
    {
        // "let x = 1;\nlet y = 2;\n"
        //       ^ delete 'x': range (0,4)-(0,5)
        var mgr = CreateWithDocument("let x = 1;\nlet y = 2;\n");

        var result = mgr.ApplyIncrementalChanges(TestUri, 2, [RangeChange(0, 4, 0, 5, "")]);

        Assert.Equal("let  = 1;\nlet y = 2;\n", result);
    }

    // ──────────────────────────────────────────────────────────
    // 4. Replace one word with another
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_ReplaceWord()
    {
        // "let foo = 1;\n"
        //      ^^^  replace "foo" at (0,4)-(0,7) with "bar"
        var mgr = CreateWithDocument("let foo = 1;\n");

        var result = mgr.ApplyIncrementalChanges(TestUri, 2, [RangeChange(0, 4, 0, 7, "bar")]);

        Assert.Equal("let bar = 1;\n", result);
    }

    // ──────────────────────────────────────────────────────────
    // 5. Insert newline to split a line into two
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_InsertNewLine()
    {
        // "hello world\n"
        //       ^ insert '\n' at (0,5)
        var mgr = CreateWithDocument("hello world\n");

        var result = mgr.ApplyIncrementalChanges(TestUri, 2, [RangeChange(0, 5, 0, 5, "\n")]);

        Assert.Equal("hello\n world\n", result);
    }

    // ──────────────────────────────────────────────────────────
    // 6. Delete an entire line including its newline
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_DeleteEntireLine()
    {
        // "line1\nline2\nline3\n"
        // Delete "line2\n": range (1,0)-(2,0), replacement ""
        var mgr = CreateWithDocument("line1\nline2\nline3\n");

        var result = mgr.ApplyIncrementalChanges(TestUri, 2, [RangeChange(1, 0, 2, 0, "")]);

        Assert.Equal("line1\nline3\n", result);
    }

    // ──────────────────────────────────────────────────────────
    // 7. Replace text spanning multiple lines
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_MultiLineEdit()
    {
        // "aaa\nbbb\nccc\n"
        // Replace "bbb\nccc" (line 1 col 0 → line 2 col 3) with "xxx"
        var mgr = CreateWithDocument("aaa\nbbb\nccc\n");

        var result = mgr.ApplyIncrementalChanges(TestUri, 2, [RangeChange(1, 0, 2, 3, "xxx")]);

        Assert.Equal("aaa\nxxx\n", result);
    }

    // ──────────────────────────────────────────────────────────
    // 8. Insert text at the very beginning of the document
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_InsertAtBeginning()
    {
        // "hello\n"
        // Insert "world\n" at (0,0)-(0,0)
        var mgr = CreateWithDocument("hello\n");

        var result = mgr.ApplyIncrementalChanges(TestUri, 2, [RangeChange(0, 0, 0, 0, "world\n")]);

        Assert.Equal("world\nhello\n", result);
    }

    // ──────────────────────────────────────────────────────────
    // 9. Insert text at the very end of the document
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_InsertAtEnd()
    {
        // "let x = 1;\n"  (line 0, '\n' at offset 10)
        // Line 1, char 0 is the position just past the final newline
        var mgr = CreateWithDocument("let x = 1;\n");

        var result = mgr.ApplyIncrementalChanges(TestUri, 2, [RangeChange(1, 0, 1, 0, "let y = 2;\n")]);

        Assert.Equal("let x = 1;\nlet y = 2;\n", result);
    }

    // ──────────────────────────────────────────────────────────
    // 10. Full replacement when Range is null
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_FullReplacement_WhenRangeIsNull()
    {
        var mgr = CreateWithDocument("old content\n");

        var result = mgr.ApplyIncrementalChanges(TestUri, 2,
            [new TextDocumentContentChangeEvent { Text = "new content\n" }]);

        Assert.Equal("new content\n", result);
        Assert.Equal("new content\n", mgr.GetText(TestUri));
    }

    // ──────────────────────────────────────────────────────────
    // 11. Multiple sequential edits applied in one call
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_MultipleSequentialEdits()
    {
        // Start: "let x = 1;\nlet y = 2;\n"
        // Edit 1: replace 'x' (0,4)-(0,5) with 'a' → "let a = 1;\nlet y = 2;\n"
        // Edit 2: replace 'y' (1,4)-(1,5) with 'b' → "let a = 1;\nlet b = 2;\n"
        var mgr = CreateWithDocument("let x = 1;\nlet y = 2;\n");

        var result = mgr.ApplyIncrementalChanges(TestUri, 2,
        [
            RangeChange(0, 4, 0, 5, "a"),
            RangeChange(1, 4, 1, 5, "b")
        ]);

        Assert.Equal("let a = 1;\nlet b = 2;\n", result);
    }

    // ──────────────────────────────────────────────────────────
    // 12. Document version is updated after applying changes
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void ApplyIncrementalChanges_UpdatesVersion()
    {
        var mgr = CreateWithDocument("let x = 1;\n");

        mgr.ApplyIncrementalChanges(TestUri, 5, [RangeChange(0, 4, 0, 5, "y")]);

        // Access the internal _documents field via reflection to verify version was stored
        var field = typeof(DocumentManager)
            .GetField("_documents", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var docs = (ConcurrentDictionary<Uri, DocumentManager.DocumentState>)field.GetValue(mgr)!;
        var state = docs[TestUri];

        Assert.Equal(5, state.Version);
        Assert.Equal("let y = 1;\n", state.Text);
    }
}
