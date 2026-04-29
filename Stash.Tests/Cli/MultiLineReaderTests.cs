using System;
using System.Collections.Generic;
using Stash;

namespace Stash.Tests.Cli;

public class MultiLineReaderTests
{
    // Helper: creates a reader backed by a fixed sequence of lines.
    private static MultiLineReader MakeReader(IEnumerable<string?> lines,
        string firstPrompt = "stash> ", string continuationPrompt = "... ")
    {
        var queue = new Queue<string?>(lines);
        return new MultiLineReader(
            _ => queue.Count > 0 ? queue.Dequeue() : null,
            firstPrompt,
            continuationPrompt);
    }

    // --- Single-line completeness ---

    [Fact]
    public void MultiLineReader_SingleLineComplete_ReturnsLine()
    {
        var reader = MakeReader(["x + y"]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal("x + y", result);
    }

    [Fact]
    public void MultiLineReader_BalancedAfterFirstLine_ReturnsImmediately()
    {
        var reader = MakeReader(["(1 + 2)"]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal("(1 + 2)", result);
    }

    [Fact]
    public void MultiLineReader_OnlyExpressionMode_NoBraces_NoFalsePositive()
    {
        var reader = MakeReader(["x + y"]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal("x + y", result);
    }

    // --- EOF ---

    [Fact]
    public void MultiLineReader_EofAtFirstPrompt_ReturnsNull()
    {
        var reader = MakeReader([null]);
        string? result = reader.ReadLogicalLine();
        Assert.Null(result);
    }

    [Fact]
    public void MultiLineReader_EofMidContinuation_ReturnsAccumulated()
    {
        // After "(1 +" the input is incomplete; then EOF on the continuation prompt.
        var reader = MakeReader(["(1 +", null]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal("(1 +", result);
    }

    // --- Trailing backslash ---

    [Fact]
    public void MultiLineReader_TrailingBackslash_ContinuesAndJoinsWithSpace()
    {
        // "foo \" then "bar" → "foo bar"
        var reader = MakeReader([@"foo \", "bar"]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal("foo bar", result);
    }

    [Fact]
    public void MultiLineReader_DoubleTrailingBackslash_TreatedAsLiteral()
    {
        // Even number of trailing backslashes → no continuation.
        var reader = MakeReader([@"foo \\"]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal(@"foo \\", result);
    }

    [Fact]
    public void MultiLineReader_EofAfterBackslashContinuation_ReturnsAccumulated()
    {
        // "foo \" then EOF on continuation prompt.
        var reader = MakeReader([@"foo \", null]);
        string? result = reader.ReadLogicalLine();
        // Backslash stripped and space appended; then EOF → return "foo "
        Assert.Equal("foo ", result);
    }

    // --- Unbalanced delimiters ---

    [Fact]
    public void MultiLineReader_UnbalancedParen_Continues()
    {
        var reader = MakeReader(["(1 +", "2)"]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal("(1 +\n2)", result);
    }

    [Fact]
    public void MultiLineReader_UnbalancedBrace_Continues()
    {
        var reader = MakeReader(["fn x() {", "  return 1", "}"]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal("fn x() {\n  return 1\n}", result);
    }

    [Fact]
    public void MultiLineReader_UnbalancedBracket_Continues()
    {
        var reader = MakeReader(["[1,", "2]"]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal("[1,\n2]", result);
    }

    [Fact]
    public void MultiLineReader_EmptyContinuationLine_KeepsContinuing()
    {
        // An empty line inside an open paren should NOT terminate continuation.
        var reader = MakeReader(["(1", "", "+ 2)"]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal("(1\n\n+ 2)", result);
    }

    // --- Unterminated strings ---

    [Fact]
    public void MultiLineReader_UnterminatedString_Continues()
    {
        var reader = MakeReader(["\"hello", "world\""]);
        string? result = reader.ReadLogicalLine();
        Assert.Equal("\"hello\nworld\"", result);
    }

    // --- HasTrailingContinuationBackslash helper ---

    [Theory]
    [InlineData(@"foo \", true)]           // one trailing backslash → continuation
    [InlineData(@"foo \\", false)]         // two trailing backslashes → literal
    [InlineData(@"foo \\\", true)]         // three → continuation
    [InlineData(@"foo \\\\", false)]       // four → literal
    [InlineData("foo", false)]             // no backslash
    [InlineData("", false)]               // empty string
    [InlineData(@"\", true)]              // lone backslash
    [InlineData(@"\\", false)]            // lone double-backslash
    public void MultiLineReader_HasTrailingContinuationBackslash_VariousCounts(string input, bool expected)
    {
        Assert.Equal(expected, MultiLineReader.HasTrailingContinuationBackslash(input));
    }
}
