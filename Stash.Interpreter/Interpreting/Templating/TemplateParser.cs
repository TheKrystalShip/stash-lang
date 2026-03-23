namespace Stash.Interpreting.Templating;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Parses a flat list of template tokens into a tree of TemplateNode objects.
/// Handles nesting of if/for blocks and filter parsing for output expressions.
/// </summary>
public class TemplateParser
{
    private readonly List<TemplateToken> _tokens;
    private int _pos;

    public TemplateParser(List<TemplateToken> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    public List<TemplateNode> Parse()
    {
        var nodes = ParseNodes(null);
        if (Current().Type != TemplateTokenType.Eof)
        {
            throw new TemplateException($"Unexpected content: '{Current().Value}'", Current().Line, Current().Column);
        }
        return nodes;
    }

    /// <summary>
    /// Parses nodes until a stop tag is found or EOF.
    /// stopTags: tag keywords that terminate this block (e.g., "endif", "endfor", "elif", "else")
    /// </summary>
    private List<TemplateNode> ParseNodes(string[]? stopTags)
    {
        var nodes = new List<TemplateNode>();

        while (_pos < _tokens.Count)
        {
            var token = Current();

            if (token.Type == TemplateTokenType.Eof)
            {
                break;
            }

            if (token.Type == TemplateTokenType.Text)
            {
                nodes.Add(new TextNode(token.Value));
                _pos++;
            }
            else if (token.Type == TemplateTokenType.ExprStart)
            {
                nodes.Add(ParseOutputExpression());
            }
            else if (token.Type == TemplateTokenType.TagStart)
            {
                // Peek at the tag content to see if it's a stop tag
                var tagContent = PeekTagContent();
                var tagKeyword = GetTagKeyword(tagContent);

                if (stopTags is not null && Array.Exists(stopTags, s => s == tagKeyword))
                {
                    // Don't consume — let the caller handle it
                    break;
                }

                nodes.Add(ParseTag());
            }
            else if (token.Type == TemplateTokenType.CommentStart)
            {
                ParseComment();
            }
            else
            {
                // Skip unexpected tokens
                _pos++;
            }
        }

        return ApplyWhitespaceTrimming(nodes);
    }

    private OutputNode ParseOutputExpression()
    {
        var startToken = Expect(TemplateTokenType.ExprStart);
        var contentToken = Expect(TemplateTokenType.Text);
        var endToken = Expect(TemplateTokenType.ExprEnd);

        bool trimBefore = startToken.TrimLeft;
        bool trimAfter = endToken.TrimRight;

        var (expression, filters) = ParseExpressionAndFilters(contentToken.Value);

        return new OutputNode(expression, filters, trimBefore, trimAfter);
    }

    private TemplateNode ParseTag()
    {
        var startToken = Expect(TemplateTokenType.TagStart);
        var contentToken = Expect(TemplateTokenType.Text);
        var endToken = Expect(TemplateTokenType.TagEnd);

        bool trimBefore = startToken.TrimLeft;
        bool trimAfter = endToken.TrimRight;

        var content = contentToken.Value.Trim();
        var keyword = GetTagKeyword(content);

        return keyword switch
        {
            "if" => ParseIfBlock(content, trimBefore, trimAfter),
            "for" => ParseForBlock(content, trimBefore, trimAfter),
            "include" => ParseInclude(content, trimBefore, trimAfter),
            "raw" => ParseRawBlock(),
            _ => throw new TemplateException($"Unknown tag '{keyword}'.", contentToken.Line, contentToken.Column)
        };
    }

    private IfNode ParseIfBlock(string firstConditionContent, bool trimBefore, bool trimAfter)
    {
        // Extract condition from "if condition"
        var condition = firstConditionContent[2..].Trim();
        if (string.IsNullOrEmpty(condition))
        {
            throw new TemplateException("'if' tag requires a condition.");
        }

        var branches = new List<TemplateBranch>();
        TemplateNode[]? elseBody = null;

        // Parse the body of the first if branch
        var body = ParseNodes(new[] { "elif", "else", "endif" });
        branches.Add(new TemplateBranch(condition, body.ToArray()));

        // Handle elif and else
        while (true)
        {
            var token = Current();
            if (token.Type == TemplateTokenType.Eof)
            {
                throw new TemplateException("Unterminated 'if' block — expected '{% endif %}'.");
            }

            if (token.Type == TemplateTokenType.TagStart)
            {
                var tagContent = PeekTagContent();
                var tagKeyword = GetTagKeyword(tagContent);

                if (tagKeyword == "elif")
                {
                    ConsumeTag(); // eat the elif tag
                    var elifContent = tagContent.Trim();
                    var elifCondition = elifContent[4..].Trim(); // skip "elif"
                    if (string.IsNullOrEmpty(elifCondition))
                    {
                        throw new TemplateException("'elif' tag requires a condition.");
                    }

                    var elifBody = ParseNodes(new[] { "elif", "else", "endif" });
                    branches.Add(new TemplateBranch(elifCondition, elifBody.ToArray()));
                }
                else if (tagKeyword == "else")
                {
                    ConsumeTag(); // eat the else tag
                    var elseBodyNodes = ParseNodes(new[] { "endif" });
                    elseBody = elseBodyNodes.ToArray();
                }
                else if (tagKeyword == "endif")
                {
                    ConsumeTag(); // eat the endif tag
                    break;
                }
                else
                {
                    throw new TemplateException($"Unexpected tag '{tagKeyword}' inside 'if' block.");
                }
            }
            else
            {
                break;
            }
        }

        return new IfNode(branches.ToArray(), elseBody, trimBefore, trimAfter);
    }

    private ForNode ParseForBlock(string tagContent, bool trimBefore, bool trimAfter)
    {
        // Parse "for var in iterable"
        var content = tagContent[3..].Trim(); // skip "for"

        var inIndex = content.IndexOf(" in ", StringComparison.Ordinal);
        if (inIndex < 0)
        {
            throw new TemplateException("'for' tag requires 'in' keyword: {% for var in iterable %}.");
        }

        var variable = content[..inIndex].Trim();
        var iterable = content[(inIndex + 4)..].Trim();

        if (string.IsNullOrEmpty(variable))
        {
            throw new TemplateException("'for' tag requires a variable name.");
        }

        if (string.IsNullOrEmpty(iterable))
        {
            throw new TemplateException("'for' tag requires an iterable expression.");
        }

        var body = ParseNodes(new[] { "endfor" });

        // Consume endfor
        if (Current().Type == TemplateTokenType.TagStart)
        {
            var endTagContent = PeekTagContent();
            if (GetTagKeyword(endTagContent) == "endfor")
            {
                ConsumeTag();
            }
            else
            {
                throw new TemplateException("Unterminated 'for' block — expected '{% endfor %}'.");
            }
        }
        else
        {
            throw new TemplateException("Unterminated 'for' block — expected '{% endfor %}'.");
        }

        return new ForNode(variable, iterable, body.ToArray(), trimBefore, trimAfter);
    }

    private IncludeNode ParseInclude(string tagContent, bool trimBefore, bool trimAfter)
    {
        // Parse: include "path" or include 'path'
        var content = tagContent[7..].Trim(); // skip "include"

        // Remove surrounding quotes
        if (content.Length >= 2 &&
            ((content[0] == '"' && content[^1] == '"') ||
             (content[0] == '\'' && content[^1] == '\'')))
        {
            content = content[1..^1];
        }
        else
        {
            throw new TemplateException("'include' tag requires a quoted path: {% include \"file.tpl\" %}.");
        }

        return new IncludeNode(content, trimBefore, trimAfter);
    }

    private RawNode ParseRawBlock()
    {
        // Everything until {% endraw %} is literal text
        var sb = new StringBuilder();

        while (_pos < _tokens.Count)
        {
            var token = Current();

            if (token.Type == TemplateTokenType.Eof)
            {
                throw new TemplateException("Unterminated 'raw' block — expected '{% endraw %}'.");
            }

            if (token.Type == TemplateTokenType.TagStart)
            {
                var tagContent = PeekTagContent();
                if (GetTagKeyword(tagContent) == "endraw")
                {
                    ConsumeTag();
                    break;
                }
            }

            // Reconstruct the original text including delimiters.
            // When a trim marker is active (TrimLeft/TrimRight), a space is emitted between
            // the marker character and the content to restore Jinja2-style spacing (e.g. {{- x -}}).
            if (token.Type == TemplateTokenType.Text)
            {
                sb.Append(token.Value);
            }
            else if (token.Type == TemplateTokenType.ExprStart)
            {
                sb.Append(token.TrimLeft ? "{{- " : "{{");
            }
            else if (token.Type == TemplateTokenType.ExprEnd)
            {
                sb.Append(token.TrimRight ? " -}}" : "}}");
            }
            else if (token.Type == TemplateTokenType.TagStart)
            {
                sb.Append(token.TrimLeft ? "{%- " : "{%");
            }
            else if (token.Type == TemplateTokenType.TagEnd)
            {
                sb.Append(token.TrimRight ? " -%}" : "%}");
            }
            else if (token.Type == TemplateTokenType.CommentStart)
            {
                sb.Append(token.TrimLeft ? "{#- " : "{#");
            }
            else if (token.Type == TemplateTokenType.CommentEnd)
            {
                sb.Append(token.TrimRight ? " -#}" : "#}");
            }

            _pos++;
        }

        return new RawNode(sb.ToString());
    }

    private void ParseComment()
    {
        // Skip: CommentStart, Text, CommentEnd
        Expect(TemplateTokenType.CommentStart);
        Expect(TemplateTokenType.Text);
        Expect(TemplateTokenType.CommentEnd);
    }

    /// <summary>
    /// Parses an expression that may contain filters: "expr | filter1 | filter2(arg)"
    /// Must be careful to distinguish | (filter pipe) from || (logical OR).
    /// </summary>
    private (string Expression, TemplateFilter[] Filters) ParseExpressionAndFilters(string content)
    {
        var filters = new List<TemplateFilter>();

        // Split by single | that is NOT part of || and not inside strings or parentheses
        var parts = SplitByPipe(content);

        if (parts.Count == 0)
        {
            return ("", Array.Empty<TemplateFilter>());
        }

        var expression = parts[0].Trim();

        for (int i = 1; i < parts.Count; i++)
        {
            var filterStr = parts[i].Trim();
            filters.Add(ParseSingleFilter(filterStr));
        }

        return (expression, filters.ToArray());
    }

    /// <summary>
    /// Splits content by single pipe (|) while respecting:
    /// - || (logical OR — not a pipe)
    /// - Strings (single and double quoted)
    /// - Parentheses (nested filter arguments)
    /// </summary>
    private List<string> SplitByPipe(string content)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        int i = 0;
        int parenDepth = 0;
        bool inDoubleQuote = false;
        bool inSingleQuote = false;

        while (i < content.Length)
        {
            char c = content[i];

            // Handle string escapes
            if (c == '\\' && i + 1 < content.Length && (inDoubleQuote || inSingleQuote))
            {
                current.Append(c);
                current.Append(content[i + 1]);
                i += 2;
                continue;
            }

            // Toggle string modes
            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                current.Append(c);
                i++;
                continue;
            }
            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                current.Append(c);
                i++;
                continue;
            }

            // Track parentheses
            if (!inDoubleQuote && !inSingleQuote)
            {
                if (c == '(')
                {
                    parenDepth++;
                }
                else if (c == ')')
                {
                    parenDepth--;
                }
            }

            // Check for pipe
            if (c == '|' && !inDoubleQuote && !inSingleQuote && parenDepth == 0)
            {
                // Check if this is || (logical OR)
                if (i + 1 < content.Length && content[i + 1] == '|')
                {
                    // This is || — include both characters and continue
                    current.Append("||");
                    i += 2;
                    continue;
                }

                // Single pipe — split here
                parts.Add(current.ToString());
                current.Clear();
                i++;
                continue;
            }

            current.Append(c);
            i++;
        }

        parts.Add(current.ToString());
        return parts;
    }

    /// <summary>
    /// Parses a single filter: "name" or "name(arg1, arg2, ...)"
    /// Arguments are parsed as raw strings, preserving quotes.
    /// </summary>
    private TemplateFilter ParseSingleFilter(string filterStr)
    {
        int parenStart = filterStr.IndexOf('(');
        if (parenStart < 0)
        {
            return new TemplateFilter(filterStr.Trim(), Array.Empty<string>());
        }

        var name = filterStr[..parenStart].Trim();

        // Find matching closing paren
        int parenEnd = filterStr.LastIndexOf(')');
        if (parenEnd < parenStart)
        {
            throw new TemplateException($"Filter '{name}' has unmatched parenthesis.");
        }

        var argsStr = filterStr[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(argsStr))
        {
            return new TemplateFilter(name, Array.Empty<string>());
        }

        var args = SplitFilterArgs(argsStr);
        return new TemplateFilter(name, args);
    }

    /// <summary>
    /// Splits filter arguments by comma, respecting quoted strings.
    /// Strips surrounding quotes from each argument.
    /// </summary>
    private string[] SplitFilterArgs(string argsStr)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inDoubleQuote = false;
        bool inSingleQuote = false;

        for (int i = 0; i < argsStr.Length; i++)
        {
            char c = argsStr[i];

            if (c == '\\' && i + 1 < argsStr.Length && (inDoubleQuote || inSingleQuote))
            {
                current.Append(argsStr[i + 1]);
                i++;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue; // strip quotes
            }
            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue; // strip quotes
            }

            if (c == ',' && !inDoubleQuote && !inSingleQuote)
            {
                args.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        args.Add(current.ToString().Trim());
        return args.ToArray();
    }

    // ── Helper methods ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the token at the current parse position without consuming it,
    /// or a synthetic <see cref="TemplateTokenType.Eof"/> token when past the end.
    /// </summary>
    private TemplateToken Current()
    {
        if (_pos < _tokens.Count)
        {
            return _tokens[_pos];
        }

        return new TemplateToken(TemplateTokenType.Eof, "", false, false, 0, 0);
    }

    /// <summary>
    /// Asserts that the current token is of <paramref name="type"/>, consumes it, and returns it.
    /// </summary>
    /// <param name="type">The expected token type.</param>
    /// <exception cref="TemplateException">
    /// Thrown when the current token does not match <paramref name="type"/>.
    /// </exception>
    private TemplateToken Expect(TemplateTokenType type)
    {
        var token = Current();
        if (token.Type != type)
        {
            throw new TemplateException($"Expected {type} but got {token.Type}.", token.Line, token.Column);
        }

        _pos++;
        return token;
    }

    /// <summary>
    /// Peeks at the content of the next tag without consuming tokens.
    /// Assumes current token is TagStart, next is Text.
    /// </summary>
    private string PeekTagContent()
    {
        if (_pos + 1 < _tokens.Count && _tokens[_pos + 1].Type == TemplateTokenType.Text)
        {
            return _tokens[_pos + 1].Value;
        }

        return "";
    }

    /// <summary>
    /// Consumes a complete tag: TagStart + Text + TagEnd
    /// </summary>
    private void ConsumeTag()
    {
        Expect(TemplateTokenType.TagStart);
        Expect(TemplateTokenType.Text);
        Expect(TemplateTokenType.TagEnd);
    }

    /// <summary>
    /// Extracts the leading keyword from a tag content string
    /// (e.g. <c>"if x > 0"</c> → <c>"if"</c>, <c>"endif"</c> → <c>"endif"</c>).
    /// </summary>
    /// <param name="content">The trimmed inner content of a tag block.</param>
    /// <returns>The first whitespace-delimited word of <paramref name="content"/>.</returns>
    private static string GetTagKeyword(string content)
    {
        var trimmed = content.Trim();
        int space = trimmed.IndexOf(' ');
        return space >= 0 ? trimmed[..space] : trimmed;
    }

    /// <summary>
    /// Applies whitespace trimming based on TrimBefore/TrimAfter flags on nodes.
    /// When a node has TrimBefore=true, trailing whitespace of the previous TextNode is stripped.
    /// When a node has TrimAfter=true, leading whitespace of the next TextNode is stripped.
    /// </summary>
    private static List<TemplateNode> ApplyWhitespaceTrimming(List<TemplateNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            bool trimBefore = node switch
            {
                OutputNode o => o.TrimBefore,
                IfNode f => f.TrimBefore,
                ForNode f => f.TrimBefore,
                IncludeNode inc => inc.TrimBefore,
                _ => false
            };

            bool trimAfter = node switch
            {
                OutputNode o => o.TrimAfter,
                IfNode f => f.TrimAfter,
                ForNode f => f.TrimAfter,
                IncludeNode inc => inc.TrimAfter,
                _ => false
            };

            // Trim trailing whitespace from previous text node
            if (trimBefore && i > 0 && nodes[i - 1] is TextNode prevText)
            {
                nodes[i - 1] = new TextNode(prevText.Text.TrimEnd());
            }

            // Trim leading whitespace from next text node
            if (trimAfter && i + 1 < nodes.Count && nodes[i + 1] is TextNode nextText)
            {
                nodes[i + 1] = new TextNode(nextText.Text.TrimStart());
            }
        }

        // Remove empty text nodes created by trimming
        nodes.RemoveAll(n => n is TextNode t && t.Text.Length == 0);

        return nodes;
    }
}
