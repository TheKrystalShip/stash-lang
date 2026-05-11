namespace Stash.Tests.Parsing;

using Stash.Common;
using Stash.Parsing;
using Stash.Parsing.AST;

/// <summary>
/// Unit tests for <see cref="DocCommentMetadata.Extract"/>.
/// </summary>
public class DocCommentMetadataTests
{
    private static readonly SourceSpan TestSpan = new("test", 1, 1, 1, 1);

    [Fact]
    public void Extract_NullInput_ReturnsNullPair()
    {
        var (doc, throws) = DocCommentMetadata.Extract(null, TestSpan);
        Assert.Null(doc);
        Assert.Null(throws);
    }

    [Fact]
    public void Extract_WhitespaceOnlyInput_ReturnsNullPair()
    {
        var (doc, throws) = DocCommentMetadata.Extract("   \n  \n  ", TestSpan);
        Assert.Null(doc);
        Assert.Null(throws);
    }

    [Fact]
    public void Extract_NoThrowsTag_ReturnsProseAndNullThrows()
    {
        const string raw = "Does something useful.\n@param x the input\n@return the result";
        var (doc, throws) = DocCommentMetadata.Extract(raw, TestSpan);
        Assert.Equal(raw, doc);
        Assert.Null(throws);
    }

    [Fact]
    public void Extract_SingleThrows_ExtractedAndRemovedFromProse()
    {
        const string raw = "Parse an integer.\n@throws ParseError if input is invalid\n@return the parsed int";
        var (doc, throws) = DocCommentMetadata.Extract(raw, TestSpan);

        // @throws line is NOT in prose
        Assert.NotNull(doc);
        Assert.DoesNotContain("@throws", doc);
        Assert.Contains("Parse an integer.", doc);

        // Throws list has one entry
        Assert.NotNull(throws);
        Assert.Single(throws!);
        Assert.Equal("ParseError", throws![0].ErrorType);
        Assert.Equal("if input is invalid", throws[0].Description);
    }

    [Fact]
    public void Extract_CommaSeparated_SplitIntoMultipleEntries()
    {
        const string raw = "@throws ParseError, ValueError if input is malformed";
        var (_, throws) = DocCommentMetadata.Extract(raw, TestSpan);

        Assert.NotNull(throws);
        Assert.Equal(2, throws!.Count);
        Assert.Equal("ParseError", throws[0].ErrorType);
        Assert.Equal("ValueError", throws[1].ErrorType);
        // Both share the same description
        Assert.Equal("if input is malformed", throws[0].Description);
        Assert.Equal("if input is malformed", throws[1].Description);
    }

    [Fact]
    public void Extract_MultipleSameType_PreservedAsSeparateEntries()
    {
        const string raw = "@throws IOError if file missing\n@throws IOError if file locked";
        var (_, throws) = DocCommentMetadata.Extract(raw, TestSpan);

        Assert.NotNull(throws);
        Assert.Equal(2, throws!.Count);
        Assert.All(throws, e => Assert.Equal("IOError", e.ErrorType));
        Assert.Equal("if file missing", throws[0].Description);
        Assert.Equal("if file locked", throws[1].Description);
    }

    [Fact]
    public void Extract_DescriptionOmitted_DescriptionIsNull()
    {
        const string raw = "@throws IOError";
        var (_, throws) = DocCommentMetadata.Extract(raw, TestSpan);

        Assert.NotNull(throws);
        Assert.Single(throws!);
        Assert.Equal("IOError", throws![0].ErrorType);
        Assert.Null(throws[0].Description);
    }

    [Fact]
    public void Extract_PreservesParamAndReturnLines()
    {
        const string raw = "Summary.\n@param path the file\n@return string content\n@throws IOError on read failure";
        var (doc, throws) = DocCommentMetadata.Extract(raw, TestSpan);

        Assert.NotNull(doc);
        Assert.Contains("@param path the file", doc);
        Assert.Contains("@return string content", doc);
        Assert.DoesNotContain("@throws", doc);

        Assert.NotNull(throws);
        Assert.Single(throws!);
        Assert.Equal("IOError", throws![0].ErrorType);
    }

    [Fact]
    public void Extract_ThrowsOnlyTag_DocumentationIsNull()
    {
        const string raw = "@throws IOError on failure";
        var (doc, throws) = DocCommentMetadata.Extract(raw, TestSpan);

        Assert.Null(doc); // no prose lines remain
        Assert.NotNull(throws);
        Assert.Single(throws!);
    }

    [Fact]
    public void Extract_MultipleThrowsTags_AllExtracted()
    {
        const string raw = "Does a thing.\n@throws IOError on read fail\n@throws ValueError on bad arg";
        var (doc, throws) = DocCommentMetadata.Extract(raw, TestSpan);

        Assert.NotNull(doc);
        Assert.Contains("Does a thing.", doc);
        Assert.DoesNotContain("@throws", doc);

        Assert.NotNull(throws);
        Assert.Equal(2, throws!.Count);
        Assert.Equal("IOError", throws[0].ErrorType);
        Assert.Equal("ValueError", throws[1].ErrorType);
    }

    [Fact]
    public void Extract_SpanIsPreservedOnAllEntries()
    {
        var span = new SourceSpan("test", 5, 1, 5, 20);
        const string raw = "@throws IOError foo\n@throws ValueError bar";
        var (_, throws) = DocCommentMetadata.Extract(raw, span);

        Assert.NotNull(throws);
        Assert.Equal(2, throws!.Count);
        Assert.Equal(span, throws[0].Span);
        Assert.Equal(span, throws[1].Span);
    }
}
