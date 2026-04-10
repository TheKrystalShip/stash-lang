namespace Stash.Analysis.Formatting;

using System.Text;

/// <summary>
/// Serializes a <see cref="Doc"/> IR tree to a human-readable debug string.
/// Useful for snapshot tests and troubleshooting document construction.
/// </summary>
public static class DocDebugPrinter
{
    /// <summary>Returns a debug representation of <paramref name="doc"/>.</summary>
    public static string Print(Doc doc)
    {
        var sb = new StringBuilder();
        Render(doc, sb, 0);
        return sb.ToString();
    }

    private static void Render(Doc doc, StringBuilder sb, int depth)
    {
        string pad = new string(' ', depth * 2);

        switch (doc)
        {
            case Doc.TextDoc text:
                sb.Append("Text(\"");
                sb.Append(EscapeText(text.Value));
                sb.Append("\")");
                break;

            case Doc.HardLineDoc:
                sb.Append("HardLine");
                break;

            case Doc.LineDoc:
                sb.Append("Line");
                break;

            case Doc.SoftLineDoc:
                sb.Append("SoftLine");
                break;

            case Doc.IndentDoc indentDoc:
                sb.AppendLine("Indent(");
                sb.Append(pad + "  ");
                Render(indentDoc.Contents, sb, depth + 1);
                sb.AppendLine();
                sb.Append(pad + ")");
                break;

            case Doc.DedentDoc dedentDoc:
                sb.AppendLine("Dedent(");
                sb.Append(pad + "  ");
                Render(dedentDoc.Contents, sb, depth + 1);
                sb.AppendLine();
                sb.Append(pad + ")");
                break;

            case Doc.GroupDoc groupDoc:
                sb.AppendLine("Group(");
                sb.Append(pad + "  ");
                Render(groupDoc.Contents, sb, depth + 1);
                sb.AppendLine();
                sb.Append(pad + ")");
                break;

            case Doc.FillDoc fill:
                sb.AppendLine("Fill(");
                for (int i = 0; i < fill.Parts.Length; i++)
                {
                    sb.Append(pad + "  ");
                    Render(fill.Parts[i], sb, depth + 1);
                    if (i < fill.Parts.Length - 1) sb.Append(',');
                    sb.AppendLine();
                }
                sb.Append(pad + ")");
                break;

            case Doc.IfBreakDoc ifBreak:
                sb.AppendLine("IfBreak(");
                sb.Append(pad + "  break=");
                Render(ifBreak.BreakContents, sb, depth + 1);
                sb.AppendLine(",");
                sb.Append(pad + "  flat=");
                Render(ifBreak.FlatContents, sb, depth + 1);
                sb.AppendLine();
                sb.Append(pad + ")");
                break;

            case Doc.LineSuffixDoc lineSuffix:
                sb.AppendLine("LineSuffix(");
                sb.Append(pad + "  ");
                Render(lineSuffix.Contents, sb, depth + 1);
                sb.AppendLine();
                sb.Append(pad + ")");
                break;

            case Doc.ConcatDoc concat:
                if (concat.Parts.Length == 0)
                {
                    sb.Append("Empty");
                    break;
                }
                sb.AppendLine("Concat(");
                for (int i = 0; i < concat.Parts.Length; i++)
                {
                    sb.Append(pad + "  ");
                    Render(concat.Parts[i], sb, depth + 1);
                    if (i < concat.Parts.Length - 1) sb.Append(',');
                    sb.AppendLine();
                }
                sb.Append(pad + ")");
                break;

            default:
                sb.Append($"Unknown({doc.GetType().Name})");
                break;
        }
    }

    private static string EscapeText(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
