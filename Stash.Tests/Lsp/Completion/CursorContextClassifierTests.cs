namespace Stash.Tests.Lsp.Completion;

using Stash.Lsp.Completion;
using Xunit;

/// <summary>
/// Unit tests for <see cref="CursorContextClassifier.Classify"/>, verifying that it
/// returns <see cref="CompletionMode"/> values in the correct precedence order:
/// ImportString &gt; Dot &gt; AfterExtend &gt; AfterIs &gt; Default.
/// </summary>
public class CursorContextClassifierTests
{
    // ── Null / empty input ───────────────────────────────────────────────────────

    [Fact]
    public void Classify_NullLine_ReturnsDefault()
    {
        var mode = CursorContextClassifier.Classify(null, 0, out var prefix);
        Assert.Equal(CompletionMode.Default, mode);
        Assert.Null(prefix);
    }

    // ── ImportString mode ────────────────────────────────────────────────────────

    [Fact]
    public void Classify_InsideImportString_ReturnsImportString()
    {
        // from "|mypkg" — cursor at col 6 (inside the string)
        var line = @"from ""mypkg""";
        var mode = CursorContextClassifier.Classify(line, 7, out var prefix);
        Assert.Equal(CompletionMode.ImportString, mode);
        Assert.Null(prefix);
    }

    [Fact]
    public void Classify_InsidePlainString_ReturnsImportString()
    {
        // Cursor inside a plain string (not import context) still returns ImportString
        // so the provider's own IsImportContext check can suppress it.
        var line = @"let x = ""hello""";
        var mode = CursorContextClassifier.Classify(line, 11, out var prefix);
        Assert.Equal(CompletionMode.ImportString, mode);
        Assert.Null(prefix);
    }

    [Fact]
    public void Classify_InsideString_TakesPrecedenceOverDot()
    {
        // "arr." — the dot is inside a string; ImportString must win, not Dot.
        var line = @"from ""arr.""";
        var mode = CursorContextClassifier.Classify(line, 10, out var prefix);
        Assert.Equal(CompletionMode.ImportString, mode);
        Assert.Null(prefix);
    }

    // ── Dot mode ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_AfterDot_ReturnsDot_WithPrefix()
    {
        // "arr." — cursor is at position 4 (after the dot)
        var line = "arr.";
        var mode = CursorContextClassifier.Classify(line, 4, out var prefix);
        Assert.Equal(CompletionMode.Dot, mode);
        Assert.Equal("arr", prefix);
    }

    [Fact]
    public void Classify_AfterDotWithPartialMember_ReturnsDot_WithPrefix()
    {
        // "math.sq" — cursor at position 7 (after 'sq', which the editor trims before dot)
        // The dot is at col-1 when col==5 (after "math.")
        var line = "math.";
        var mode = CursorContextClassifier.Classify(line, 5, out var prefix);
        Assert.Equal(CompletionMode.Dot, mode);
        Assert.Equal("math", prefix);
    }

    [Fact]
    public void Classify_NoDot_DoesNotReturnDot()
    {
        var line = "let x = ";
        var mode = CursorContextClassifier.Classify(line, 8, out var prefix);
        Assert.NotEqual(CompletionMode.Dot, mode);
        Assert.Null(prefix);
    }

    // ── AfterExtend mode ─────────────────────────────────────────────────────────

    [Fact]
    public void Classify_AfterExtend_ReturnsAfterExtend()
    {
        // "extend " — cursor at position 7
        var line = "extend ";
        var mode = CursorContextClassifier.Classify(line, 7, out var prefix);
        Assert.Equal(CompletionMode.AfterExtend, mode);
        Assert.Null(prefix);
    }

    [Fact]
    public void Classify_AfterExtend_WithPartialTypeName_ReturnsAfterExtend()
    {
        // "extend str" — cursor at position 10
        var line = "extend str";
        var mode = CursorContextClassifier.Classify(line, 10, out var prefix);
        Assert.Equal(CompletionMode.AfterExtend, mode);
        Assert.Null(prefix);
    }

    [Fact]
    public void Classify_AfterExtend_TakesPrecedenceOverAfterIs()
    {
        // If somehow "extend" and "is" both match (contrived), AfterExtend wins.
        // This verifies ordering; in practice they can't both match on the same token.
        var line = "extend ";
        var mode = CursorContextClassifier.Classify(line, 7, out _);
        Assert.Equal(CompletionMode.AfterExtend, mode);
    }

    [Fact]
    public void Classify_NotAfterExtend_WhenWordIsLonger()
    {
        // "myextend " — "extend" is not a whole word here
        var line = "myextend ";
        var mode = CursorContextClassifier.Classify(line, 9, out _);
        Assert.NotEqual(CompletionMode.AfterExtend, mode);
    }

    // ── AfterIs mode ─────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_AfterIs_ReturnsAfterIs()
    {
        // "x is " — cursor at position 5
        var line = "x is ";
        var mode = CursorContextClassifier.Classify(line, 5, out var prefix);
        Assert.Equal(CompletionMode.AfterIs, mode);
        Assert.Null(prefix);
    }

    [Fact]
    public void Classify_AfterIs_WithPartialTypeName_ReturnsAfterIs()
    {
        // "x is str" — cursor at position 8
        var line = "x is str";
        var mode = CursorContextClassifier.Classify(line, 8, out var prefix);
        Assert.Equal(CompletionMode.AfterIs, mode);
        Assert.Null(prefix);
    }

    [Fact]
    public void Classify_NotAfterIs_WhenWordIsLonger()
    {
        // "this is " — "is" is part of "this"; must NOT trigger AfterIs
        // Actually "this" precedes, but "is" is a whole word here. Testing "exists is "
        var line = "exists is ";
        var mode = CursorContextClassifier.Classify(line, 10, out _);
        Assert.Equal(CompletionMode.AfterIs, mode);
    }

    [Fact]
    public void Classify_IsInsideIdentifier_NotAfterIs()
    {
        // "this " — "is" inside "this" must not match
        var line = "this ";
        var mode = CursorContextClassifier.Classify(line, 5, out _);
        Assert.NotEqual(CompletionMode.AfterIs, mode);
    }

    // ── Default mode ─────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_PlainIdentifier_ReturnsDefault()
    {
        var line = "let x = ";
        var mode = CursorContextClassifier.Classify(line, 8, out var prefix);
        Assert.Equal(CompletionMode.Default, mode);
        Assert.Null(prefix);
    }

    [Fact]
    public void Classify_EmptyLine_ReturnsDefault()
    {
        var mode = CursorContextClassifier.Classify("", 0, out var prefix);
        Assert.Equal(CompletionMode.Default, mode);
        Assert.Null(prefix);
    }

    // ── Helper internals ─────────────────────────────────────────────────────────

    [Fact]
    public void IsInsideString_OpenQuoteBeforeCursor_ReturnsTrue()
    {
        // |"hello" — cursor at col 2 (between h and e)
        Assert.True(CursorContextClassifier.IsInsideString("\"hello\"", 3));
    }

    [Fact]
    public void IsInsideString_CursorOutsideString_ReturnsFalse()
    {
        Assert.False(CursorContextClassifier.IsInsideString("\"hello\" + x", 10));
    }

    [Fact]
    public void IsInsideString_InterpolationExpression_ReturnsFalse()
    {
        // $"hello {expr|}" — cursor inside interpolation braces → not in string text
        Assert.False(CursorContextClassifier.IsInsideString("$\"hello {expr}\"", 13));
    }

    [Fact]
    public void GetDotPrefix_NoDot_ReturnsNull()
    {
        Assert.Null(CursorContextClassifier.GetDotPrefix("arr", 3));
    }

    [Fact]
    public void GetDotPrefix_DotAfterIdentifier_ReturnsIdentifier()
    {
        Assert.Equal("arr", CursorContextClassifier.GetDotPrefix("arr.", 4));
    }

    [Fact]
    public void IsAfterIsKeyword_IsAsWholeWord_ReturnsTrue()
    {
        Assert.True(CursorContextClassifier.IsAfterIsKeyword("x is ", 5));
    }

    [Fact]
    public void IsAfterIsKeyword_IsInsideLongerWord_ReturnsFalse()
    {
        // "this" ends at col 4, then space. "is" is not a whole-word token.
        Assert.False(CursorContextClassifier.IsAfterIsKeyword("this ", 5));
    }

    [Fact]
    public void IsAfterExtendKeyword_ExtendAsWholeWord_ReturnsTrue()
    {
        Assert.True(CursorContextClassifier.IsAfterExtendKeyword("extend ", 7));
    }

    [Fact]
    public void IsAfterExtendKeyword_ExtendInsideLongerWord_ReturnsFalse()
    {
        Assert.False(CursorContextClassifier.IsAfterExtendKeyword("myextend ", 9));
    }
}
