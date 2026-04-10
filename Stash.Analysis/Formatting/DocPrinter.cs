namespace Stash.Analysis.Formatting;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Renders a <see cref="Doc"/> IR to a string using the Wadler-Lindig algorithm.
/// The implementation is iterative (stack-based) to avoid stack-overflow on deeply nested documents.
/// </summary>
public static class DocPrinter
{
    private enum Mode { Flat, Break }

    private readonly record struct Command(Doc Doc, int Indent, Mode Mode);

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Renders <paramref name="doc"/> to a string.
    /// </summary>
    /// <param name="doc">The document to render.</param>
    /// <param name="printWidth">Target line width (default: 80).</param>
    /// <param name="indentWidth">Spaces (or chars) per indent level (default: 2).</param>
    /// <param name="indentChar">Character used for indentation (default: space).</param>
    public static string Print(Doc doc, int printWidth = 80, int indentWidth = 2, char indentChar = ' ')
    {
        var sb = new StringBuilder();
        var commands = new Stack<Command>();
        var lineSuffixes = new List<Doc>();

        bool pendingIndent = false;
        int pendingIndentLevel = 0;
        int column = 0;

        commands.Push(new Command(doc, 0, Mode.Break));

        while (commands.Count > 0)
        {
            var (current, indent, mode) = commands.Pop();
            Process(current, indent, mode);
        }

        // Flush any trailing line-suffix content.
        FlushLineSuffixes(0, Mode.Break);

        return sb.ToString();

        // -----------------------------------------------------------------
        // Inner helpers (closures over local state)
        // -----------------------------------------------------------------

        void Process(Doc current, int indent, Mode mode)
        {
            switch (current)
            {
                case Doc.TextDoc text:
                    if (text.Value.Length == 0) break;
                    if (pendingIndent)
                    {
                        EmitIndent(pendingIndentLevel);
                        pendingIndent = false;
                    }
                    sb.Append(text.Value);
                    column += text.Value.Length;
                    break;

                case Doc.HardLineDoc:
                    FlushLineSuffixes(indent, mode);
                    sb.Append('\n');
                    pendingIndent = true;
                    pendingIndentLevel = indent;
                    column = 0;
                    break;

                case Doc.LineDoc:
                    if (mode == Mode.Flat)
                    {
                        sb.Append(' ');
                        column++;
                    }
                    else
                    {
                        FlushLineSuffixes(indent, mode);
                        sb.Append('\n');
                        pendingIndent = true;
                        pendingIndentLevel = indent;
                        column = 0;
                    }
                    break;

                case Doc.SoftLineDoc:
                    if (mode == Mode.Break)
                    {
                        FlushLineSuffixes(indent, mode);
                        sb.Append('\n');
                        pendingIndent = true;
                        pendingIndentLevel = indent;
                        column = 0;
                    }
                    // In Flat mode: emit nothing.
                    break;

                case Doc.IndentDoc indentDoc:
                    commands.Push(new Command(indentDoc.Contents, indent + 1, mode));
                    break;

                case Doc.DedentDoc dedentDoc:
                    commands.Push(new Command(dedentDoc.Contents, Math.Max(indent - 1, 0), mode));
                    break;

                case Doc.GroupDoc groupDoc:
                {
                    int currentColumn = pendingIndent
                        ? pendingIndentLevel * indentWidth
                        : column;
                    int remaining = printWidth - currentColumn;
                    var childMode = Fits(groupDoc.Contents, indent, remaining, indentWidth) ? Mode.Flat : Mode.Break;
                    commands.Push(new Command(groupDoc.Contents, indent, childMode));
                    break;
                }

                case Doc.ConcatDoc concat:
                    // Push in reverse so that first part is processed first.
                    for (int i = concat.Parts.Length - 1; i >= 0; i--)
                        commands.Push(new Command(concat.Parts[i], indent, mode));
                    break;

                case Doc.FillDoc fill:
                    ProcessFill(fill.Parts, indent, mode);
                    break;

                case Doc.IfBreakDoc ifBreak:
                    commands.Push(mode == Mode.Break
                        ? new Command(ifBreak.BreakContents, indent, mode)
                        : new Command(ifBreak.FlatContents, indent, mode));
                    break;

                case Doc.LineSuffixDoc lineSuffix:
                    lineSuffixes.Add(lineSuffix.Contents);
                    break;
            }
        }

        void EmitIndent(int level)
        {
            int spaces = level * indentWidth;
            sb.Append(indentChar, spaces);
            column = spaces;
        }

        void FlushLineSuffixes(int indent, Mode mode)
        {
            if (lineSuffixes.Count == 0) return;

            var toFlush = new List<Doc>(lineSuffixes);
            lineSuffixes.Clear();

            // Process each suffix fully before returning. When Process pushes compound
            // doc parts onto the commands stack, drain them so all suffix content is
            // rendered before the caller emits the newline.
            int savedCount = commands.Count;
            foreach (var suffix in toFlush)
            {
                Process(suffix, indent, mode);
                while (commands.Count > savedCount)
                {
                    var (current, ind, m) = commands.Pop();
                    Process(current, ind, m);
                }
            }

            // If processing the suffix added more suffixes, flush those too.
            if (lineSuffixes.Count > 0)
                FlushLineSuffixes(indent, mode);
        }

        // Fill: greedily pack parts onto lines.
        // Parts are [content, sep, content, sep, ...].
        void ProcessFill(Doc[] parts, int indent, Mode mode)
        {
            if (parts.Length == 0) return;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];

                // Separator (odd index): decide flat or break based on what follows.
                if (i % 2 == 1)
                {
                    // Already handled together with the preceding content below.
                    // This path is only reached when ProcessFill re-pushes parts individually.
                    int currentColumn = pendingIndent ? pendingIndentLevel * indentWidth : column;
                    int remaining = printWidth - currentColumn;

                    // Check if the separator in flat mode + next content fits.
                    bool sepFits = i + 1 < parts.Length
                        ? FitsAll([part, parts[i + 1]], indent, remaining, indentWidth)
                        : Fits(part, indent, remaining, indentWidth);

                    if (sepFits)
                    {
                        // Render separator flat (e.g., Line → space).
                        RenderFlat(part, indent);
                    }
                    else
                    {
                        // Render separator as break (e.g., Line → newline).
                        Process(part, indent, Mode.Break);
                    }
                }
                else
                {
                    // Content (even index): always render flat in fill mode.
                    RenderFlat(part, indent);
                }
            }
        }

        void RenderFlat(Doc d, int indent)
        {
            // Push a group wrapping d in flat mode — but we need it to stay flat.
            // Simplest: process it with mode=Flat directly.
            Process(d, indent, Mode.Flat);
        }
    }

    // -------------------------------------------------------------------------
    // Fits check (iterative)
    // -------------------------------------------------------------------------

    private static bool Fits(Doc doc, int indent, int remainingWidth, int indentWidth)
    {
        return FitsAll([doc], indent, remainingWidth, indentWidth);
    }

    private static bool FitsAll(Doc[] docs, int indent, int remainingWidth, int indentWidth)
    {
        var stack = new Stack<(Doc Doc, int Indent)>();

        // Push in reverse order.
        for (int i = docs.Length - 1; i >= 0; i--)
            stack.Push((docs[i], indent));

        int remaining = remainingWidth;

        while (stack.Count > 0 && remaining >= 0)
        {
            var (doc, ind) = stack.Pop();

            switch (doc)
            {
                case Doc.TextDoc text:
                    remaining -= text.Value.Length;
                    break;

                case Doc.LineDoc:
                    remaining -= 1; // space in flat mode
                    break;

                case Doc.SoftLineDoc:
                    break; // nothing in flat mode

                case Doc.HardLineDoc:
                    return false; // always breaks

                case Doc.IndentDoc indentDoc:
                    stack.Push((indentDoc.Contents, ind + 1));
                    break;

                case Doc.DedentDoc dedentDoc:
                    stack.Push((dedentDoc.Contents, Math.Max(ind - 1, 0)));
                    break;

                case Doc.GroupDoc groupDoc:
                    stack.Push((groupDoc.Contents, ind));
                    break;

                case Doc.ConcatDoc concat:
                    for (int i = concat.Parts.Length - 1; i >= 0; i--)
                        stack.Push((concat.Parts[i], ind));
                    break;

                case Doc.FillDoc fill:
                    for (int i = fill.Parts.Length - 1; i >= 0; i--)
                        stack.Push((fill.Parts[i], ind));
                    break;

                case Doc.IfBreakDoc ifBreak:
                    stack.Push((ifBreak.FlatContents, ind));
                    break;

                case Doc.LineSuffixDoc:
                    break; // doesn't affect line width
            }
        }

        return remaining >= 0;
    }
}
