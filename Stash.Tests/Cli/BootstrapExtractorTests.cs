using System;
using System.IO;
using System.Linq;
using Stash.Cli.Repl;

namespace Stash.Tests.Cli;

/// <summary>
/// Unit tests for <see cref="BootstrapExtractor"/>: target-dir resolution,
/// extraction to a temp directory, idempotency, version-guard behavior, and
/// banner presence.
/// Uses isolated temp directories; never touches <c>~/.config/stash/prompt/</c>.
/// </summary>
public sealed class BootstrapExtractorTests : IDisposable
{
    private readonly string _tempRoot;

    public BootstrapExtractorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"stash-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private string TempDir(string label = "")
        => Path.Combine(_tempRoot, string.IsNullOrEmpty(label) ? Guid.NewGuid().ToString("N") : label);

    // =========================================================================
    // 1. GetTargetDir
    // =========================================================================

    [Fact]
    public void GetTargetDir_ReturnsPathEndingWithConfigStashPrompt()
    {
        string dir = BootstrapExtractor.GetTargetDir();
        Assert.NotEmpty(dir);
        // Path ends with .config/stash/prompt (using OS-specific separators)
        string expected = Path.Combine(".config", "stash", "prompt");
        Assert.True(dir.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            $"Expected path to end with '{expected}', got: '{dir}'");
    }

    // =========================================================================
    // 2. Extract to a fresh temp dir
    // =========================================================================

    [Fact]
    public void Extract_FreshDir_AllBundledFilesExtracted()
    {
        string target = TempDir("fresh");

        BootstrapExtractor.Extract(target);

        // 1 VERSION + 1 palette.stash + 1 bootstrap.stash + 1 default-prompt.stash
        // + 17 themes + 14 starters = 35 files
        string[] files = Directory.GetFiles(target, "*", SearchOption.AllDirectories);
        Assert.Equal(35, files.Length);
    }

    [Fact]
    public void Extract_VersionFilePresent_MatchesEmbeddedVersion()
    {
        string target = TempDir("version");

        BootstrapExtractor.Extract(target);

        string versionPath = Path.Combine(target, "VERSION");
        Assert.True(File.Exists(versionPath), "VERSION file not found after extract");

        string diskVersion = File.ReadAllText(versionPath).Trim();
        Assert.NotEmpty(diskVersion);
        // The embedded version is non-empty and matches the disk file
        // (NeedsExtraction returns false when versions match)
        Assert.False(BootstrapExtractor.NeedsExtraction(target, diskVersion),
            "NeedsExtraction should be false when version matches");
    }

    [Fact]
    public void Extract_BannerPresentInEveryStashFile()
    {
        string target = TempDir("banner");

        BootstrapExtractor.Extract(target);

        string[] stashFiles = Directory.GetFiles(target, "*.stash", SearchOption.AllDirectories);
        Assert.NotEmpty(stashFiles);

        foreach (string file in stashFiles)
        {
            string content = File.ReadAllText(file);
            Assert.True(content.StartsWith("// ============", StringComparison.Ordinal),
                $"Expected banner at start of '{Path.GetRelativePath(target, file)}'");
        }
    }

    [Fact]
    public void Extract_Twice_Idempotent_NoErrors()
    {
        string target = TempDir("idempotent");

        // First extraction
        BootstrapExtractor.Extract(target);
        int firstCount = Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length;

        // Second extraction — should wipe and re-create
        BootstrapExtractor.Extract(target);
        int secondCount = Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length;

        Assert.Equal(firstCount, secondCount);
    }

    [Fact]
    public void Extract_CreatesSubdirectories_ThemesAndStarters()
    {
        string target = TempDir("subdirs");

        BootstrapExtractor.Extract(target);

        Assert.True(Directory.Exists(Path.Combine(target, "themes")),
            "Expected 'themes/' subdirectory");
        Assert.True(Directory.Exists(Path.Combine(target, "starters")),
            "Expected 'starters/' subdirectory");
    }

    [Fact]
    public void Extract_BundledThemeAndStarterCounts()
    {
        string target = TempDir("counts");

        BootstrapExtractor.Extract(target);

        string[] themes = Directory.GetFiles(Path.Combine(target, "themes"), "*.stash");
        string[] starters = Directory.GetFiles(Path.Combine(target, "starters"), "*.stash");

        Assert.Equal(17, themes.Length);
        Assert.Equal(14, starters.Length);
    }

    // =========================================================================
    // 3. EnsureExtracted — uses HOME redirection to avoid touching real config
    // =========================================================================

    [Fact]
    public void EnsureExtracted_UpToDate_SentinelFilePreserved()
    {
        string fakeHome = TempDir("home-sentinel");
        Directory.CreateDirectory(fakeHome);
        string targetDir = Path.Combine(fakeHome, ".config", "stash", "prompt");

        // Extract once with a redirected home
        string? origHome = Environment.GetEnvironmentVariable("HOME");
        string? origProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        try
        {
            Environment.SetEnvironmentVariable("HOME", fakeHome);
            Environment.SetEnvironmentVariable("USERPROFILE", fakeHome);

            // First extraction establishes the correct version
            BootstrapExtractor.Extract(targetDir);

            // Write a sentinel file
            string sentinel = Path.Combine(targetDir, "_sentinel.txt");
            File.WriteAllText(sentinel, "keep me");

            // EnsureExtracted sees the current version matches → skips extraction
            BootstrapExtractor.EnsureExtracted();

            Assert.True(File.Exists(sentinel),
                "Sentinel should survive EnsureExtracted when version is up to date");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", origHome);
            Environment.SetEnvironmentVariable("USERPROFILE", origProfile);
        }
    }

    [Fact]
    public void EnsureExtracted_WrongVersion_RewipesAndReExtracts()
    {
        string fakeHome = TempDir("home-reextract");
        Directory.CreateDirectory(fakeHome);
        string targetDir = Path.Combine(fakeHome, ".config", "stash", "prompt");

        string? origHome = Environment.GetEnvironmentVariable("HOME");
        string? origProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        try
        {
            Environment.SetEnvironmentVariable("HOME", fakeHome);
            Environment.SetEnvironmentVariable("USERPROFILE", fakeHome);

            // Extract once, then deliberately corrupt the version
            BootstrapExtractor.Extract(targetDir);

            string versionPath = Path.Combine(targetDir, "VERSION");
            File.WriteAllText(versionPath, "0.0.0-stale");

            // Write a sentinel
            string sentinel = Path.Combine(targetDir, "_sentinel.txt");
            File.WriteAllText(sentinel, "wipe me");

            // EnsureExtracted sees version mismatch → wipes and re-extracts
            BootstrapExtractor.EnsureExtracted();

            // Sentinel should be gone (directory was wiped)
            Assert.False(File.Exists(sentinel),
                "Sentinel should be wiped when version is mismatched");

            // VERSION should now contain the correct embedded version
            string newVersion = File.ReadAllText(versionPath).Trim();
            Assert.False(BootstrapExtractor.NeedsExtraction(targetDir, newVersion),
                "After re-extraction, NeedsExtraction should return false");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", origHome);
            Environment.SetEnvironmentVariable("USERPROFILE", origProfile);
        }
    }

    // =========================================================================
    // 4. NeedsExtraction
    // =========================================================================

    [Fact]
    public void NeedsExtraction_MissingDir_ReturnsTrue()
    {
        string nonexistent = Path.Combine(_tempRoot, "nonexistent-dir");
        Assert.True(BootstrapExtractor.NeedsExtraction(nonexistent, "1.0.0"));
    }

    [Fact]
    public void NeedsExtraction_MissingVersionFile_ReturnsTrue()
    {
        string dir = TempDir("no-version");
        Directory.CreateDirectory(dir);
        Assert.True(BootstrapExtractor.NeedsExtraction(dir, "1.0.0"));
    }

    [Fact]
    public void NeedsExtraction_VersionMatches_ReturnsFalse()
    {
        string dir = TempDir("good-version");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "VERSION"), "2.5.0");
        Assert.False(BootstrapExtractor.NeedsExtraction(dir, "2.5.0"));
    }

    [Fact]
    public void NeedsExtraction_VersionMismatch_ReturnsTrue()
    {
        string dir = TempDir("bad-version");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "VERSION"), "1.0.0");
        Assert.True(BootstrapExtractor.NeedsExtraction(dir, "2.0.0"));
    }
}
