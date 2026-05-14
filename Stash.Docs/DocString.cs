namespace Stash.Docs;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Parsed view over the <c>Documentation</c> blob attached to every
/// <c>NamespaceFunction</c> / <c>NamespaceConstant</c>. The generator that
/// produces this blob (<c>Stash.Stdlib.Generators.DocCommentParser</c>)
/// emits a fixed shape: a summary paragraph, optionally followed by
/// remarks and example blocks, then <c>\n@param NAME ...</c> lines, then
/// a final <c>\n@return ...</c> line.
/// </summary>
internal sealed class DocString
{
    public string Summary { get; }
    public IReadOnlyDictionary<string, string> Params { get; }
    public string? Returns { get; }

    private DocString(string summary, IReadOnlyDictionary<string, string> @params, string? returns)
    {
        Summary = summary;
        Params = @params;
        Returns = returns;
    }

    public static DocString Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new DocString(string.Empty, EmptyDict, null);

        // Split on lines that start with @param or @return. Both tags were
        // emitted with a leading newline by DocCommentParser, so they always
        // sit at the start of a line.
        var lines = raw!.Split('\n');
        var summaryLines = new List<string>();
        var paramDict = new Dictionary<string, string>(StringComparer.Ordinal);
        string? returns = null;
        string? currentTag = null;
        string? currentParamName = null;
        var currentBuffer = new List<string>();

        void Flush()
        {
            if (currentTag is null)
            {
                summaryLines.AddRange(currentBuffer);
            }
            else if (currentTag == ParamTag && currentParamName is not null)
            {
                paramDict[currentParamName] = string.Join(" ", currentBuffer).Trim();
            }
            else if (currentTag == ReturnTag)
            {
                returns = string.Join(" ", currentBuffer).Trim();
            }
            currentBuffer.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith(ParamTagPrefix, StringComparison.Ordinal))
            {
                Flush();
                currentTag = ParamTag;
                var rest = line.Substring(ParamTagPrefix.Length).TrimStart();
                int sp = IndexOfWhitespace(rest);
                if (sp < 0)
                {
                    currentParamName = rest;
                    currentBuffer.Add(string.Empty);
                }
                else
                {
                    currentParamName = rest.Substring(0, sp);
                    currentBuffer.Add(rest.Substring(sp + 1));
                }
            }
            else if (line.StartsWith(ReturnTagPrefix, StringComparison.Ordinal))
            {
                Flush();
                currentTag = ReturnTag;
                currentBuffer.Add(line.Substring(ReturnTagPrefix.Length).TrimStart());
            }
            else
            {
                currentBuffer.Add(line);
            }
        }
        Flush();

        string summary = string.Join("\n", summaryLines).Trim();
        return new DocString(summary, paramDict, returns);
    }

    private const string ParamTag = "param";
    private const string ReturnTag = "return";
    private const string ParamTagPrefix = "@param ";
    private const string ReturnTagPrefix = "@return ";

    private static readonly IReadOnlyDictionary<string, string> EmptyDict =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static int IndexOfWhitespace(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsWhiteSpace(s[i])) return i;
        }
        return -1;
    }

    /// <summary>Returns just the first sentence of the summary, used for table rows.</summary>
    public string SummaryFirstSentence()
    {
        if (string.IsNullOrEmpty(Summary)) return string.Empty;

        int dot = -1;
        for (int i = 0; i < Summary.Length - 1; i++)
        {
            if (Summary[i] == '.' && (Summary[i + 1] == ' ' || Summary[i + 1] == '\n'))
            {
                dot = i;
                break;
            }
        }
        var first = dot < 0 ? Summary : Summary.Substring(0, dot + 1);
        // Collapse internal newlines so the sentence sits on one Markdown table row.
        return first.Replace('\n', ' ').Trim();
    }
}
