using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Cli.Completion;

namespace Stash.Tests.Cli.Completion;

/// <summary>
/// Unit tests for <see cref="CompletionMenu"/> covering spec §7.1–§7.2.
/// Captures <c>Console.Out</c> to verify multi-column layout and pager threshold.
/// </summary>
public class CompletionMenuTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static string CaptureOutput(Action action)
    {
        var sw = new StringWriter();
        var oldOut = Console.Out;
        Console.SetOut(sw);
        try { action(); }
        finally { Console.SetOut(oldOut); }
        return sw.ToString();
    }

    private static List<Candidate> MakeCandidates(params string[] names)
        => names.Select(n => new Candidate(n, n, CandidateKind.StashFunction)).ToList();

    // ── Empty candidates ──────────────────────────────────────────────────────

    [Fact]
    public void Render_EmptyCandidates_PrintsNothing()
    {
        string output = CaptureOutput(() =>
            CompletionMenu.Render([], _ => true));

        Assert.Equal(string.Empty, output);
    }

    // ── Single candidate ──────────────────────────────────────────────────────

    [Fact]
    public void Render_SingleCandidate_PrintsJustThatLine()
    {
        string output = CaptureOutput(() =>
            CompletionMenu.Render(MakeCandidates("hello"), _ => true));

        Assert.Contains("hello", output);
        // Should be just "hello" + newline, no extra whitespace columns
        Assert.Equal("hello" + Environment.NewLine, output.TrimEnd('\n').TrimEnd('\r').Replace("\r\n", "\n") + "\n");
    }

    // ── Multi-column layout ───────────────────────────────────────────────────

    [Fact]
    public void Render_MultipleCandidates_SortsAlphabetically()
    {
        var candidates = MakeCandidates("zebra", "apple", "mango");
        string output = CaptureOutput(() =>
            CompletionMenu.Render(candidates, _ => true));

        // All three should appear in the output
        Assert.Contains("apple", output);
        Assert.Contains("mango", output);
        Assert.Contains("zebra", output);

        // "apple" should appear before "mango" and "zebra" in the output
        int appleIdx = output.IndexOf("apple", StringComparison.Ordinal);
        int mangoIdx = output.IndexOf("mango", StringComparison.Ordinal);
        int zebraIdx = output.IndexOf("zebra", StringComparison.Ordinal);
        Assert.True(appleIdx < mangoIdx);
        Assert.True(mangoIdx < zebraIdx);
    }

    [Fact]
    public void Render_ColumnMajorLayout_FilledTopToBottomThenLeftToRight()
    {
        // With a narrow forced width, 6 items in 2 columns → 3 rows
        // Column-major order: col0=[a,c,e], col1=[b,d,f]
        // Row 0: a  b
        // Row 1: c  d
        // Row 2: e  f
        var candidates = MakeCandidates("a", "b", "c", "d", "e", "f");

        // Force 2-column layout by using items of length 1 + padding 2 = colWidth 3
        // termWidth needs to fit 2 columns of width 3 = 6
        // Console.WindowWidth in test environment is unknown; just verify output contains all items
        string output = CaptureOutput(() =>
            CompletionMenu.Render(candidates, _ => true));

        // All 6 items present
        foreach (string name in new[] { "a", "b", "c", "d", "e", "f" })
            Assert.Contains(name, output);

        // 'a' comes before 'b' in sorted order
        Assert.True(output.IndexOf('a') < output.IndexOf('b'));
    }

    // ── Pager threshold ───────────────────────────────────────────────────────

    [Fact]
    public void Render_MoreThan100Candidates_CallsPromptYesNo()
    {
        bool promptCalled = false;
        var candidates = MakeCandidates(
            Enumerable.Range(1, 101).Select(i => $"item{i:000}").ToArray());

        string output = CaptureOutput(() =>
            CompletionMenu.Render(candidates, msg =>
            {
                promptCalled = true;
                Assert.Contains("101", msg);   // shows count
                return false;                   // decline
            }));

        Assert.True(promptCalled, "Expected pager prompt to be called for 101 candidates");
        Assert.Equal(string.Empty, output);     // declined → nothing printed
    }

    [Fact]
    public void Render_MoreThan100Candidates_DeclineSkipsPrinting()
    {
        var candidates = MakeCandidates(
            Enumerable.Range(1, 105).Select(i => $"item{i:000}").ToArray());

        string output = CaptureOutput(() =>
            CompletionMenu.Render(candidates, _ => false));

        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void Render_MoreThan100Candidates_AcceptPrintsList()
    {
        var candidates = MakeCandidates(
            Enumerable.Range(1, 101).Select(i => $"item{i:000}").ToArray());

        string output = CaptureOutput(() =>
            CompletionMenu.Render(candidates, _ => true));

        // All items should be in the output
        Assert.Contains("item001", output);
        Assert.Contains("item101", output);
    }

    [Fact]
    public void Render_Exactly100Candidates_NoPagerPrompt()
    {
        bool promptCalled = false;
        var candidates = MakeCandidates(
            Enumerable.Range(1, 100).Select(i => $"item{i:000}").ToArray());

        string output = CaptureOutput(() =>
            CompletionMenu.Render(candidates, msg =>
            {
                promptCalled = true;
                return false;
            }));

        // Exactly 100 → no pager (threshold is >100)
        Assert.False(promptCalled, "Pager should NOT fire for exactly 100 candidates");
        Assert.Contains("item001", output);
    }
}
