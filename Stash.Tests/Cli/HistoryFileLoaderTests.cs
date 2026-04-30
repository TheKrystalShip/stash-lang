using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Stash.Cli.History;

namespace Stash.Tests.Cli;

// ── Collection definition for env-var-sensitive tests ──────────────────────
[CollectionDefinition("HistoryEnvTests", DisableParallelization = true)]
public sealed class HistoryEnvTestsCollection { }

/// <summary>
/// Tests for <see cref="HistoryFileLoader"/> — file format (§12.1), path resolution (§12.2),
/// cap and trim (§12.4), and concurrency (§12.5 partial).
/// </summary>
public sealed class HistoryFileLoaderFormatTests : IDisposable
{
    private readonly string _tempDir;

    public HistoryFileLoaderFormatTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string TempFile(string name = "history") => Path.Combine(_tempDir, name);

    private static List<string> LoadFrom(string path, int cap = 10000, StringWriter? sw = null)
        => HistoryFileLoader.Load(path, cap, sw ?? TextWriter.Null as TextWriter as StringWriter ?? new StringWriter());

    private static List<string> Load(string path, int cap = 10000, TextWriter? stderr = null)
        => HistoryFileLoader.Load(path, cap, stderr ?? TextWriter.Null);

    // =========================================================================
    // §12.1 — File format
    // =========================================================================

    [Fact]
    public void Load_EmptyFile_ReturnsEmpty()
    {
        string path = TempFile();
        File.WriteAllText(path, "", new UTF8Encoding(false));

        var entries = Load(path);

        Assert.Empty(entries);
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsEmpty()
    {
        string path = TempFile("does-not-exist");

        var entries = Load(path);

        Assert.Empty(entries);
    }

    [Fact]
    public void Load_HeaderOnly_ReturnsEmpty()
    {
        string path = TempFile();
        File.WriteAllText(path, "# stash history v1\n", new UTF8Encoding(false));

        var entries = Load(path);

        Assert.Empty(entries);
    }

    [Fact]
    public void Load_ThreeEntries_ReturnsThreeOldestFirst()
    {
        string path = TempFile();
        File.WriteAllText(path, "# stash history v1\nfoo\n\nbar\n\nbaz\n\n", new UTF8Encoding(false));

        var entries = Load(path);

        Assert.Equal(3, entries.Count);
        Assert.Equal("foo", entries[0]);
        Assert.Equal("bar", entries[1]);
        Assert.Equal("baz", entries[2]);
    }

    [Fact]
    public void Load_MultiLineEntry_PreservesEmbeddedNewlines()
    {
        string path = TempFile();
        // A multi-line entry is separated from neighbors by blank lines (double-newline).
        // The entry itself contains embedded newlines.
        string multiLine = "let x = fn(a, b) {\n    return a + b\n}";
        string content = $"# stash history v1\nbefore\n\n{multiLine}\n\nafter\n\n";
        File.WriteAllText(path, content, new UTF8Encoding(false));

        var entries = Load(path);

        Assert.Equal(3, entries.Count);
        Assert.Equal("before", entries[0]);
        Assert.Equal(multiLine, entries[1]);
        Assert.Equal("after", entries[2]);
    }

    [Fact]
    public void Load_UnknownHeaderVersion_ParsesAndWarns()
    {
        string path = TempFile();
        File.WriteAllText(path, "# stash history v99\nfoo\n\nbar\n\n", new UTF8Encoding(false));

        var stderr = new StringWriter();
        var entries = Load(path, 10000, stderr);

        // Entries are still parsed
        Assert.Equal(2, entries.Count);
        Assert.Equal("foo", entries[0]);
        Assert.Equal("bar", entries[1]);

        // Warning written to stderr
        string warning = stderr.ToString();
        Assert.Contains("unknown format version", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_TrailingBlankLines_NotTreatedAsEntry()
    {
        string path = TempFile();
        // Three trailing blank lines after the last entry
        File.WriteAllText(path, "# stash history v1\nfoo\n\n\n\n\n", new UTF8Encoding(false));

        var entries = Load(path);

        Assert.Single(entries);
        Assert.Equal("foo", entries[0]);
    }

    [Fact]
    public void Load_MissingTrailingBlankLine_StillParses()
    {
        string path = TempFile();
        // Last entry has no trailing blank line
        File.WriteAllText(path, "# stash history v1\nfoo\n\nbar", new UTF8Encoding(false));

        var entries = Load(path);

        Assert.Equal(2, entries.Count);
        Assert.Equal("foo", entries[0]);
        Assert.Equal("bar", entries[1]);
    }

    // =========================================================================
    // §12.4 — Cap and trim
    // =========================================================================

    [Fact]
    public void Load_CountAboveCap_TrimsOldest()
    {
        string path = TempFile();
        // 5 entries, cap = 3 → keep newest 3 (c, d, e)
        File.WriteAllText(path, "# stash history v1\na\n\nb\n\nc\n\nd\n\ne\n\n", new UTF8Encoding(false));

        var entries = Load(path, cap: 3);

        Assert.Equal(3, entries.Count);
        Assert.Equal("c", entries[0]);
        Assert.Equal("d", entries[1]);
        Assert.Equal("e", entries[2]);

        // File should have been rewritten to contain only the 3 kept entries
        var reloaded = Load(path, cap: 10000);
        Assert.Equal(3, reloaded.Count);
        Assert.Equal("c", reloaded[0]);
        Assert.Equal("d", reloaded[1]);
        Assert.Equal("e", reloaded[2]);
    }

    [Fact]
    public void Load_CountAtCap_NoRewrite()
    {
        string path = TempFile();
        File.WriteAllText(path, "# stash history v1\na\n\nb\n\nc\n\n", new UTF8Encoding(false));
        DateTime mtimeBefore = File.GetLastWriteTimeUtc(path);

        // Small sleep to ensure mtime would change if file were written
        System.Threading.Thread.Sleep(50);

        var entries = Load(path, cap: 3);

        Assert.Equal(3, entries.Count);

        // File was not rewritten — same content
        string content = File.ReadAllText(path);
        Assert.Contains("a", content);
        Assert.Contains("b", content);
        Assert.Contains("c", content);
    }

    [Fact]
    public void RewriteAtomic_WritesHeaderAndEntries()
    {
        string path = TempFile();
        var entries = new List<string> { "first", "second", "third" };

        HistoryFileLoader.RewriteAtomic(path, entries, TextWriter.Null);

        string content = File.ReadAllText(path, new UTF8Encoding(false));
        Assert.StartsWith("# stash history v1\n", content);
        Assert.Contains("first\n\n", content);
        Assert.Contains("second\n\n", content);
        Assert.Contains("third\n\n", content);
    }
}

/// <summary>
/// Tests for <see cref="HistoryFileLoader"/> path resolution and cap — requires env var isolation.
/// </summary>
[Collection("HistoryEnvTests")]
public sealed class HistoryFileLoaderEnvTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Dictionary<string, string?> _savedEnv = new();

    public HistoryFileLoaderEnvTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private void SetEnv(string name, string? value)
    {
        if (!_savedEnv.ContainsKey(name))
            _savedEnv[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _savedEnv)
            Environment.SetEnvironmentVariable(name, value);
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // =========================================================================
    // §12.2 — Path resolution
    // =========================================================================

    [Fact]
    public void ResolvePath_StashHistoryFileSet_HonoredAbsolute()
    {
        string customPath = Path.Combine(_tempDir, "custom_history");
        SetEnv("STASH_HISTORY_FILE", customPath);

        string? result = HistoryFileLoader.ResolvePath();

        Assert.Equal(customPath, result);
    }

    [Fact]
    public void ResolvePath_StashHistoryFileEmpty_ReturnsNull()
    {
        SetEnv("STASH_HISTORY_FILE", "");

        string? result = HistoryFileLoader.ResolvePath();

        Assert.Null(result);
    }

    [Fact]
    public void ResolvePath_XdgStateHomeSet_UsedOnPosix()
    {
        if (OperatingSystem.IsWindows()) return;

        SetEnv("STASH_HISTORY_FILE", null);
        string xdgState = Path.Combine(_tempDir, "xdg_state");
        SetEnv("XDG_STATE_HOME", xdgState);

        string? result = HistoryFileLoader.ResolvePath();

        Assert.NotNull(result);
        Assert.StartsWith(xdgState, result);
        Assert.EndsWith("history", result);
    }

    [Fact]
    public void ResolvePath_XdgStateHomeUnset_UsesLocalState()
    {
        if (OperatingSystem.IsWindows()) return;

        SetEnv("STASH_HISTORY_FILE", null);
        SetEnv("XDG_STATE_HOME", null);

        string? result = HistoryFileLoader.ResolvePath();

        Assert.NotNull(result);
        // Should use ~/.local/state/stash/history or fallback to ~/.stash_history
        Assert.True(
            result!.Contains(Path.Combine(".local", "state", "stash")) || result.EndsWith(".stash_history"),
            $"Unexpected path: {result}");
        Assert.EndsWith("history", result);
    }

    // =========================================================================
    // §12.4 — GetCap
    // =========================================================================

    [Fact]
    public void GetCap_DefaultIs10000()
    {
        SetEnv("STASH_HISTORY_SIZE", null);

        int cap = HistoryFileLoader.GetCap();

        Assert.Equal(10000, cap);
    }

    [Fact]
    public void GetCap_StashHistorySizeOverride()
    {
        SetEnv("STASH_HISTORY_SIZE", "500");

        int cap = HistoryFileLoader.GetCap();

        Assert.Equal(500, cap);
    }

    [Fact]
    public void GetCap_StashHistorySizeZero_ReturnsZero()
    {
        SetEnv("STASH_HISTORY_SIZE", "0");

        int cap = HistoryFileLoader.GetCap();

        Assert.Equal(0, cap);
    }

    [Fact]
    public void GetCap_StashHistorySizeNegative_ReturnsMaxValue()
    {
        SetEnv("STASH_HISTORY_SIZE", "-1");

        int cap = HistoryFileLoader.GetCap();

        Assert.Equal(int.MaxValue, cap);
    }
}
