namespace Stash.Tests.Analysis;

using Stash.Analysis;

/// <summary>
/// Regression tests verifying that the formatter preserves <c>@throws</c> doc-comment tags
/// exactly as authored, without reordering, collapsing, or splitting them.
/// Doc comments are handled as trivia by the formatter and are emitted verbatim.
/// </summary>
public class FormatterDocCommentTests
{
    private static string Format(string source, int indentSize = 2) =>
        new StashFormatter(indentSize, useTabs: false).Format(source);

    [Fact]
    public void Formatter_PreservesThrowsTags_NoReordering()
    {
        // @throws B appears before @throws A in the source — formatter must not sort them.
        var source =
            """
            /// Does a thing.
            /// @throws B when B condition
            /// @throws A when A condition
            fn doThing() {
            }
            """;

        var result = Format(source);

        var idxB = result.IndexOf("@throws B", StringComparison.Ordinal);
        var idxA = result.IndexOf("@throws A", StringComparison.Ordinal);
        Assert.True(idxB >= 0, "@throws B should be present");
        Assert.True(idxA >= 0, "@throws A should be present");
        Assert.True(idxB < idxA, "@throws B must appear before @throws A");
    }

    [Fact]
    public void Formatter_PreservesMultipleThrowsTagsForSameType_NoCollapse()
    {
        // Two @throws IOError tags document different conditions — both must be preserved.
        var source =
            """
            /// Does a thing.
            /// @throws IOError when the file does not exist
            /// @throws IOError when the file is unreadable
            fn doThing() {
            }
            """;

        var result = Format(source);

        int firstIdx = result.IndexOf("@throws IOError", StringComparison.Ordinal);
        int secondIdx = result.IndexOf("@throws IOError", firstIdx + 1, StringComparison.Ordinal);
        Assert.True(firstIdx >= 0, "First @throws IOError should be present");
        Assert.True(secondIdx >= 0, "Second @throws IOError should also be present");
    }

    [Fact]
    public void Formatter_PreservesCommaSeparatedThrows_NoSplit()
    {
        // @throws A, B on one line — formatter must not split into two separate @throws lines.
        var source =
            """
            /// Does a thing.
            /// @throws ParseError, ValueError if the input is malformed
            fn doThing() {
            }
            """;

        var result = Format(source);

        Assert.Contains("@throws ParseError, ValueError", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatter_PreservesDocCommentEntirely_RoundTrip()
    {
        // A full doc comment with @param, @return, and @throws must round-trip byte-for-byte
        // (modulo leading/trailing whitespace normalisation that the formatter always applies).
        var source =
            """
            /// Parse a string as an integer.
            /// @param s the input string
            /// @return the parsed integer
            /// @throws ParseError if the string is not a valid integer
            /// @throws ValueError if s is empty
            fn parseInteger(s) {
            }
            """;

        var result = Format(source);

        Assert.Contains("/// Parse a string as an integer.", result, StringComparison.Ordinal);
        Assert.Contains("/// @param s the input string", result, StringComparison.Ordinal);
        Assert.Contains("/// @return the parsed integer", result, StringComparison.Ordinal);
        Assert.Contains("/// @throws ParseError if the string is not a valid integer", result, StringComparison.Ordinal);
        Assert.Contains("/// @throws ValueError if s is empty", result, StringComparison.Ordinal);
    }
}
