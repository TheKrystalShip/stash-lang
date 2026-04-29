using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Bytecode;
using Stash.Tests.Interpreting;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for Phase 3 of the Shell Mode spec: glob expansion in $(...) command arguments.
/// Unit tests exercise <see cref="GlobExpander"/> directly; integration tests go end-to-end
/// through the bytecode VM using $(...) syntax.
///
/// POSIX-only for integration tests (skip on Windows for v1 per spec §14.1).
/// Each test saves and restores the process working directory so tests don't interfere.
/// </summary>
public class GlobExpansionTests : StashTestBase
{
    // =========================================================================
    // GlobExpander.HasGlobChars — unit tests
    // =========================================================================

    [Fact]
    public void HasGlobChars_WithStar_ReturnsTrue()
    {
        Assert.True(GlobExpander.HasGlobChars("*.txt"));
    }

    [Fact]
    public void HasGlobChars_WithQuestion_ReturnsTrue()
    {
        Assert.True(GlobExpander.HasGlobChars("?.txt"));
    }

    [Fact]
    public void HasGlobChars_WithBracket_ReturnsTrue()
    {
        Assert.True(GlobExpander.HasGlobChars("[abc].txt"));
    }

    [Fact]
    public void HasGlobChars_PlainName_ReturnsFalse()
    {
        Assert.False(GlobExpander.HasGlobChars("readme.md"));
        Assert.False(GlobExpander.HasGlobChars("hello"));
        Assert.False(GlobExpander.HasGlobChars(""));
    }

    // =========================================================================
    // GlobExpander.Expand — unit tests (using a temporary directory)
    // =========================================================================

    [Fact]
    public void Expand_StarMatchesFiles_InCurrentDir()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "b.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "readme.md"), "");

        var matches = GlobExpander.Expand("*.txt");
        Assert.Equal(new[] { "a.txt", "b.txt" }, matches.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Expand_StarDoesNotMatchDotfiles_ByDefault()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, ".hidden"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "visible.txt"), "");

        var matches = GlobExpander.Expand("*");
        Assert.DoesNotContain(".hidden", matches);
        Assert.Contains("visible.txt", matches);
    }

    [Fact]
    public void Expand_DotPrefixedPattern_MatchesDotfiles()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, ".bashrc"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "other.txt"), "");

        var matches = GlobExpander.Expand(".bashrc");
        Assert.Equal(new[] { ".bashrc" }, matches.ToArray());
    }

    [Fact]
    public void Expand_QuestionMark_MatchesSingleChar()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "b.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "ab.txt"), "");

        var matches = GlobExpander.Expand("?.txt");
        Assert.Equal(new[] { "a.txt", "b.txt" }, matches.Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("ab.txt", matches);
    }

    [Fact]
    public void Expand_CharClass_MatchesAllowedChars()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "b.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "c.txt"), "");

        var matches = GlobExpander.Expand("[ab].txt");
        Assert.Equal(new[] { "a.txt", "b.txt" }, matches.Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("c.txt", matches);
    }

    [Fact]
    public void Expand_NegatedCharClass_ExcludesChars()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "b.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "c.txt"), "");

        var matches = GlobExpander.Expand("[!a].txt");
        Assert.DoesNotContain("a.txt", matches);
        Assert.Contains("b.txt", matches);
        Assert.Contains("c.txt", matches);
    }

    [Fact]
    public void Expand_DoubleStarRecurses()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "src"));
        Directory.CreateDirectory(Path.Combine(tmp.Path, "src", "sub"));
        File.WriteAllText(Path.Combine(tmp.Path, "src", "foo.cs"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "src", "sub", "bar.cs"), "");

        var matches = GlobExpander.Expand("src/**/*.cs");
        var sorted = matches.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(2, sorted.Length);
        Assert.Contains("src/foo.cs", matches);
        Assert.Contains("src/sub/bar.cs", matches);
    }

    [Fact]
    public void Expand_NoMatches_ReturnsEmptyList()
    {
        using var tmp = new TempDir();
        var matches = GlobExpander.Expand("*.xyz");
        Assert.Empty(matches);
    }

    [Fact]
    public void Expand_DeterministicOrdering()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "z.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "m.txt"), "");

        var matches = GlobExpander.Expand("*.txt");
        Assert.Equal(new[] { "a.txt", "m.txt", "z.txt" }, matches.ToArray());
    }

    // =========================================================================
    // Integration tests via $(...) — POSIX only
    // =========================================================================

    [Fact]
    public void Command_GlobInArgs_ExpandsBeforeRunning()
    {
        if (OperatingSystem.IsWindows()) return;

        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "b.txt"), "");

        var result = (string?)Run("let r = $(echo *.txt); let result = r.stdout;");
        Assert.NotNull(result);
        // 'echo a.txt b.txt' produces "a.txt b.txt" on stdout
        string trimmed = result.Trim();
        Assert.Contains("a.txt", trimmed);
        Assert.Contains("b.txt", trimmed);
    }

    [Fact]
    public void Command_QuotedGlob_NotExpanded()
    {
        if (OperatingSystem.IsWindows()) return;

        using var tmp = new TempDir();
        // Even if there are matching files, a quoted glob must NOT expand
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "");

        var result = (string?)Run("let r = $(echo \"*.txt\"); let result = r.stdout;");
        Assert.NotNull(result);
        Assert.Equal("*.txt", result.Trim());
    }

    [Fact]
    public void Command_NoMatchGlob_ThrowsCommandError()
    {
        if (OperatingSystem.IsWindows()) return;

        using var tmp = new TempDir();
        var result = (string?)Run("""
            let result = "no error";
            try {
                $(echo *.xyz);
            } catch (e) {
                result = e.message;
            }
            """);
        Assert.NotNull(result);
        Assert.Contains("did not match", result);
    }

    [Fact]
    public void Command_GlobInPipeline_ExpandsForEachStage()
    {
        if (OperatingSystem.IsWindows()) return;

        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "a.txt"), "");
        File.WriteAllText(Path.Combine(tmp.Path, "b.txt"), "");

        // echo *.txt | wc -w should output "2" (two words)
        var result = (string?)Run("let r = $(echo *.txt) | $(wc -w); let result = r.stdout;");
        Assert.NotNull(result);
        int count = int.Parse(result.Trim());
        Assert.Equal(2, count);
    }

    // =========================================================================
    // Helper: temporary directory that changes cwd and restores it on Dispose
    // =========================================================================

    private sealed class TempDir : IDisposable
    {
        private readonly string _oldCwd;

        public string Path { get; }

        public TempDir()
        {
            _oldCwd = Directory.GetCurrentDirectory();
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "stash_glob_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Directory.SetCurrentDirectory(Path);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_oldCwd);
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
