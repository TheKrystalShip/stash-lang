namespace Stash.Tests.Stdlib;

/// <summary>
/// Tests for the <c>re</c> namespace: re.match, re.matchAll, re.test, re.replace,
/// re.capture, and re.captureAll.
/// </summary>
public class RegexNamespaceTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // re.match
    // =========================================================================

    [Fact]
    public void ReMatch_FindsFirstDigit_ReturnsMatchedString()
    {
        var result = Run("""let result = re.match("abc123def", "\\d+");""");
        Assert.Equal("123", result);
    }

    [Fact]
    public void ReMatch_NoMatch_ReturnsNull()
    {
        var result = Run("""let result = re.match("abcdef", "\\d+");""");
        Assert.Null(result);
    }

    // =========================================================================
    // re.matchAll
    // =========================================================================

    [Fact]
    public void ReMatchAll_MultipleMatches_ReturnsCorrectCount()
    {
        var result = Run("""
            let matches = re.matchAll("a1b2c3", "\\d");
            let result = len(matches);
        """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void ReMatchAll_NoMatch_ReturnsEmptyArray()
    {
        var result = Run("""
            let matches = re.matchAll("abcdef", "\\d");
            let result = len(matches);
        """);
        Assert.Equal(0L, result);
    }

    // =========================================================================
    // re.test
    // =========================================================================

    [Fact]
    public void ReTest_HasMatch_ReturnsTrue()
    {
        var result = Run("""let result = re.test("hello123", "\\d+");""");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ReTest_NoMatch_ReturnsFalse()
    {
        var result = Run("""let result = re.test("hello", "\\d+");""");
        Assert.Equal(false, result);
    }

    // =========================================================================
    // re.replace
    // =========================================================================

    [Fact]
    public void ReReplace_AllMatches_ReturnsReplacedString()
    {
        var result = Run("""let result = re.replace("abc123def", "\\d+", "_");""");
        Assert.Equal("abc_def", result);
    }

    // =========================================================================
    // re.capture
    // =========================================================================

    [Fact]
    public void ReCapture_FindsMatch_ReturnsRegexMatchValue()
    {
        var result = Run("""
            let m = re.capture("abc123def", "\\d+");
            let result = m.value;
        """);
        Assert.Equal("123", result);
    }

    [Fact]
    public void ReCapture_FindsMatch_ReturnsCorrectIndex()
    {
        var result = Run("""
            let m = re.capture("abc123def", "\\d+");
            let result = m.index;
        """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void ReCapture_NoMatch_ReturnsNull()
    {
        var result = Run("""let result = re.capture("hello", "\\d+") == null;""");
        Assert.Equal(true, result);
    }

    // =========================================================================
    // re.captureAll
    // =========================================================================

    [Fact]
    public void ReCaptureAll_MultipleMatches_ReturnsCorrectCount()
    {
        var result = Run("""
            let matches = re.captureAll("a@b c@d", "(\\w+)@(\\w+)");
            let result = len(matches);
        """);
        Assert.Equal(2L, result);
    }

    // =========================================================================
    // Error handling
    // =========================================================================

    [Fact]
    public void ReMatch_InvalidPattern_ThrowsError()
    {
        RunExpectingError("""re.match("hello", "(unclosed");""");
    }
}
