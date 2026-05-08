namespace Stash.Stdlib.Generators;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

internal static class DocCommentParser
{
    /// <summary>
    /// Parses an XML doc comment string from <c>ISymbol.GetDocumentationCommentXml()</c> and
    /// returns the documentation string in the format used by the existing hand-written
    /// <c>NamespaceFunction.Documentation</c>: summary first, then <c>\n@param X ...</c> lines,
    /// then <c>\n@return ...</c>, with <c>&lt;remarks&gt;</c> and <c>&lt;example&gt;</c>
    /// appended after the summary.
    /// </summary>
    public static string? Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        XElement root;
        try
        {
            root = XElement.Parse(xml!, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return null;
        }

        // Roslyn returns either <member name="...">...</member> with children inside, or
        // (rarely) the bare top-level tags. Descend if root is the wrapper.
        var src = root;

        var summary = NormalizeText(src.Element("summary")?.Value);
        var remarks = NormalizeText(src.Element("remarks")?.Value);
        var example = NormalizeText(src.Element("example")?.Value);
        var returns = NormalizeText(src.Element("returns")?.Value);
        var paramTags = src.Elements("param")
            .Select(p => (Name: p.Attribute("name")?.Value, Text: NormalizeText(p.Value)))
            .Where(p => !string.IsNullOrEmpty(p.Name))
            .ToList();

        if (string.IsNullOrEmpty(summary)
            && string.IsNullOrEmpty(remarks)
            && string.IsNullOrEmpty(example)
            && string.IsNullOrEmpty(returns)
            && paramTags.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(summary)) sb.Append(summary);
        if (!string.IsNullOrEmpty(remarks))
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(remarks);
        }
        if (!string.IsNullOrEmpty(example))
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(example);
        }
        foreach (var p in paramTags)
        {
            sb.Append("\n@param ").Append(p.Name);
            if (!string.IsNullOrEmpty(p.Text)) sb.Append(' ').Append(p.Text);
        }
        if (!string.IsNullOrEmpty(returns))
        {
            sb.Append("\n@return ").Append(returns);
        }

        return sb.ToString();
    }

    public static HashSet<string> GetDocumentedParamNames(string? xml)
    {
        var names = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(xml)) return names;
        XElement root;
        try { root = XElement.Parse(xml!); }
        catch { return names; }
        foreach (var p in root.Elements("param"))
        {
            var n = p.Attribute("name")?.Value;
            if (!string.IsNullOrEmpty(n)) names.Add(n!);
        }
        return names;
    }

    /// <summary>Returns param names in document order (for Raw function arity metadata).</summary>
    public static List<string> GetDocumentedParamList(string? xml)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(xml)) return names;
        XElement root;
        try { root = XElement.Parse(xml!); }
        catch { return names; }
        foreach (var p in root.Elements("param"))
        {
            var n = p.Attribute("name")?.Value;
            if (!string.IsNullOrEmpty(n)) names.Add(n!);
        }
        return names;
    }

    /// <summary>
    /// Returns only the normalized summary text from an XML doc comment, ignoring
    /// all other tags. Used for struct/enum type descriptions.
    /// </summary>
    public static string? ParseSummaryOnly(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        try
        {
            var root = XElement.Parse(xml!, LoadOptions.PreserveWhitespace);
            var summary = NormalizeText(root.Element("summary")?.Value);
            return string.IsNullOrEmpty(summary) ? null : summary;
        }
        catch
        {
            return null;
        }
    }

    public static bool HasSummary(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try
        {
            var root = XElement.Parse(xml!);
            var s = root.Element("summary")?.Value;
            return !string.IsNullOrWhiteSpace(s);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var lines = raw!.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);
        return string.Join(" ", lines);
    }
}
