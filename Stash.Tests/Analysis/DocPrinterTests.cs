using System.Collections.Generic;
using System.Text;
using Stash.Analysis.Formatting;
using Xunit;

namespace Stash.Tests.Analysis;

public class DocPrinterTests
{
    // ── 1. Basic text ─────────────────────────────────────────────

    [Fact]
    public void Print_SimpleText_ReturnsText()
    {
        var doc = Doc.Text("hello");
        string result = DocPrinter.Print(doc);
        Assert.Equal("hello", result);
    }

    // ── 2. HardLine ───────────────────────────────────────────────

    [Fact]
    public void Print_HardLine_EmitsNewline()
    {
        var doc = Doc.Concat(Doc.Text("a"), Doc.HardLine, Doc.Text("b"));
        string result = DocPrinter.Print(doc);
        Assert.Equal("a\nb", result);
    }

    // ── 3. IndentedHardLine ───────────────────────────────────────

    [Fact]
    public void Print_IndentedHardLine_EmitsIndent()
    {
        var doc = Doc.Concat(
            Doc.Text("{"),
            Doc.Indent(Doc.Concat(Doc.HardLine, Doc.Text("x"))),
            Doc.HardLine,
            Doc.Text("}"));
        string result = DocPrinter.Print(doc);
        Assert.Equal("{\n  x\n}", result);
    }

    // ── 4. Group fits → flat mode ─────────────────────────────────

    [Fact]
    public void Print_GroupFits_StaysFlat()
    {
        var doc = Doc.Group(Doc.Concat(Doc.Text("a"), Doc.Line, Doc.Text("b")));
        string result = DocPrinter.Print(doc, printWidth: 80);
        Assert.Equal("a b", result);
    }

    // ── 5. Group does not fit → break mode ───────────────────────

    [Fact]
    public void Print_GroupDoesNotFit_Breaks()
    {
        var doc = Doc.Group(Doc.Concat(Doc.Text("a"), Doc.Line, Doc.Text("b")));
        string result = DocPrinter.Print(doc, printWidth: 2);
        Assert.Equal("a\nb", result);
    }

    // ── 6. SoftLine in flat mode ──────────────────────────────────

    [Fact]
    public void Print_SoftLineFlat_EmitsNothing()
    {
        var doc = Doc.Group(Doc.Concat(Doc.Text("a"), Doc.SoftLine, Doc.Text("b")));
        string result = DocPrinter.Print(doc, printWidth: 80);
        Assert.Equal("ab", result);
    }

    // ── 7. SoftLine in break mode ─────────────────────────────────

    [Fact]
    public void Print_SoftLineBreak_EmitsNewline()
    {
        // Force the group to break: "ab" = 2 chars but width = 1.
        var doc = Doc.Group(Doc.Concat(Doc.Text("a"), Doc.SoftLine, Doc.Text("b")));
        string result = DocPrinter.Print(doc, printWidth: 1);
        Assert.Equal("a\nb", result);
    }

    // ── 8. IfBreak in flat mode ───────────────────────────────────

    [Fact]
    public void Print_IfBreakFlat_UsesFlatContent()
    {
        // Trailing-comma pattern: comma only when the group breaks.
        var doc = Doc.Group(Doc.Concat(
            Doc.Text("["),
            Doc.Indent(Doc.Concat(Doc.SoftLine, Doc.Text("item"), Doc.IfBreak(Doc.Text(","), Doc.Empty))),
            Doc.SoftLine,
            Doc.Text("]")));
        string result = DocPrinter.Print(doc, printWidth: 80);
        Assert.Equal("[item]", result);
    }

    // ── 9. IfBreak in break mode ──────────────────────────────────

    [Fact]
    public void Print_IfBreakBreak_UsesBreakContent()
    {
        // Same doc as above; narrow width forces break → trailing comma appears.
        var doc = Doc.Group(Doc.Concat(
            Doc.Text("["),
            Doc.Indent(Doc.Concat(Doc.SoftLine, Doc.Text("item"), Doc.IfBreak(Doc.Text(","), Doc.Empty))),
            Doc.SoftLine,
            Doc.Text("]")));
        string result = DocPrinter.Print(doc, printWidth: 5);
        Assert.Equal("[\n  item,\n]", result);
    }

    // ── 10. Nested indent accumulates ────────────────────────────

    [Fact]
    public void Print_NestedIndent_Accumulates()
    {
        var doc = Doc.Concat(
            Doc.Text("a"),
            Doc.Indent(Doc.Indent(Doc.Concat(Doc.HardLine, Doc.Text("x")))));
        string result = DocPrinter.Print(doc);
        Assert.Equal("a\n    x", result);
    }

    // ── 11. Consecutive HardLines produce no trailing whitespace ──

    [Fact]
    public void Print_ConsecutiveHardLines_NoTrailingWhitespace()
    {
        var doc = Doc.Concat(Doc.Text("a"), Doc.HardLine, Doc.HardLine, Doc.Text("b"));
        string result = DocPrinter.Print(doc);
        Assert.Equal("a\n\nb", result);
        // Second line must be completely empty (no trailing spaces).
        string[] lines = result.Split('\n');
        Assert.Equal("", lines[1]);
    }

    // ── 12. LineSuffix appends after content, before newline ──────

    [Fact]
    public void Print_LineSuffix_AppendsAfterContent()
    {
        var doc = Doc.Concat(
            Doc.Text("code"),
            Doc.LineSuffix(Doc.Text(" // comment")),
            Doc.HardLine,
            Doc.Text("next"));
        string result = DocPrinter.Print(doc);
        Assert.Equal("code // comment\nnext", result);
    }

    // ── 12b. LineSuffix with compound doc ─────────────────────────

    [Fact]
    public void Print_LineSuffixWithConcatDoc_AppendsBeforeNewline()
    {
        var doc = Doc.Concat(
            Doc.Text("code"),
            Doc.LineSuffix(Doc.Concat(Doc.Text(" //"), Doc.Text(" comment"))),
            Doc.HardLine,
            Doc.Text("next"));
        string result = DocPrinter.Print(doc);
        Assert.Equal("code // comment\nnext", result);
    }

    // ── 13. Dedent decreases indent back to base level ────────────

    [Fact]
    public void Print_Dedent_DecreasesIndent()
    {
        var doc = Doc.Concat(
            Doc.Text("start"),
            Doc.Indent(Doc.Concat(
                Doc.HardLine,
                Doc.Text("in"),
                Doc.Dedent(Doc.Concat(
                    Doc.HardLine,
                    Doc.Text("out"))))));
        string result = DocPrinter.Print(doc);
        Assert.Equal("start\n  in\nout", result);
    }

    // ── 14. Tab indent ────────────────────────────────────────────

    [Fact]
    public void Print_TabIndent_UsesTabs()
    {
        var doc = Doc.Concat(
            Doc.Text("a"),
            Doc.Indent(Doc.Concat(Doc.HardLine, Doc.Text("x"))));
        string result = DocPrinter.Print(doc, indentWidth: 1, indentChar: '\t');
        Assert.Equal("a\n\tx", result);
    }

    // ── 15. Empty concat returns empty string ─────────────────────

    [Fact]
    public void Print_EmptyConcat_ReturnsEmpty()
    {
        string result = DocPrinter.Print(Doc.Empty);
        Assert.Equal("", result);
    }

    // ── 16. Fill packs items onto one line when they fit ──────────

    [Fact]
    public void Print_Fill_PacksItems()
    {
        var doc = Doc.Fill(Doc.Text("a"), Doc.Line, Doc.Text("b"), Doc.Line, Doc.Text("c"));
        string result = DocPrinter.Print(doc, printWidth: 80);
        Assert.Equal("a b c", result);
    }

    // ── 17. Fill wraps when items exceed width ────────────────────

    [Fact]
    public void Print_Fill_BreaksWhenExceedingWidth()
    {
        // Each chunk is 5 chars; " " + 5 = 6, which exceeds width 10 from column 5.
        var doc = Doc.Fill(
            Doc.Text("abcde"),
            Doc.Line,
            Doc.Text("fghij"),
            Doc.Line,
            Doc.Text("klmno"));
        string result = DocPrinter.Print(doc, printWidth: 10);
        Assert.Equal("abcde\nfghij\nklmno", result);
    }

    // ── 18. Array formatting fits on one line at width 80 ─────────

    [Fact]
    public void Print_ArrayFormatting_FitsOnOneLine()
    {
        var items = new List<Doc> { Doc.Text("1"), Doc.Text("2"), Doc.Text("3") };
        var sep = Doc.Concat(Doc.Text(","), Doc.Line);
        var joined = Doc.Join(sep, items);
        var doc = Doc.Group(Doc.Concat(
            Doc.Text("["),
            Doc.Indent(Doc.Concat(Doc.SoftLine, joined)),
            Doc.SoftLine,
            Doc.Text("]")));

        string result = DocPrinter.Print(doc, printWidth: 9);
        Assert.Equal("[1, 2, 3]", result);
    }

    // ── 19. Array formatting wraps at narrow width ────────────────

    [Fact]
    public void Print_ArrayFormatting_BreaksAtNarrowWidth()
    {
        var items = new List<Doc> { Doc.Text("1"), Doc.Text("2"), Doc.Text("3") };
        var sep = Doc.Concat(Doc.Text(","), Doc.Line);
        var joined = Doc.Join(sep, items);
        var doc = Doc.Group(Doc.Concat(
            Doc.Text("["),
            Doc.Indent(Doc.Concat(Doc.SoftLine, joined)),
            Doc.SoftLine,
            Doc.Text("]")));

        string result = DocPrinter.Print(doc, printWidth: 8);
        Assert.Equal("[\n  1,\n  2,\n  3\n]", result);
    }

    // ── 20. Deeply nested indent produces correct spacing ─────────

    [Fact]
    public void Print_DeeplyNested_CorrectIndentAtEachLevel()
    {
        const int levels = 10;

        // Build from inside out: innermost is Text("9") wrapped at indent level 9.
        Doc inner = Doc.Concat(Doc.HardLine, Doc.Text("9"));
        for (int i = levels - 2; i >= 1; i--)
            inner = Doc.Concat(Doc.HardLine, Doc.Text(i.ToString()), Doc.Indent(inner));
        Doc doc = Doc.Concat(Doc.Text("0"), Doc.Indent(inner));

        string result = DocPrinter.Print(doc);

        var sb = new StringBuilder();
        sb.Append("0");
        for (int i = 1; i < levels; i++)
            sb.Append($"\n{new string(' ', i * 2)}{i}");
        string expected = sb.ToString();

        Assert.Equal(expected, result);
    }

    // ── 21. Join separates items with separator ───────────────────

    [Fact]
    public void Print_Join_SeparatesWithSeparator()
    {
        var items = new List<Doc> { Doc.Text("a"), Doc.Text("b"), Doc.Text("c") };
        var doc = Doc.Join(Doc.Text(", "), items);
        string result = DocPrinter.Print(doc);
        Assert.Equal("a, b, c", result);
    }

    // ── 22. DocDebugPrinter shows tree structure ───────────────────

    [Fact]
    public void DebugPrint_ShowsStructure()
    {
        var doc = Doc.Group(Doc.Concat(Doc.Text("hello"), Doc.Line, Doc.Text("world")));
        string debug = DocDebugPrinter.Print(doc);

        Assert.Contains("Group(", debug);
        Assert.Contains("Text(\"hello\")", debug);
        Assert.Contains("Line", debug);
        Assert.Contains("Text(\"world\")", debug);
    }
}
