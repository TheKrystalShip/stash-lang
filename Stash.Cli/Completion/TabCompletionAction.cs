using System;
using System.Collections.Generic;
using System.Text;

namespace Stash.Cli.Completion;

internal enum TabActionKind
{
    /// <summary>0 candidates → ring the bell.</summary>
    Bell,
    /// <summary>N>1 candidates, first Tab, no LCP progress → no visible change.</summary>
    NoOp,
    /// <summary>1 candidate inserted, or LCP inserted into the buffer.</summary>
    Modified,
    /// <summary>N>1 candidates, second consecutive Tab → caller should render menu.</summary>
    ListedCandidates,
}

internal sealed record TabActionResult(TabActionKind Kind, string? Inserted = null);

/// <summary>
/// Stateless helper that implements the bash-classic Tab state machine (spec §4).
/// Extracted from <see cref="LineEditor"/> so it can be unit-tested without
/// simulating <see cref="Console.ReadKey"/>.
/// </summary>
internal static class TabCompletionAction
{
    /// <summary>
    /// Applies one Tab press against <paramref name="buffer"/> and <paramref name="cursor"/>.
    /// Updates the buffer, cursor, and <paramref name="lastKeyWasTab"/> in-place.
    /// The caller is responsible for ringing the bell and invoking the menu renderer.
    /// </summary>
    /// <param name="buffer">The current line buffer (modified in-place on insert).</param>
    /// <param name="cursor">Current cursor position (updated in-place).</param>
    /// <param name="lastKeyWasTab">True if the previous key was also Tab (updated in-place).</param>
    /// <param name="engine">The completion engine to query.</param>
    /// <param name="renderMenu">Called when the candidate list should be displayed.</param>
    /// <returns>A <see cref="TabActionResult"/> describing what happened.</returns>
    public static TabActionResult Apply(
        StringBuilder buffer,
        ref int cursor,
        ref bool lastKeyWasTab,
        CompletionEngine engine,
        Action<IReadOnlyList<Candidate>> renderMenu)
    {
        CompletionResult result = engine.Complete(buffer.ToString(), cursor);

        // ── 0 candidates ────────────────────────────────────────────────────
        if (result.Candidates.Count == 0)
        {
            lastKeyWasTab = true;
            return new TabActionResult(TabActionKind.Bell);
        }

        // ── 1 candidate → insert it ─────────────────────────────────────────
        if (result.Candidates.Count == 1)
        {
            string insert = result.Candidates[0].Insert;
            int tokenLen = cursor - result.ReplaceStart;
            if (tokenLen > 0)
                buffer.Remove(result.ReplaceStart, tokenLen);
            buffer.Insert(result.ReplaceStart, insert);
            cursor = result.ReplaceStart + insert.Length;
            lastKeyWasTab = false;
            return new TabActionResult(TabActionKind.Modified, insert);
        }

        // ── N>1 candidates ───────────────────────────────────────────────────
        if (lastKeyWasTab)
        {
            // Second consecutive Tab → show candidate list
            renderMenu(result.Candidates);
            lastKeyWasTab = false;
            return new TabActionResult(TabActionKind.ListedCandidates);
        }
        else
        {
            // First Tab — try to insert LCP
            int tokenLen = cursor - result.ReplaceStart;
            if (result.CommonPrefix.Length > tokenLen)
            {
                string lcp = result.CommonPrefix;
                if (tokenLen > 0)
                    buffer.Remove(result.ReplaceStart, tokenLen);
                buffer.Insert(result.ReplaceStart, lcp);
                cursor = result.ReplaceStart + lcp.Length;
                lastKeyWasTab = true;
                return new TabActionResult(TabActionKind.Modified, lcp);
            }
            else
            {
                // No LCP progress — nothing visible, wait for second Tab
                lastKeyWasTab = true;
                return new TabActionResult(TabActionKind.NoOp);
            }
        }
    }
}
