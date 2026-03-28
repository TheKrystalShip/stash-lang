namespace Stash.Tpl;

using System.Collections.Generic;

public enum TemplateTokenType
{
    Text,
    ExprStart,
    ExprEnd,
    TagStart,
    TagEnd,
    CommentStart,
    CommentEnd,
    Eof
}

public record TemplateToken(TemplateTokenType Type, string Value, bool TrimLeft, bool TrimRight, int Line, int Column);

/// <summary>
/// Scans raw template text into a sequence of template tokens.
/// Identifies boundaries between literal text, {{ expressions }}, {% tags %}, and {# comments #}.
/// </summary>
public class TemplateLexer
{
    private readonly string _source;
    private int _pos;
    private int _line;
    private int _column;

    public TemplateLexer(string source)
    {
        _source = source;
        _pos = 0;
        _line = 1;
        _column = 1;
    }

    public List<TemplateToken> Scan()
    {
        var tokens = new List<TemplateToken>();

        while (_pos < _source.Length)
        {
            // Check for delimiters
            if (Matches("{{"))
            {
                // Expression start
                bool trimLeft = false;
                int startLine = _line, startCol = _column;
                Advance(2); // skip {{
                if (_pos < _source.Length && _source[_pos] == '-')
                {
                    trimLeft = true;
                    Advance(1);
                }
                tokens.Add(new TemplateToken(TemplateTokenType.ExprStart, "{{", trimLeft, false, startLine, startCol));

                // Scan until }} or -}}
                var content = ScanUntilExprEnd(out bool trimRight);
                tokens.Add(new TemplateToken(TemplateTokenType.Text, content, false, false, _line, _column));
                tokens.Add(new TemplateToken(TemplateTokenType.ExprEnd, "}}", false, trimRight, _line, _column));
            }
            else if (Matches("{%"))
            {
                // Tag start
                bool trimLeft = false;
                int startLine = _line, startCol = _column;
                Advance(2); // skip {%
                if (_pos < _source.Length && _source[_pos] == '-')
                {
                    trimLeft = true;
                    Advance(1);
                }
                tokens.Add(new TemplateToken(TemplateTokenType.TagStart, "{%", trimLeft, false, startLine, startCol));

                // Scan until %} or -%}
                var content = ScanUntilTagEnd(out bool trimRight);
                tokens.Add(new TemplateToken(TemplateTokenType.Text, content, false, false, _line, _column));
                tokens.Add(new TemplateToken(TemplateTokenType.TagEnd, "%}", false, trimRight, _line, _column));
            }
            else if (Matches("{#"))
            {
                // Comment start
                bool trimLeft = false;
                int startLine = _line, startCol = _column;
                Advance(2); // skip {#
                if (_pos < _source.Length && _source[_pos] == '-')
                {
                    trimLeft = true;
                    Advance(1);
                }
                tokens.Add(new TemplateToken(TemplateTokenType.CommentStart, "{#", trimLeft, false, startLine, startCol));

                // Scan until #} or -#}
                var content = ScanUntilCommentEnd(out bool trimRight);
                tokens.Add(new TemplateToken(TemplateTokenType.Text, content, false, false, _line, _column));
                tokens.Add(new TemplateToken(TemplateTokenType.CommentEnd, "#}", false, trimRight, _line, _column));
            }
            else
            {
                // Literal text — scan until next delimiter or end
                var text = ScanText();
                if (text.Length > 0)
                {
                    tokens.Add(new TemplateToken(TemplateTokenType.Text, text, false, false, _line, _column));
                }
            }
        }

        tokens.Add(new TemplateToken(TemplateTokenType.Eof, "", false, false, _line, _column));
        return tokens;
    }

    private string ScanText()
    {
        int start = _pos;
        while (_pos < _source.Length)
        {
            if (Matches("{{") || Matches("{%") || Matches("{#"))
            {
                break;
            }

            AdvanceChar();
        }
        return _source[start.._pos];
    }

    private string ScanUntilExprEnd(out bool trimRight)
    {
        trimRight = false;
        int start = _pos;
        while (_pos < _source.Length)
        {
            if (_pos + 2 < _source.Length && _source[_pos] == '-' && _source[_pos + 1] == '}' && _source[_pos + 2] == '}')
            {
                trimRight = true;
                string content = _source[start.._pos];
                Advance(3); // skip -}}
                return content.Trim();
            }
            if (Matches("}}"))
            {
                string content = _source[start.._pos];
                Advance(2); // skip }}
                return content.Trim();
            }
            AdvanceChar();
        }
        throw new TemplateException("Unterminated expression block — expected '}}'", _line, _column);
    }

    private string ScanUntilTagEnd(out bool trimRight)
    {
        trimRight = false;
        int start = _pos;
        while (_pos < _source.Length)
        {
            if (_pos + 2 < _source.Length && _source[_pos] == '-' && _source[_pos + 1] == '%' && _source[_pos + 2] == '}')
            {
                trimRight = true;
                string content = _source[start.._pos];
                Advance(3); // skip -%}
                return content.Trim();
            }
            if (Matches("%}"))
            {
                string content = _source[start.._pos];
                Advance(2); // skip %}
                return content.Trim();
            }
            AdvanceChar();
        }
        throw new TemplateException("Unterminated tag block — expected '%}'", _line, _column);
    }

    private string ScanUntilCommentEnd(out bool trimRight)
    {
        trimRight = false;
        int start = _pos;
        while (_pos < _source.Length)
        {
            if (_pos + 2 < _source.Length && _source[_pos] == '-' && _source[_pos + 1] == '#' && _source[_pos + 2] == '}')
            {
                trimRight = true;
                string content = _source[start.._pos];
                Advance(3); // skip -#}
                return content.Trim();
            }
            if (Matches("#}"))
            {
                string content = _source[start.._pos];
                Advance(2); // skip #}
                return content.Trim();
            }
            AdvanceChar();
        }
        throw new TemplateException("Unterminated comment block — expected '#}'", _line, _column);
    }

    private bool Matches(string s)
    {
        if (_pos + s.Length > _source.Length)
        {
            return false;
        }

        for (int i = 0; i < s.Length; i++)
        {
            if (_source[_pos + i] != s[i])
            {
                return false;
            }
        }
        return true;
    }

    private void Advance(int count)
    {
        for (int i = 0; i < count; i++)
        {
            AdvanceChar();
        }
    }

    private void AdvanceChar()
    {
        if (_pos < _source.Length)
        {
            if (_source[_pos] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            _pos++;
        }
    }
}
