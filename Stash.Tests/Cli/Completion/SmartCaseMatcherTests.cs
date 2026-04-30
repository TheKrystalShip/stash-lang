using Stash.Cli.Completion;

namespace Stash.Tests.Cli.Completion;

/// <summary>
/// Unit tests for <see cref="SmartCaseMatcher"/> covering smart-case prefix matching
/// and longest-common-prefix computation per spec §15.1.
/// </summary>
public class SmartCaseMatcherTests
{
    // ── Matches — case-insensitive (all-lowercase prefix) ────────────────────

    [Fact]
    public void Matches_LowerPrefix_MatchesCaseInsensitive()
        => Assert.True(SmartCaseMatcher.Matches("foo", "foobar"));

    [Fact]
    public void Matches_LowerPrefix_MatchesMixedCaseCandidate()
        => Assert.True(SmartCaseMatcher.Matches("foo", "FooBar"));

    [Fact]
    public void Matches_LowerPrefix_DoesNotMatchUnrelated()
        => Assert.False(SmartCaseMatcher.Matches("foo", "barfoo"));

    // ── Matches — case-sensitive (prefix contains uppercase) ─────────────────

    [Fact]
    public void Matches_UpperPrefix_DoesNotMatchLowerCandidate()
        => Assert.False(SmartCaseMatcher.Matches("FOO", "foobar"));

    [Fact]
    public void Matches_MixedPrefix_Foo_DoesNotMatchFOOBAR()
        => Assert.False(SmartCaseMatcher.Matches("Foo", "FOOBAR"));

    [Fact]
    public void Matches_MixedPrefix_Foo_MatchesFooBar()
        => Assert.True(SmartCaseMatcher.Matches("Foo", "FooBar"));

    [Fact]
    public void Matches_MixedPrefix_Foo_DoesNotMatchfoobar()
        => Assert.False(SmartCaseMatcher.Matches("Foo", "foobar"));

    // ── Matches — edge cases ──────────────────────────────────────────────────

    [Fact]
    public void Matches_EmptyPrefix_AlwaysTrue()
        => Assert.True(SmartCaseMatcher.Matches(string.Empty, "anything"));

    [Fact]
    public void Matches_PrefixLongerThanCandidate_ReturnsFalse()
        => Assert.False(SmartCaseMatcher.Matches("foobar", "foo"));

    [Fact]
    public void Matches_EqualStrings_LowerReturnsTrue()
        => Assert.True(SmartCaseMatcher.Matches("abc", "abc"));

    [Fact]
    public void Matches_EqualStrings_UpperCaseSensitiveReturnsTrue()
        => Assert.True(SmartCaseMatcher.Matches("ABC", "ABC"));

    // ── HasUpper ─────────────────────────────────────────────────────────────

    [Fact]
    public void HasUpper_AllLower_ReturnsFalse()
        => Assert.False(SmartCaseMatcher.HasUpper("foobar"));

    [Fact]
    public void HasUpper_WithUpper_ReturnsTrue()
        => Assert.True(SmartCaseMatcher.HasUpper("fooBar"));

    [Fact]
    public void HasUpper_AllUpper_ReturnsTrue()
        => Assert.True(SmartCaseMatcher.HasUpper("FOOBAR"));

    [Fact]
    public void HasUpper_EmptyString_ReturnsFalse()
        => Assert.False(SmartCaseMatcher.HasUpper(string.Empty));

    // ── LongestCommonPrefix — case-sensitive ─────────────────────────────────

    [Fact]
    public void Lcp_CaseSensitive_BasicThreeStrings()
    {
        string result = SmartCaseMatcher.LongestCommonPrefix(
            ["foo", "foobar", "foobaz"], caseSensitive: true);
        Assert.Equal("foo", result);
    }

    [Fact]
    public void Lcp_CaseSensitive_NoCommonPrefix()
    {
        string result = SmartCaseMatcher.LongestCommonPrefix(
            ["alpha", "beta", "gamma"], caseSensitive: true);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Lcp_CaseSensitive_DifferentCase_NoMatch()
    {
        // "Foo" vs "foo" case-sensitively share 0 chars (F != f).
        string result = SmartCaseMatcher.LongestCommonPrefix(
            ["Foo", "foo"], caseSensitive: true);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Lcp_CaseSensitive_SingleElement_ReturnsFull()
    {
        string result = SmartCaseMatcher.LongestCommonPrefix(
            ["hello"], caseSensitive: true);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Lcp_CaseSensitive_EmptySequence_ReturnsEmpty()
    {
        string result = SmartCaseMatcher.LongestCommonPrefix(
            Array.Empty<string>(), caseSensitive: true);
        Assert.Equal(string.Empty, result);
    }

    // ── LongestCommonPrefix — case-insensitive (smart-case lower input) ──────

    [Fact]
    public void Lcp_CaseInsensitive_PreservesFirstStringCasing()
    {
        // "foo" is first; result preserves "foo"'s casing.
        string result = SmartCaseMatcher.LongestCommonPrefix(
            ["foo", "Foo"], caseSensitive: false);
        Assert.Equal("foo", result);
    }

    [Fact]
    public void Lcp_CaseInsensitive_ThreeStrings_LowerFirst()
    {
        string result = SmartCaseMatcher.LongestCommonPrefix(
            ["foo", "foobar", "foobaz"], caseSensitive: false);
        Assert.Equal("foo", result);
    }

    [Fact]
    public void Lcp_CaseInsensitive_MixedCaseCandidates_LcpFromFirst()
    {
        // All three start with "pr" case-insensitively; first is "print".
        string result = SmartCaseMatcher.LongestCommonPrefix(
            ["print", "println", "PRINTF"], caseSensitive: false);
        Assert.Equal("print", result);
    }

    [Fact]
    public void Lcp_CaseInsensitive_EmptySequence_ReturnsEmpty()
    {
        string result = SmartCaseMatcher.LongestCommonPrefix(
            Array.Empty<string>(), caseSensitive: false);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Lcp_CaseInsensitive_TruncatesAtFirstDifference()
    {
        // "fooA" vs "foob": 'A' vs 'b' differ even case-insensitively.
        string result = SmartCaseMatcher.LongestCommonPrefix(
            ["fooA", "foob"], caseSensitive: false);
        Assert.Equal("foo", result);
    }
}
