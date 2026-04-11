using System;
using System.Collections.Generic;
using Stash.Scheduler.Models;

namespace Stash.Cli.ServiceManager;

/// <summary>
/// Formats data as an aligned plain-text table and writes it to standard output.
/// </summary>
public static class TableFormatter
{
    /// <summary>
    /// Prints a formatted table with the given column headers and rows.
    /// Each column is padded to the width of its widest cell.
    /// </summary>
    /// <param name="headers">Column header names.</param>
    /// <param name="rows">Rows of cell values — each row must have the same length as <paramref name="headers"/>.</param>
    public static void Print(string[] headers, IReadOnlyList<string[]> rows)
    {
        int columnCount = headers.Length;
        int[] widths = new int[columnCount];

        for (int c = 0; c < columnCount; c++)
            widths[c] = headers[c].Length;

        foreach (string[] row in rows)
        {
            for (int c = 0; c < columnCount && c < row.Length; c++)
            {
                if (row[c].Length > widths[c])
                    widths[c] = row[c].Length;
            }
        }

        Console.WriteLine(FormatRow(headers, widths));

        foreach (string[] row in rows)
            Console.WriteLine(FormatRow(row, widths));
    }

    private static string FormatRow(string[] cells, int[] widths)
    {
        var sb = new System.Text.StringBuilder();
        for (int c = 0; c < widths.Length; c++)
        {
            if (c > 0)
                sb.Append("  ");
            string cell = c < cells.Length ? cells[c] : string.Empty;
            sb.Append(cell.PadRight(widths[c]));
        }
        return sb.ToString().TrimEnd();
    }

    public static string FormatState(ServiceState state) => state switch
    {
        ServiceState.Active    => "active",
        ServiceState.Running   => "running",
        ServiceState.Inactive  => "inactive",
        ServiceState.Stopped   => "stopped",
        ServiceState.Failed    => "failed",
        ServiceState.Orphaned  => "orphaned",
        ServiceState.Unmanaged => "unmanaged",
        _                      => "unknown",
    };
}
