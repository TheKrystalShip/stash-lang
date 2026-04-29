using Stash.Cli.Repl;

namespace Stash.Tests.Cli;

/// <summary>
/// Unit tests for <see cref="PromptWidthCalculator.VisibleWidth"/>.
/// Verifies stripping of zero-width OSC regions, ANSI SGR sequences, and
/// documents behavior for edge cases (truncated sequences, combining characters).
/// </summary>
public class PromptWidthCalculatorTests
{
    [Fact]
    public void VisibleWidth_PlainString_ReturnsLength()
    {
        Assert.Equal(5, PromptWidthCalculator.VisibleWidth("hello"));
    }

    [Fact]
    public void VisibleWidth_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, PromptWidthCalculator.VisibleWidth(""));
    }

    [Fact]
    public void VisibleWidth_WithAnsiColorCodes_StripsCodesCountsText()
    {
        // \x1b[31m = red color on, \x1b[0m = reset — both stripped
        Assert.Equal(2, PromptWidthCalculator.VisibleWidth("\x1b[31mhi\x1b[0m"));
    }

    [Fact]
    public void VisibleWidth_OnlyAnsiNoText_ReturnsZero()
    {
        Assert.Equal(0, PromptWidthCalculator.VisibleWidth("\x1b[31m\x1b[0m"));
    }

    [Fact]
    public void VisibleWidth_WithOscShellIntegrationMarkers_StripsMarkersCountsText()
    {
        // OSC 133;A marker wrapped in \x01..\x02 zero-width regions, then "hi"
        const string osc = "\x01\x1b]133;A\x07\x02";
        Assert.Equal(2, PromptWidthCalculator.VisibleWidth(osc + "hi"));
    }

    [Fact]
    public void VisibleWidth_FullPromptWithOscAndAnsi_CorrectCount()
    {
        // Simulates a wrapped prompt: OSC-A + color + "stash> " + reset + OSC-B
        const string oscA = "\x01\x1b]133;A\x07\x02";
        const string oscB = "\x01\x1b]133;B\x07\x02";
        string prompt = oscA + "\x1b[32mstash> \x1b[0m" + oscB;
        Assert.Equal(7, PromptWidthCalculator.VisibleWidth(prompt));
    }

    [Fact]
    public void VisibleWidth_TruncatedAnsiNoClosingM_TreatedAsLiteral()
    {
        // "\x1b[31" has no closing 'm', so the regex does not match.
        // The sequence is treated as literal text: ESC(1) + '[' + '3' + '1' = 4 chars.
        // Document: incomplete ANSI sequences are not stripped by the current implementation.
        Assert.Equal(4, PromptWidthCalculator.VisibleWidth("\x1b[31"));
    }

    [Fact]
    public void VisibleWidth_CombiningChar_UsesStringLength()
    {
        // "e\u0301" is 'e' followed by combining acute accent (U+0301).
        // string.Length counts UTF-16 code units, returning 2 (not 1 grapheme).
        // Document: combining characters count as separate units in this implementation.
        Assert.Equal(2, PromptWidthCalculator.VisibleWidth("e\u0301"));
    }

    [Fact]
    public void VisibleWidth_MultipleAnsiSequences_AllStripped()
    {
        // Bold + red + "X" + reset
        Assert.Equal(1, PromptWidthCalculator.VisibleWidth("\x1b[1m\x1b[31mX\x1b[0m"));
    }

    [Fact]
    public void VisibleWidth_SemicolonSeparatedParams_Stripped()
    {
        // "\x1b[38;5;196m" is a 256-color sequence
        Assert.Equal(3, PromptWidthCalculator.VisibleWidth("\x1b[38;5;196mfoo\x1b[0m"));
    }
}
