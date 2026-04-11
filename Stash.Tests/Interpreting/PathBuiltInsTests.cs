namespace Stash.Tests.Interpreting;

public class PathBuiltInsTests : StashTestBase
{
    // ── path.normalize ────────────────────────────────────────────────────

    [Fact]
    public void Normalize_ResolvesDotsToAbsolutePath()
    {
        var result = Run("let result = path.normalize(\"/foo/bar/../baz\");");
        Assert.Equal("/foo/baz", result);
    }

    [Fact]
    public void Normalize_NonStringThrows()
    {
        RunExpectingError("path.normalize(42);");
    }

    // ── path.isAbsolute ───────────────────────────────────────────────────

    [Fact]
    public void IsAbsolute_AbsolutePath()
    {
        var result = Run("let result = path.isAbsolute(\"/foo/bar\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IsAbsolute_RelativePath()
    {
        var result = Run("let result = path.isAbsolute(\"foo/bar\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void IsAbsolute_NonStringThrows()
    {
        RunExpectingError("path.isAbsolute(42);");
    }

    // ── path.relative ─────────────────────────────────────────────────────

    [Fact]
    public void Relative_SiblingDirectories()
    {
        var result = Run("let result = path.relative(\"/foo/bar\", \"/foo/baz/file.txt\");");
        Assert.IsType<string>(result);
        Assert.Contains("baz", (string)result!);
    }

    [Fact]
    public void Relative_NonStringThrows()
    {
        RunExpectingError("path.relative(42, \"/foo\");");
    }

    // ── path.separator ────────────────────────────────────────────────────

    [Fact]
    public void Separator_ReturnsString()
    {
        var result = Run("let result = path.separator();");
        Assert.IsType<string>(result);
        var sep = (string)result!;
        Assert.True(sep == "/" || sep == "\\");
    }
    // ── Optional Args ────────────────────────────────────────────────────────

    [Fact]
    public void Join_ThreeSegments_CombinesAll()
    {
        var result = Run("let result = path.join(\"/usr\", \"local\", \"bin\");");
        var p = Assert.IsType<string>(result);
        Assert.Contains("usr", p);
        Assert.Contains("local", p);
        Assert.Contains("bin", p);
    }

    [Fact]
    public void Join_FourSegments_CombinesAll()
    {
        var result = Run("let result = path.join(\"/usr\", \"local\", \"bin\", \"stash\");");
        var p = Assert.IsType<string>(result);
        Assert.Contains("usr", p);
        Assert.Contains("stash", p);
    }}
