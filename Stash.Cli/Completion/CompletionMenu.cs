using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Stash.Cli.Completion;

/// <summary>
/// Renders a multi-column candidate list to the terminal (spec §7.1).
/// Activated on the second consecutive Tab when multiple candidates exist.
/// </summary>
internal static class CompletionMenu
{
    private const int PagerThreshold = 100;

    /// <summary>
    /// Prints the candidate list to <see cref="Console.Out"/> in multi-column layout.
    /// If there are more than 100 candidates, calls <paramref name="promptYesNo"/> first.
    /// Returns without printing if the user declines.
    /// </summary>
    public static void Render(IReadOnlyList<Candidate> candidates, Func<string, bool> promptYesNo)
    {
        if (candidates.Count == 0)
            return;

        // Sort case-insensitively by Display
        var sorted = candidates
            .OrderBy(c => c.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Pager prompt for large candidate sets
        if (sorted.Count > PagerThreshold)
        {
            bool confirmed = promptYesNo($"Display all {sorted.Count} possibilities? (y or n) ");
            if (!confirmed)
                return;
        }

        // Determine terminal width
        int termWidth;
        try
        {
            termWidth = Console.WindowWidth;
        }
        catch (IOException)
        {
            termWidth = 80;
        }

        if (termWidth <= 0)
            termWidth = 80;

        // Column width = longest display + 2 spaces padding
        int maxDisplay = sorted.Max(c => c.Display.Length);
        int colWidth = maxDisplay + 2;

        // Number of columns
        int columns = Math.Max(1, termWidth / colWidth);

        // Rows: column-major (fill top-to-bottom, then left-to-right like ls)
        int rows = (int)Math.Ceiling((double)sorted.Count / columns);

        // Print rows
        for (int r = 0; r < rows; r++)
        {
            for (int col = 0; col < columns; col++)
            {
                int idx = col * rows + r;
                if (idx >= sorted.Count)
                    break;

                string display = sorted[idx].Display;
                bool isLastCol = col == columns - 1 || (col + 1) * rows + r >= sorted.Count;

                if (isLastCol)
                {
                    Console.Write(display);
                }
                else
                {
                    Console.Write(display.PadRight(colWidth));
                }
            }
            Console.WriteLine();
        }
    }
}
