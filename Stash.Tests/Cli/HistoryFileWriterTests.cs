using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Stash.Cli.History;

namespace Stash.Tests.Cli;

/// <summary>
/// Tests for <see cref="HistoryFileWriter"/> — append behavior (§12.3), in-memory cap (§12.4),
/// snapshot, clear, and persistence-failure handling (§12.6 partial).
/// </summary>
public sealed class HistoryFileWriterTests : IDisposable
{
    private readonly string _tempDir;

    public HistoryFileWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempPath(string name = "history") => Path.Combine(_tempDir, name);

    private HistoryFileWriter MakeWriter(
        string? path = null,
        int cap = 1000,
        IReadOnlyList<string>? initial = null,
        TextWriter? stderr = null)
    {
        return new HistoryFileWriter(
            path ?? TempPath(),
            cap,
            initial ?? Array.Empty<string>(),
            stderr ?? TextWriter.Null);
    }

    private static string ReadFile(string path) => File.ReadAllText(path, new UTF8Encoding(false));

    // =========================================================================
    // §12.3 — Append behavior
    // =========================================================================

    [Fact]
    public void Append_SingleEntry_WritesEntryFollowedByBlankLine()
    {
        string path = TempPath();
        var writer = MakeWriter(path);

        writer.Append("ls -la");

        string content = ReadFile(path);
        Assert.EndsWith("ls -la\n\n", content);
    }

    [Fact]
    public void Append_TwoConsecutiveDuplicates_OnlyFirstWritten()
    {
        string path = TempPath();
        var writer = MakeWriter(path);

        writer.Append("git status");
        writer.Append("git status");

        var snapshot = writer.Snapshot();
        Assert.Single(snapshot);

        // File should only contain one copy
        string content = ReadFile(path);
        int count = 0;
        int idx = 0;
        while ((idx = content.IndexOf("git status", idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx++;
        }
        Assert.Equal(1, count);
    }

    [Fact]
    public void Append_TwoNonAdjacentDuplicates_BothWritten()
    {
        string path = TempPath();
        var writer = MakeWriter(path);

        writer.Append("a");
        writer.Append("b");
        writer.Append("a");

        var snapshot = writer.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal("a", snapshot[0]);
        Assert.Equal("b", snapshot[1]);
        Assert.Equal("a", snapshot[2]);
    }

    [Fact]
    public void Append_LeadingSpaceLine_NotWrittenAnywhere()
    {
        string path = TempPath();
        var writer = MakeWriter(path);

        writer.Append(" secret-command");

        var snapshot = writer.Snapshot();
        Assert.Empty(snapshot);

        // File should not be created or should be empty / only header
        if (File.Exists(path))
        {
            string content = ReadFile(path);
            Assert.DoesNotContain("secret-command", content);
        }
    }

    [Fact]
    public void Append_LeadingTab_IsWritten()
    {
        string path = TempPath();
        var writer = MakeWriter(path);

        // Leading TAB does NOT trigger the leading-space skip rule (only ASCII space does)
        writer.Append("\techo hello");

        var snapshot = writer.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal("\techo hello", snapshot[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t  ")]
    public void Append_EmptyOrWhitespaceOnly_NotWritten(string input)
    {
        string path = TempPath();
        var writer = MakeWriter(path);

        writer.Append(input);

        var snapshot = writer.Snapshot();
        Assert.Empty(snapshot);
    }

    [Fact]
    public void Append_MultiLineEntry_PreservedInFile()
    {
        string path = TempPath();
        var writer = MakeWriter(path);

        string multiLine = "let x = fn(a, b) {\n    return a + b\n}";
        writer.Append(multiLine);

        string content = ReadFile(path);
        // Entry with embedded newlines should be in the file followed by \n\n
        Assert.Contains(multiLine + "\n\n", content);

        var snapshot = writer.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal(multiLine, snapshot[0]);
    }

    [Fact]
    public void Append_PreservesInitialEntries()
    {
        string path = TempPath();
        var writer = MakeWriter(path, initial: new[] { "a", "b" });

        writer.Append("c");

        var snapshot = writer.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal("a", snapshot[0]);
        Assert.Equal("b", snapshot[1]);
        Assert.Equal("c", snapshot[2]);
    }

    // =========================================================================
    // §12.4 — In-memory cap
    // =========================================================================

    [Fact]
    public void Append_OverCap_EvictsOldestInMemory()
    {
        string path = TempPath();
        var writer = MakeWriter(path, cap: 3, initial: new[] { "a", "b", "c" });

        writer.Append("d");

        var snapshot = writer.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal("b", snapshot[0]);
        Assert.Equal("c", snapshot[1]);
        Assert.Equal("d", snapshot[2]);
    }

    // =========================================================================
    // §12.x — Snapshot
    // =========================================================================

    [Fact]
    public void Snapshot_ReturnsCopy_NotReference()
    {
        string path = TempPath();
        var writer = MakeWriter(path);

        writer.Append("alpha");
        var snap1 = (List<string>)writer.Snapshot();
        snap1.Add("injected");

        var snap2 = writer.Snapshot();
        Assert.DoesNotContain("injected", snap2);
    }

    // =========================================================================
    // §6.6 — Clear
    // =========================================================================

    [Fact]
    public void Clear_EmptiesInMemoryAndTruncatesFileToHeader()
    {
        string path = TempPath();
        var writer = MakeWriter(path);

        writer.Append("cmd1");
        writer.Append("cmd2");
        writer.Clear();

        // In-memory is empty
        var snapshot = writer.Snapshot();
        Assert.Empty(snapshot);

        // File contains exactly the header
        string content = ReadFile(path);
        Assert.Equal("# stash history v1\n", content);
    }

    // =========================================================================
    // §12.x — Persistence failure
    // =========================================================================

    [Fact]
    public void Append_FileWriteFails_DisablesPersistenceAndKeepsInMemory()
    {
        // Force a failure: put writer at a path INSIDE an existing file so
        // Directory.CreateDirectory will throw (a file is not a directory).
        string fileAsDir = TempPath("notadir");
        File.WriteAllText(fileAsDir, "I am a file");
        string badPath = Path.Combine(fileAsDir, "history");

        var stderr = new StringWriter();
        var writer = new HistoryFileWriter(badPath, 1000, Array.Empty<string>(), stderr);

        // First append triggers the failure
        writer.Append("cmd1");
        string firstWarning = stderr.ToString();

        // Warning should have been written once
        Assert.NotEmpty(firstWarning);
        Assert.Contains("history disabled", firstWarning, StringComparison.OrdinalIgnoreCase);

        // Subsequent appends keep in-memory but do NOT repeat the warning
        string beforeSecond = stderr.ToString();
        writer.Append("cmd2");
        writer.Append("cmd3");
        string afterMore = stderr.ToString();

        // No additional stderr output after the first failure
        Assert.Equal(beforeSecond.Length, afterMore.Length - (afterMore.Length - beforeSecond.Length == 0 ? 0 : 0));
        // Simpler assertion: warning appears exactly once (count occurrences)
        int warningCount = 0;
        string warnText = "history disabled";
        int searchIdx = 0;
        string allStderr = afterMore;
        while ((searchIdx = allStderr.IndexOf(warnText, searchIdx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            warningCount++;
            searchIdx++;
        }
        Assert.Equal(1, warningCount);

        // In-memory still has all entries
        var snapshot = writer.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal("cmd1", snapshot[0]);
        Assert.Equal("cmd2", snapshot[1]);
        Assert.Equal("cmd3", snapshot[2]);
    }
}
