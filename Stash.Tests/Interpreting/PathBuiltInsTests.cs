namespace Stash.Tests.Interpreting;

public class PathBuiltInsTests : StashTestBase
{
    // ── path.match ────────────────────────────────────────────────────────

    [Fact]
    public void Match_DoubleStarCrossesSlash_ReturnsTrue()
    {
        var result = Run(@"let result = path.match(""a/b.cs"", ""a/**"");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Match_DoubleStarDoesNotMatchEmptySegment_ReturnsFalse()
    {
        // "a" alone should not match "a/**" — ** requires at least one more char.
        var result = Run(@"let result = path.match(""a"", ""a/**"");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Match_SingleStarCrossesSlash_ReturnsTrue()
    {
        // Bash [[ ]] semantics: * crosses /
        var result = Run(@"let result = path.match(""a/b/file.cs"", ""a/*.cs"");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Match_SingleStarAtSameLevel_ReturnsTrue()
    {
        var result = Run(@"let result = path.match(""a/file.cs"", ""a/*.cs"");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Match_QuestionMark_MatchesSingleChar()
    {
        var result = Run(@"let result = path.match(""a/b.cs"", ""a/?.cs"");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Match_QuestionMark_DoesNotMatchTwoChars()
    {
        var result = Run(@"let result = path.match(""a/bc.cs"", ""a/?.cs"");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Match_CharacterClass_Positive()
    {
        var result = Run(@"let result = path.match(""Stash.Core/Foo.cs"", ""Stash.[A-Z]ore/Foo.cs"");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Match_CharacterClass_Negative()
    {
        var result = Run(@"let result = path.match(""Stash.1ore/Foo.cs"", ""Stash.[A-Z]ore/Foo.cs"");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Match_NegatedCharacterClassBang_ExcludesChar()
    {
        // [!T] should match anything except T
        var result = Run(@"let result = path.match(""Stash.Core/Foo.cs"", ""Stash.[!T]ore/Foo.cs"");");
        Assert.Equal(true, result);

        var result2 = Run(@"let result = path.match(""Stash.Tore/Foo.cs"", ""Stash.[!T]ore/Foo.cs"");");
        Assert.Equal(false, result2);
    }

    [Fact]
    public void Match_NegatedCharacterClassCaret_ExcludesChar()
    {
        // [^T] should match anything except T
        var result = Run(@"let result = path.match(""Stash.Core/Foo.cs"", ""Stash.[^T]ore/Foo.cs"");");
        Assert.Equal(true, result);

        var result2 = Run(@"let result = path.match(""Stash.Tore/Foo.cs"", ""Stash.[^T]ore/Foo.cs"");");
        Assert.Equal(false, result2);
    }

    [Fact]
    public void Match_BackslashEscape_MatchesLiteralStar()
    {
        // \* should match a literal '*', not a wildcard
        var result = Run(@"let result = path.match(""a/b.cs"", ""a/\*.cs"");");
        Assert.Equal(false, result);

        var result2 = Run(@"let result = path.match(""a/*.cs"", ""a/\*.cs"");");
        Assert.Equal(true, result2);
    }

    [Fact]
    public void Match_LiteralPath_ExactMatch()
    {
        var result = Run(@"let result = path.match(""CHANGELOG.md"", ""CHANGELOG.md"");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Match_LiteralPath_NoMatch()
    {
        var result = Run(@"let result = path.match(""other/CHANGELOG.md"", ""CHANGELOG.md"");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Match_EmptyPathEmptyPattern_ReturnsTrue()
    {
        var result = Run(@"let result = path.match("""", """");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Match_EmptyPathNonEmptyPattern_ReturnsFalse()
    {
        var result = Run(@"let result = path.match("""", ""a/**"");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Match_CaseSensitive_ReturnsFalse()
    {
        // Pattern is case-sensitive
        var result = Run(@"let result = path.match(""stash.core/foo.cs"", ""Stash.Core/Foo.cs"");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Match_ExtglobAtSign_ThrowsRuntimeError()
    {
        RunExpectingError(@"path.match(""a/b.cs"", ""@(a|b)"");");
    }

    [Fact]
    public void Match_ExtglobBang_ThrowsRuntimeError()
    {
        RunExpectingError(@"path.match(""a/b.cs"", ""!(a|b)"");");
    }

    [Fact]
    public void Match_ExtglobPlus_ThrowsRuntimeError()
    {
        RunExpectingError(@"path.match(""a/b.cs"", ""+(a|b)"");");
    }

    [Fact]
    public void Match_ExtglobQuestion_ThrowsRuntimeError()
    {
        RunExpectingError(@"path.match(""a/b.cs"", ""?(a|b)"");");
    }

    [Fact]
    public void Match_ExtglobStar_ThrowsRuntimeError()
    {
        RunExpectingError(@"path.match(""a/b.cs"", ""*(a|b)"");");
    }

    [Fact]
    public void Match_MalformedUnclosedBracket_FallsBackToLiteral()
    {
        // Unclosed '[' — treated as literal match (mirrors bash), does not throw.
        // Path "a/[b.cs" against pattern "a/[b.cs" should match (literal equality).
        var result = Run(@"let result = path.match(""a/[b.cs"", ""a/[b.cs"");");
        Assert.Equal(true, result);

        // A different path should not match.
        var result2 = Run(@"let result = path.match(""a/xb.cs"", ""a/[b.cs"");");
        Assert.Equal(false, result2);
    }

    [Fact]
    public void Match_DotInPath_EscapedProperly()
    {
        // '.' in pattern is literal, not regex '.'
        var result = Run(@"let result = path.match(""Stash_Core/Foo.cs"", ""Stash.Core/Foo.cs"");");
        Assert.Equal(false, result);
    }


    // ── path.normalize ────────────────────────────────────────────────────

    [Fact]
    public void Normalize_ResolvesDotsToAbsolutePath()
    {
        var result = Run("let result = path.normalize(\"/foo/bar/../baz\");");
        Assert.Equal(System.IO.Path.GetFullPath("/foo/bar/../baz"), result);
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
