namespace Stash.Tests.Interpreting;

public class StrBuiltInsTests : StashTestBase
{
    // ── str.capitalize ────────────────────────────────────────────────────

    [Fact]
    public void Capitalize_BasicString()
    {
        var result = Run("let result = str.capitalize(\"hello world\");");
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void Capitalize_AlreadyCapitalized()
    {
        var result = Run("let result = str.capitalize(\"HELLO\");");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void Capitalize_EmptyString()
    {
        var result = Run("let result = str.capitalize(\"\");");
        Assert.Equal("", result);
    }

    [Fact]
    public void Capitalize_SingleChar()
    {
        var result = Run("let result = str.capitalize(\"a\");");
        Assert.Equal("A", result);
    }

    [Fact]
    public void Capitalize_NonStringThrows()
    {
        RunExpectingError("str.capitalize(42);");
    }

    // ── str.title ─────────────────────────────────────────────────────────

    [Fact]
    public void Title_BasicString()
    {
        var result = Run("let result = str.title(\"hello world\");");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Title_MixedCase()
    {
        var result = Run("let result = str.title(\"hELLO wORLD\");");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Title_EmptyString()
    {
        var result = Run("let result = str.title(\"\");");
        Assert.Equal("", result);
    }

    [Fact]
    public void Title_SingleWord()
    {
        var result = Run("let result = str.title(\"hello\");");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void Title_NonStringThrows()
    {
        RunExpectingError("str.title(42);");
    }

    // ── str.lines ─────────────────────────────────────────────────────────

    [Fact]
    public void Lines_SplitsByNewline()
    {
        var result = Run("let result = str.lines(\"a\\nb\\nc\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("b", list[1]);
        Assert.Equal("c", list[2]);
    }

    [Fact]
    public void Lines_EmptyString()
    {
        var result = Run("let result = str.lines(\"\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        Assert.Equal("", list[0]);
    }

    [Fact]
    public void Lines_NoNewlines()
    {
        var result = Run("let result = str.lines(\"hello\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        Assert.Equal("hello", list[0]);
    }

    [Fact]
    public void Lines_NonStringThrows()
    {
        RunExpectingError("str.lines(42);");
    }

    // ── str.words ─────────────────────────────────────────────────────────

    [Fact]
    public void Words_SplitsByWhitespace()
    {
        var result = Run("let result = str.words(\"hello  world  foo\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("hello", list[0]);
        Assert.Equal("world", list[1]);
        Assert.Equal("foo", list[2]);
    }

    [Fact]
    public void Words_EmptyString()
    {
        var result = Run("let result = str.words(\"\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void Words_TabsAndSpaces()
    {
        var result = Run("let result = str.words(\"a\\tb\\tc\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Words_NonStringThrows()
    {
        RunExpectingError("str.words(42);");
    }

    // ── str.truncate ──────────────────────────────────────────────────────

    [Fact]
    public void Truncate_ShorterThanMax()
    {
        var result = Run("let result = str.truncate(\"hello\", 10);");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Truncate_LongerThanMax()
    {
        var result = Run("let result = str.truncate(\"hello world\", 8);");
        Assert.Equal("hello...", result);
    }

    [Fact]
    public void Truncate_CustomSuffix()
    {
        var result = Run("let result = str.truncate(\"hello world\", 8, \"--\");");
        Assert.Equal("hello --", result);
    }

    [Fact]
    public void Truncate_ExactLength()
    {
        var result = Run("let result = str.truncate(\"hello\", 5);");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Truncate_NonStringThrows()
    {
        RunExpectingError("str.truncate(42, 5);");
    }

    [Fact]
    public void Truncate_NonIntLenThrows()
    {
        RunExpectingError("str.truncate(\"hello\", \"five\");");
    }

    // ── str.slug ──────────────────────────────────────────────────────────

    [Fact]
    public void Slug_BasicString()
    {
        var result = Run("let result = str.slug(\"Hello World!\");");
        Assert.Equal("hello-world", result);
    }

    [Fact]
    public void Slug_SpecialCharacters()
    {
        var result = Run("let result = str.slug(\"My Article #1: Great!\");");
        Assert.Equal("my-article-1-great", result);
    }

    [Fact]
    public void Slug_MultipleSpaces()
    {
        var result = Run("let result = str.slug(\"  hello   world  \");");
        Assert.Equal("hello-world", result);
    }

    [Fact]
    public void Slug_EmptyString()
    {
        var result = Run("let result = str.slug(\"\");");
        Assert.Equal("", result);
    }

    [Fact]
    public void Slug_NonStringThrows()
    {
        RunExpectingError("str.slug(42);");
    }

    // ── str.wrap ──────────────────────────────────────────────────────────

    [Fact]
    public void Wrap_ShortLine()
    {
        var result = Run("let result = str.wrap(\"hello world\", 20);");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Wrap_LongLine()
    {
        var result = Run("let result = str.wrap(\"hello world foo bar\", 10);");
        Assert.Contains("\n", (string)result!);
    }

    [Fact]
    public void Wrap_ZeroWidthThrows()
    {
        RunExpectingError("str.wrap(\"hello\", 0);");
    }

    [Fact]
    public void Wrap_NonStringThrows()
    {
        RunExpectingError("str.wrap(42, 10);");
    }

    [Fact]
    public void Wrap_NonIntWidthThrows()
    {
        RunExpectingError("str.wrap(\"hello\", \"ten\");");
    }

    // ── str.capture ───────────────────────────────────────────────────────

    [Fact]
    public void Capture_NoGroups_ReturnsFullMatch()
    {
        var result = Run("""
            let m = str.capture("hello world", "world");
            let result = m.value;
        """);
        Assert.Equal("world", result);
    }

    [Fact]
    public void Capture_FullMatchValue()
    {
        var result = Run("""
            let m = str.capture("version 1.23", "(\\d+)\\.(\\d+)");
            let result = m.value;
        """);
        Assert.Equal("1.23", result);
    }

    [Fact]
    public void Capture_FullMatchIndex()
    {
        var result = Run("""
            let m = str.capture("version 1.23", "(\\d+)\\.(\\d+)");
            let result = m.index;
        """);
        Assert.Equal(8L, result);
    }

    [Fact]
    public void Capture_FullMatchLength()
    {
        var result = Run("""
            let m = str.capture("version 1.23", "(\\d+)\\.(\\d+)");
            let result = m.length;
        """);
        Assert.Equal(4L, result);
    }

    [Fact]
    public void Capture_PositionalGroups_Values()
    {
        var result = Run("""
            let m = str.capture("version 1.23", "(\\d+)\\.(\\d+)");
            let result = m.groups[1].value;
        """);
        Assert.Equal("1", result);

        var result2 = Run("""
            let m = str.capture("version 1.23", "(\\d+)\\.(\\d+)");
            let result = m.groups[2].value;
        """);
        Assert.Equal("23", result2);
    }

    [Fact]
    public void Capture_PositionalGroups_Index()
    {
        var result = Run("""
            let m = str.capture("version 1.23", "(\\d+)\\.(\\d+)");
            let result = m.groups[1].index;
        """);
        Assert.Equal(8L, result);
    }

    [Fact]
    public void Capture_PositionalGroups_Length()
    {
        var result = Run("""
            let m = str.capture("version 1.23", "(\\d+)\\.(\\d+)");
            let result = m.groups[1].length;
        """);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Capture_NamedGroups_Dict()
    {
        var result = Run("""
            let m = str.capture("192.168.1.1 myhost", "(?<ip>\\d+\\.\\d+\\.\\d+\\.\\d+)\\s+(?<host>\\S+)");
            let result = m.namedGroups["ip"];
        """);
        Assert.Equal("192.168.1.1", result);
    }

    [Fact]
    public void Capture_NamedGroups_GroupName()
    {
        var result = Run("""
            let m = str.capture("192.168.1.1 myhost", "(?<ip>\\d+\\.\\d+\\.\\d+\\.\\d+)\\s+(?<host>\\S+)");
            let result = m.groups[1].name;
        """);
        Assert.Equal("ip", result);
    }

    [Fact]
    public void Capture_NamedGroups_PositionalGroupNameIsNull()
    {
        var result = Run("""
            let m = str.capture("version 1.23", "(\\d+)\\.(\\d+)");
            let result = m.groups[0].name == null;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Capture_OptionalGroupNotMatched_ValueIsNull()
    {
        var result = Run("""
            let m = str.capture("a", "(a)(b)?");
            let result = m.groups[2].value == null;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Capture_OptionalGroupNotMatched_IndexIsNegativeOne()
    {
        var result = Run("""
            let m = str.capture("a", "(a)(b)?");
            let result = m.groups[2].index;
        """);
        Assert.Equal(-1L, result);
    }

    [Fact]
    public void Capture_NoMatch_ReturnsNull()
    {
        var result = Run("""
            let result = str.capture("hello", "\\d+") == null;
        """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Capture_InvalidPattern_Throws()
    {
        RunExpectingError("""str.capture("hello", "(unclosed");""");
    }

    [Fact]
    public void Capture_NonStringFirst_Throws()
    {
        RunExpectingError("""str.capture(42, "\\d+");""");
    }

    [Fact]
    public void Capture_NonStringSecond_Throws()
    {
        RunExpectingError("""str.capture("hello", 42);""");
    }

    [Fact]
    public void Capture_GroupsCount()
    {
        var result = Run("""
            let m = str.capture("version 1.23", "(\\d+)\\.(\\d+)");
            let result = len(m.groups);
        """);
        Assert.Equal(3L, result);
    }

    // ── str.captureAll ────────────────────────────────────────────────────

    [Fact]
    public void CaptureAll_MultipleMatches_Count()
    {
        var result = Run("""
            let matches = str.captureAll("a@b c@d", "(\\w+)@(\\w+)");
            let result = len(matches);
        """);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void CaptureAll_MultipleMatches_FirstValue()
    {
        var result = Run("""
            let matches = str.captureAll("a@b c@d", "(\\w+)@(\\w+)");
            let result = matches[0].value;
        """);
        Assert.Equal("a@b", result);
    }

    [Fact]
    public void CaptureAll_MultipleMatches_SecondValue()
    {
        var result = Run("""
            let matches = str.captureAll("a@b c@d", "(\\w+)@(\\w+)");
            let result = matches[1].value;
        """);
        Assert.Equal("c@d", result);
    }

    [Fact]
    public void CaptureAll_MultipleMatches_Groups()
    {
        var result = Run("""
            let matches = str.captureAll("a@b c@d", "(\\w+)@(\\w+)");
            let result = matches[0].groups[1].value;
        """);
        Assert.Equal("a", result);
    }

    [Fact]
    public void CaptureAll_NamedGroups()
    {
        var result = Run("""
            let matches = str.captureAll("a@b c@d", "(?<user>\\w+)@(?<domain>\\w+)");
            let result = matches[1].namedGroups["user"];
        """);
        Assert.Equal("c", result);
    }

    [Fact]
    public void CaptureAll_NoMatches_EmptyArray()
    {
        var result = Run("""
            let matches = str.captureAll("hello", "\\d+");
            let result = len(matches);
        """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void CaptureAll_InvalidPattern_Throws()
    {
        RunExpectingError("""str.captureAll("hello", "(unclosed");""");
    }

    // ── Optional Args ────────────────────────────────────────────────────────

    [Fact]
    public void Split_WithLimit_LimitsSegments()
    {
        var result = Run("let result = str.split(\"a,b,c,d\", \",\", 2);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("b", list[1]);
        Assert.Equal("c,d", list[2]);
    }

    [Fact]
    public void Replace_WithCount1_ReplacesOneOccurrence()
    {
        var result = Run("let result = str.replace(\"aaa\", \"a\", \"b\", 1);");
        Assert.Equal("baa", result);
    }

    [Fact]
    public void Replace_WithCount2_ReplacesTwoOccurrences()
    {
        var result = Run("let result = str.replace(\"aaa\", \"a\", \"b\", 2);");
        Assert.Equal("bba", result);
    }

    [Fact]
    public void Contains_IgnoreCase_True_Matches()
    {
        var result = Run("let result = str.contains(\"Hello\", \"hello\", true);");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Contains_IgnoreCase_False_NoMatch()
    {
        var result = Run("let result = str.contains(\"Hello\", \"hello\", false);");
        Assert.Equal(false, result);
    }

    [Fact]
    public void StartsWith_IgnoreCase_True_Matches()
    {
        var result = Run("let result = str.startsWith(\"Hello\", \"hello\", true);");
        Assert.Equal(true, result);
    }

    [Fact]
    public void EndsWith_IgnoreCase_True_Matches()
    {
        var result = Run("let result = str.endsWith(\"Hello.TXT\", \".txt\", true);");
        Assert.Equal(true, result);
    }

    [Fact]
    public void IndexOf_WithStartIndex_SkipsEarlierOccurrence()
    {
        var result = Run("let result = str.indexOf(\"hello world\", \"o\", 5);");
        Assert.Equal(7L, result);
    }

    [Fact]
    public void LastIndexOf_WithStartIndex_SearchesBackward()
    {
        var result = Run("let result = str.lastIndexOf(\"hello world\", \"o\", 6);");
        Assert.Equal(4L, result);
    }

    [Fact]
    public void Trim_WithChars_TrimsSpecifiedChars()
    {
        var result = Run("let result = str.trim(\"xxhelloxx\", \"x\");");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void TrimStart_WithChars_TrimsLeadingChars()
    {
        var result = Run("let result = str.trimStart(\"xxhello\", \"x\");");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void TrimEnd_WithChars_TrimsTrailingChars()
    {
        var result = Run("let result = str.trimEnd(\"helloxx\", \"x\");");
        Assert.Equal("hello", result);
    }
}
