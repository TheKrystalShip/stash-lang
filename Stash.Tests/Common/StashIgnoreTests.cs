using Stash.Common;

namespace Stash.Tests.Common;

public class StashIgnoreTests
{
    // ── Default Exclusions ────────────────────────────────────────────────────

    [Fact]
    public void DefaultExclusions_GitDirectory_IsExcluded()
    {
        var ignore = new StashIgnore([]);
        Assert.True(ignore.IsExcluded(".git/config"));
    }

    [Fact]
    public void DefaultExclusions_StashesDirectory_IsExcluded()
    {
        var ignore = new StashIgnore([]);
        Assert.True(ignore.IsExcluded("stashes/foo/index.stash"));
    }

    [Fact]
    public void DefaultExclusions_LockFile_IsExcluded()
    {
        var ignore = new StashIgnore([]);
        Assert.True(ignore.IsExcluded("stash-lock.json"));
    }

    [Fact]
    public void DefaultExclusions_EnvFile_IsExcluded()
    {
        var ignore = new StashIgnore([]);
        Assert.True(ignore.IsExcluded(".env"));
    }

    [Fact]
    public void DefaultExclusions_RegularFile_IsNotExcluded()
    {
        var ignore = new StashIgnore([]);
        Assert.False(ignore.IsExcluded("src/main.stash"));
    }

    // ── Basic Glob Patterns ───────────────────────────────────────────────────

    [Fact]
    public void GlobPattern_WildcardExtension_MatchesAtRoot()
    {
        var ignore = new StashIgnore(["*.test.stash"]);
        Assert.True(ignore.IsExcluded("foo.test.stash"));
    }

    [Fact]
    public void GlobPattern_WildcardExtension_MatchesInSubdirectory()
    {
        var ignore = new StashIgnore(["*.test.stash"]);
        Assert.True(ignore.IsExcluded("lib/bar.test.stash"));
    }

    [Fact]
    public void GlobPattern_DirectorySuffix_ExcludesFilesInsideDirectory()
    {
        var ignore = new StashIgnore(["tests/"]);
        Assert.True(ignore.IsExcluded("tests/foo.stash"));
        Assert.True(ignore.IsExcluded("tests/sub/bar.stash"));
    }

    [Fact]
    public void GlobPattern_PlainFilename_MatchesAtRoot()
    {
        var ignore = new StashIgnore(["build.log"]);
        Assert.True(ignore.IsExcluded("build.log"));
    }

    [Fact]
    public void GlobPattern_PlainFilename_MatchesInSubdirectory()
    {
        var ignore = new StashIgnore(["build.log"]);
        Assert.True(ignore.IsExcluded("src/build.log"));
    }

    [Fact]
    public void GlobPattern_WildcardJs_DoesNotExcludeStashFile()
    {
        var ignore = new StashIgnore(["*.js"]);
        Assert.False(ignore.IsExcluded("app.stash"));
    }

    // ── Anchored Patterns ─────────────────────────────────────────────────────

    [Fact]
    public void AnchoredPattern_RootFile_ExcludesAtRoot()
    {
        var ignore = new StashIgnore(["/build.log"]);
        Assert.True(ignore.IsExcluded("build.log"));
    }

    [Fact]
    public void AnchoredPattern_RootFile_DoesNotExcludeInSubdirectory()
    {
        var ignore = new StashIgnore(["/build.log"]);
        Assert.False(ignore.IsExcluded("src/build.log"));
    }

    [Fact]
    public void AnchoredPattern_RootDirectory_ExcludesFilesInsideIt()
    {
        var ignore = new StashIgnore(["/dist/"]);
        Assert.True(ignore.IsExcluded("dist/out.js"));
    }

    [Fact]
    public void AnchoredPattern_RootDirectory_DoesNotMatchNestedDirectory()
    {
        var ignore = new StashIgnore(["/dist/"]);
        Assert.False(ignore.IsExcluded("src/dist/out.js"));
    }

    // ── Double-Star ───────────────────────────────────────────────────────────

    [Fact]
    public void DoubleStar_Leading_MatchesAtAllDepths()
    {
        var ignore = new StashIgnore(["**/test"]);
        Assert.True(ignore.IsExcluded("test"));
        Assert.True(ignore.IsExcluded("a/test"));
        Assert.True(ignore.IsExcluded("a/b/test"));
    }

    [Fact]
    public void DoubleStar_Trailing_MatchesDirectFilesInDirectory()
    {
        var ignore = new StashIgnore(["docs/**"]);
        Assert.True(ignore.IsExcluded("docs/readme.md"));
    }

    [Fact]
    public void DoubleStar_Trailing_MatchesNestedFilesInDirectory()
    {
        var ignore = new StashIgnore(["docs/**"]);
        Assert.True(ignore.IsExcluded("docs/api/ref.md"));
    }

    // ── Negation ──────────────────────────────────────────────────────────────

    [Fact]
    public void Negation_ExcludedPattern_ReincludesSpecificFile()
    {
        var ignore = new StashIgnore(["*.log", "!important.log"]);
        Assert.False(ignore.IsExcluded("important.log"));
    }

    [Fact]
    public void Negation_ExcludedPattern_StillExcludesOtherFiles()
    {
        var ignore = new StashIgnore(["*.log", "!important.log"]);
        Assert.True(ignore.IsExcluded("debug.log"));
    }

    [Fact]
    public void Negation_OverridesDefaultExclusion()
    {
        var ignore = new StashIgnore(["!.env"]);
        Assert.False(ignore.IsExcluded(".env"));
    }

    // ── Comments and Empty Lines ──────────────────────────────────────────────

    [Fact]
    public void Comments_AndEmptyLines_AreIgnored()
    {
        var ignore = new StashIgnore(["# this is a comment", "", "*.log"]);
        Assert.True(ignore.IsExcluded("debug.log"));
        Assert.False(ignore.IsExcluded("src/main.stash"));
    }

    [Fact]
    public void TrailingWhitespace_IsTrimmedFromPattern()
    {
        var ignore = new StashIgnore(["*.log   "]);
        Assert.True(ignore.IsExcluded("debug.log"));
    }

    // ── Filter Method ─────────────────────────────────────────────────────────

    [Fact]
    public void Filter_ReturnsOnlyNonExcludedPaths()
    {
        var ignore = new StashIgnore([]);
        var result = ignore.Filter([".git/config", "src/main.stash", "stash-lock.json"]);
        Assert.Equal(["src/main.stash"], result);
    }

    [Fact]
    public void Filter_AppliesDefaultAndCustomExclusions()
    {
        var ignore = new StashIgnore(["*.test.stash"]);
        var result = ignore.Filter(["src/main.stash", "foo.test.stash", ".env", "lib/utils.stash"]);
        Assert.Equal(["src/main.stash", "lib/utils.stash"], result);
    }

    [Fact]
    public void Filter_EmptyInput_ReturnsEmpty()
    {
        var ignore = new StashIgnore([]);
        var result = ignore.Filter([]);
        Assert.Empty(result);
    }

    // ── Load Method ───────────────────────────────────────────────────────────

    [Fact]
    public void Load_WithStashignoreFile_AppliesPatterns()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, ".stashignore"), "*.test.stash\ntests/\n");
            var ignore = StashIgnore.Load(tempDir);

            Assert.True(ignore.IsExcluded("foo.test.stash"));
            Assert.True(ignore.IsExcluded("tests/bar.stash"));
            Assert.False(ignore.IsExcluded("src/main.stash"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Load_WithoutStashignoreFile_OnlyDefaultsApply()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var ignore = StashIgnore.Load(tempDir);

            Assert.True(ignore.IsExcluded(".git/HEAD"));
            Assert.True(ignore.IsExcluded(".env"));
            Assert.False(ignore.IsExcluded("src/main.stash"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
