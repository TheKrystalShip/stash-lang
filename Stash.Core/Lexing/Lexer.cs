using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Text;
using Stash.Common;
using Stash.Runtime.Types;

namespace Stash.Lexing;

/// <summary>
/// Scans Stash source code and produces a flat list of <see cref="Token"/>s for the
/// <see cref="Stash.Parsing.Parser"/>.
/// </summary>
/// <remarks>
/// <para>
/// The lexer uses a <em>two-pointer scanning approach</em>: <see cref="_start"/> marks the
/// first character of the token currently being built, while <see cref="_current"/> is the
/// read head that advances through the source. When a complete token is recognized the
/// substring <c>_source[_start.._current]</c> becomes the token's lexeme.
/// </para>
/// <para>
/// <strong>Error recovery strategy:</strong> when the lexer encounters an invalid character
/// or unterminated construct (string, block comment, etc.) it records a human-readable error
/// message in <see cref="Errors"/> and continues scanning. This allows all lexical errors in
/// a file to be reported in a single pass rather than stopping at the first one.
/// </para>
/// </remarks>
public class Lexer
{
    /// <summary>The complete source text to be tokenized.</summary>
    private readonly string _source;

    /// <summary>The file path or display name used in error messages (e.g. <c>"&lt;stdin&gt;"</c>).</summary>
    private readonly string _file;

    /// <summary>Accumulates tokens as they are recognized during scanning.</summary>
    private readonly List<Token> _tokens = new();

    /// <summary>Accumulates human-readable error messages encountered during scanning.</summary>
    private readonly List<string> _errors = new();

    /// <summary>
    /// Index into <see cref="_source"/> marking the first character of the token currently
    /// being scanned. Reset at the beginning of each <see cref="ScanToken"/> call.
    /// </summary>
    private int _start;

    /// <summary>
    /// Index into <see cref="_source"/> of the next character to be consumed (the read head).
    /// Always satisfies <c>_start &lt;= _current &lt;= _source.Length</c>.
    /// </summary>
    private int _current;

    /// <summary>The 1-based line number of the character at <see cref="_current"/>.</summary>
    private int _line;

    /// <summary>The 1-based column number of the character at <see cref="_current"/>.</summary>
    private int _column;

    /// <summary>The 1-based line number captured at <see cref="_start"/> when a new token begins.</summary>
    private int _startLine;

    /// <summary>The 1-based column number captured at <see cref="_start"/> when a new token begins.</summary>
    private int _startColumn;

    /// <summary>When <c>true</c>, whitespace and comment tokens are emitted instead of being discarded.</summary>
    private readonly bool _preserveTrivia;

    /// <summary>
    /// Maps reserved word strings to their corresponding <see cref="TokenType"/> values.
    /// </summary>
    /// <remarks>
    /// A <see cref="FrozenDictionary{TKey, TValue}"/> is used because the keyword set is
    /// fixed at compile time and never changes. Compared to a regular
    /// <see cref="Dictionary{TKey, TValue}"/>, <see cref="FrozenDictionary{TKey, TValue}"/>
    /// provides O(1) lookup with lower per-lookup overhead and is allocated once at startup
    /// as a static field shared across all <see cref="Lexer"/> instances.
    /// </remarks>
    private static readonly FrozenDictionary<string, TokenType> _keywords =
        new Dictionary<string, TokenType>
        {
            ["let"] = TokenType.Let,
            ["const"] = TokenType.Const,
            ["fn"] = TokenType.Fn,
            ["struct"] = TokenType.Struct,
            ["enum"] = TokenType.Enum,
            ["interface"] = TokenType.Interface,
            ["if"] = TokenType.If,
            ["else"] = TokenType.Else,
            ["for"] = TokenType.For,
            ["in"] = TokenType.In,
            ["while"] = TokenType.While,
            ["do"] = TokenType.Do,
            ["return"] = TokenType.Return,
            ["break"] = TokenType.Break,
            ["continue"] = TokenType.Continue,
            ["true"] = TokenType.True,
            ["false"] = TokenType.False,
            ["null"] = TokenType.Null,
            ["try"] = TokenType.Try,
            ["throw"] = TokenType.Throw,
            ["catch"] = TokenType.Catch,
            ["finally"] = TokenType.Finally,
            ["import"] = TokenType.Import,
            ["as"] = TokenType.As,
            ["switch"] = TokenType.Switch,
            ["is"] = TokenType.Is,
            ["async"] = TokenType.Async,
            ["await"] = TokenType.Await,
            ["elevate"] = TokenType.Elevate,
            ["extend"] = TokenType.Extend,
            ["and"] = TokenType.AmpersandAmpersand,
            ["or"] = TokenType.PipePipe,
        }.ToFrozenDictionary();

    /// <summary>Alternate lookup handle for <see cref="_keywords"/>, enabling keyword matching directly from <see cref="ReadOnlySpan{T}"/> slices without allocating a string.</summary>
    private static readonly FrozenDictionary<string, TokenType>.AlternateLookup<ReadOnlySpan<char>> _keywordLookup =
        _keywords.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>
    /// Gets the list of error messages accumulated during scanning.
    /// </summary>
    /// <remarks>
    /// Callers should check this list after <see cref="ScanTokens"/> returns. A non-empty
    /// list means the source contained lexical errors, but the returned token stream is
    /// still usable for best-effort parsing.
    /// </remarks>
    public List<string> Errors => _errors;

    /// <summary>Backing store for <see cref="StructuredErrors"/>.</summary>
    private readonly List<DiagnosticError> _structuredErrors = new();
    /// <summary>Gets the list of structured diagnostic errors encountered during scanning, with full <see cref="SourceSpan"/> location information.</summary>
    public List<DiagnosticError> StructuredErrors => _structuredErrors;

    /// <summary>
    /// Initializes a new <see cref="Lexer"/> for the given source text.
    /// </summary>
    /// <param name="source">The complete source code to tokenize.</param>
    /// <param name="file">
    /// A file path or display name included in error messages. Defaults to
    /// <c>"&lt;stdin&gt;"</c> for interactive/REPL input.
    /// </param>
    public Lexer(string source, string file = "<stdin>", int startLine = 1, int startColumn = 1, bool preserveTrivia = false)
    {
        _source = source;
        _file = file;
        _line = startLine;
        _column = startColumn;
        _preserveTrivia = preserveTrivia;
    }

    /// <summary>
    /// Scans the entire source and returns the resulting list of tokens, terminated by an
    /// <see cref="TokenType.Eof"/> token.
    /// </summary>
    /// <returns>
    /// A list of <see cref="Token"/>s. The final token is always
    /// <see cref="TokenType.Eof"/>.
    /// </returns>
    public List<Token> ScanTokens()
    {
        SkipShebang();

        while (!IsAtEnd)
        {
            _start = _current;
            _startLine = _line;
            _startColumn = _column;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.Eof, "", null,
            new SourceSpan(_file, _line, _column, _line, _column)));
        return _tokens;
    }

    /// <summary>
    /// Skips a Unix shebang line (<c>#!/usr/bin/env stash</c>) if present at the very start
    /// of the source.
    /// </summary>
    /// <remarks>
    /// Shebang support allows Stash scripts to be executed directly on Unix-like systems
    /// (e.g. <c>chmod +x script.stash &amp;&amp; ./script.stash</c>). The shebang line is
    /// silently consumed and no token is emitted for it.
    /// </remarks>
    private void SkipShebang()
    {
        if (_current + 1 < _source.Length && _source[_current] == '#' && _source[_current + 1] == '!')
        {
            _start = _current;
            _startLine = _line;
            _startColumn = _column;
            while (!IsAtEnd && _source[_current] != '\n')
            {
                _current++;
                _column++;
            }
            if (_preserveTrivia)
            {
                AddToken(TokenType.Shebang);
            }
            if (!IsAtEnd)
            {
                _current++;
                _line++;
                _column = 1;
            }
        }
    }

    /// <summary>
    /// Reads the next character(s) from the source and emits the appropriate token, skipping
    /// whitespace and comments.
    /// </summary>
    /// <remarks>
    /// This method is the core dispatch of the lexer. It consumes one character via
    /// <see cref="Advance"/>, then uses a <c>switch</c> to decide whether the character
    /// starts a single-character token, a two-character operator (peeking ahead with
    /// <see cref="Match"/>), a comment, a string literal, a number, or an identifier/keyword.
    /// Unrecognized characters are recorded as errors.
    /// </remarks>
    private void ScanToken()
    {
        // let test = 123;
        char c = Advance();
        switch (c)
        {
            case '(': AddToken(TokenType.LeftParen); break;
            case ')': AddToken(TokenType.RightParen); break;
            case '{': AddToken(TokenType.LeftBrace); break;
            case '}': AddToken(TokenType.RightBrace); break;
            case '[': AddToken(TokenType.LeftBracket); break;
            case ']': AddToken(TokenType.RightBracket); break;
            case ',': AddToken(TokenType.Comma); break;
            case '.':
                if (Match('.'))
                {
                    AddToken(TokenType.DotDot);
                }
                else
                {
                    AddToken(TokenType.Dot);
                }
                break;
            case ';': AddToken(TokenType.Semicolon); break;
            case ':': AddToken(TokenType.Colon); break;
            case '%': AddToken(Match('=') ? TokenType.PercentEqual : TokenType.Percent); break;
            case '$':
                if (_current + 2 < _source.Length && _source[_current] == '"' && _source[_current + 1] == '"' && _source[_current + 2] == '"')
                {
                    _current += 3;
                    _column += 3;
                    ScanTripleQuotedString(prefixed: true);
                }
                else if (Match('"'))
                {
                    ScanInterpolatedString(prefixed: true);
                }
                else if (_current + 1 < _source.Length && _source[_current] == '>' && _source[_current + 1] == '(')
                {
                    _current += 2;
                    _column += 2;
                    ScanCommandLiteral(passthrough: true);
                }
                else if (Match('('))
                {
                    ScanCommandLiteral();
                }
                else
                {
                    AddToken(TokenType.Dollar);
                }
                break;

            case '+':
                if (Match('+'))
                {
                    AddToken(TokenType.PlusPlus);
                }
                else if (Match('='))
                {
                    AddToken(TokenType.PlusEqual);
                }
                else
                {
                    AddToken(TokenType.Plus);
                }

                break;
            case '-':
                if (Match('-'))
                {
                    AddToken(TokenType.MinusMinus);
                }
                else if (Match('>'))
                {
                    AddToken(TokenType.Arrow);
                }
                else if (Match('='))
                {
                    AddToken(TokenType.MinusEqual);
                }
                else
                {
                    AddToken(TokenType.Minus);
                }

                break;
            case '*':
                AddToken(Match('=') ? TokenType.StarEqual : TokenType.Star);
                break;
            case '/':
                if (Match('/'))
                {
                    if (Peek() == '/' && PeekNext() != '/')
                    {
                        // /// doc comment — consume the third /
                        _current++;
                        _column++;
                        DocLineComment();
                    }
                    else
                    {
                        SingleLineComment();
                    }
                }
                else if (Match('*'))
                {
                    if (Peek() == '*' && PeekNext() != '/')
                    {
                        // /** doc block comment — consume the second *
                        _current++;
                        _column++;
                        DocBlockComment();
                    }
                    else
                    {
                        MultiLineComment();
                    }
                }
                else if (Match('='))
                {
                    AddToken(TokenType.SlashEqual);
                }
                else
                {
                    AddToken(TokenType.Slash);
                }

                break;
            case '!':
                AddToken(Match('=') ? TokenType.BangEqual : TokenType.Bang);
                break;
            case '=':
                if (Match('='))
                {
                    AddToken(TokenType.EqualEqual);
                }
                else if (Match('>'))
                {
                    AddToken(TokenType.FatArrow);
                }
                else
                {
                    AddToken(TokenType.Equal);
                }

                break;
            case '<':
                if (Match('='))
                {
                    AddToken(TokenType.LessEqual);
                }
                else if (Match('<'))
                {
                    AddToken(Match('=') ? TokenType.LessLessEqual : TokenType.LessLess);
                }
                else
                {
                    AddToken(TokenType.Less);
                }
                break;
            case '>':
                if (Match('='))
                {
                    AddToken(TokenType.GreaterEqual);
                }
                else if (Match('>'))
                {
                    AddToken(Match('=') ? TokenType.GreaterGreaterEqual : TokenType.GreaterGreater);
                }
                else
                {
                    AddToken(TokenType.Greater);
                }

                break;
            case '&':
                if (Match('&'))
                {
                    AddToken(TokenType.AmpersandAmpersand);
                }
                else if (Match('>'))
                {
                    if (Match('>'))
                    {
                        AddToken(TokenType.AmpersandGreaterGreater);
                    }
                    else
                    {
                        AddToken(TokenType.AmpersandGreater);
                    }
                }
                else if (Match('='))
                {
                    AddToken(TokenType.AmpersandEqual);
                }
                else
                {
                    AddToken(TokenType.Ampersand);
                }

                break;
            case '|':
                if (Match('|'))
                {
                    AddToken(TokenType.PipePipe);
                }
                else if (Match('='))
                {
                    AddToken(TokenType.PipeEqual);
                }
                else
                {
                    AddToken(TokenType.Pipe);
                }

                break;
            case '^':
                AddToken(Match('=') ? TokenType.CaretEqual : TokenType.Caret);
                break;
            case '~':
                AddToken(TokenType.Tilde);
                break;
            case '?':
                if (Match('?'))
                {
                    AddToken(Match('=') ? TokenType.QuestionQuestionEqual : TokenType.QuestionQuestion);
                }
                else if (Match('.'))
                {
                    AddToken(TokenType.QuestionDot);
                }
                else
                {
                    AddToken(TokenType.QuestionMark);
                }
                break;

            case '@':
                ScanIpAddress();
                break;

            case ' ':
            case '\r':
            case '\t':
                break;
            case '\n':
                _line++;
                _column = 1;
                break;

            case '"':
                if (_current + 1 < _source.Length && _source[_current] == '"' && _source[_current + 1] == '"')
                {
                    _current += 2;
                    _column += 2;
                    ScanTripleQuotedString(prefixed: false);
                }
                else
                {
                    ScanString();
                }
                break;

            default:
                if (IsDigit(c))
                {
                    ScanNumber();
                }
                else if (IsAlpha(c))
                {
                    ScanIdentifier();
                }
                else
                {
                    _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unexpected character '{c}'.");
                    _structuredErrors.Add(new DiagnosticError(
                        new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                        $"Unexpected character '{c}'."));
                }

                break;
        }
    }

    /// <summary>
    /// Consumes a single-line comment (everything from <c>//</c> to the end of the line).
    /// No token is emitted.
    /// </summary>
    private void SingleLineComment()
    {
        while (!IsAtEnd && _source[_current] != '\n')
        {
            _current++;
            _column++;
        }

        if (_preserveTrivia)
        {
            AddToken(TokenType.SingleLineComment);
        }
    }

    /// <summary>
    /// Scans a <c>///</c> documentation comment line.
    /// The three slashes have already been consumed by the caller.
    /// </summary>
    private void DocLineComment()
    {
        while (!IsAtEnd && _source[_current] != '\n')
        {
            _current++;
            _column++;
        }

        if (_preserveTrivia)
        {
            AddToken(TokenType.DocComment);
        }
    }

    /// <summary>
    /// Scans a <c>/** ... */</c> documentation block comment.
    /// The opening <c>/**</c> has already been consumed by the caller.
    /// Unlike regular block comments, doc block comments do not support nesting.
    /// </summary>
    private void DocBlockComment()
    {
        while (!IsAtEnd)
        {
            if (_source[_current] == '*' && _current + 1 < _source.Length && _source[_current + 1] == '/')
            {
                _current += 2;
                _column += 2;

                if (_preserveTrivia)
                {
                    AddToken(TokenType.DocComment);
                }
                return;
            }
            else if (_source[_current] == '\n')
            {
                _current++;
                _line++;
                _column = 1;
            }
            else
            {
                _current++;
                _column++;
            }
        }

        _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated doc comment.");
        _structuredErrors.Add(new DiagnosticError(
            new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
            "Unterminated doc comment."));
    }

    /// <summary>
    /// Consumes a block comment delimited by <c>/* ... */</c>, supporting arbitrary nesting.
    /// No token is emitted.
    /// </summary>
    /// <remarks>
    /// Unlike C/C++ block comments, Stash block comments nest. This means you can wrap a
    /// region that already contains block comments inside another <c>/* ... */</c> pair
    /// without breaking the lexer — a practical convenience when temporarily commenting out
    /// large sections of code. A depth counter tracks nesting; an unterminated comment (depth
    /// still positive at EOF) is reported as an error.
    /// </remarks>
    private void MultiLineComment()
    {
        int depth = 1;
        while (!IsAtEnd && depth > 0)
        {
            if (_source[_current] == '/' && _current + 1 < _source.Length && _source[_current + 1] == '*')
            {
                depth++;
                _current += 2;
                _column += 2;
            }
            else if (_source[_current] == '*' && _current + 1 < _source.Length && _source[_current + 1] == '/')
            {
                depth--;
                _current += 2;
                _column += 2;
            }
            else if (_source[_current] == '\n')
            {
                _current++;
                _line++;
                _column = 1;
            }
            else
            {
                _current++;
                _column++;
            }
        }

        if (depth > 0)
        {
            _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated block comment.");
            _structuredErrors.Add(new DiagnosticError(
                new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                "Unterminated block comment."));
        }

        if (_preserveTrivia && depth == 0)
        {
            AddToken(TokenType.BlockComment);
        }
    }

    /// <summary>
    /// Scans a double-quoted string literal, processing escape sequences and producing a
    /// <see cref="TokenType.StringLiteral"/> token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lexer supports multi-line strings (newlines inside quotes are preserved) and the
    /// following escape sequences: <c>\\</c>, <c>\"</c>, <c>\n</c>, <c>\t</c>, <c>\r</c>,
    /// <c>\0</c>. Invalid escape sequences are reported as errors but scanning continues so
    /// the rest of the string (and file) can still be analyzed.
    /// </para>
    /// <para>
    /// The token's <see cref="Token.Lexeme"/> includes the surrounding quotes, while its
    /// <see cref="Token.Literal"/> holds the processed string content.
    /// </para>
    /// </remarks>
    private void ScanString()
    {
        // Check if this regular string contains ${...} interpolation markers.
        // If so, delegate to ScanInterpolatedString instead.
        int scanAhead = _current;
        while (scanAhead < _source.Length && _source[scanAhead] != '"')
        {
            if (_source[scanAhead] == '\\' && scanAhead + 1 < _source.Length)
            {
                scanAhead += 2; // skip escape sequence
            }
            else if (_source[scanAhead] == '$' && scanAhead + 1 < _source.Length && _source[scanAhead + 1] == '{')
            {
                // Contains ${...} — scan as interpolated string
                ScanInterpolatedString(prefixed: false);
                return;
            }
            else
            {
                scanAhead++;
            }
        }

        // No interpolation found — scan as a plain string.
        var sb = new StringBuilder();
        while (!IsAtEnd && _source[_current] != '"')
        {
            if (_source[_current] == '\n')
            {
                _line++;
                _column = 1;
                sb.Append(_source[_current]);
                _current++;
            }
            else if (_source[_current] == '\\')
            {
                _current++;
                _column++;
                if (IsAtEnd)
                {
                    _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated string.");
                    _structuredErrors.Add(new DiagnosticError(
                        new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                        "Unterminated string."));
                    return;
                }
                char escaped = _source[_current];
                _current++;
                _column++;
                switch (escaped)
                {
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break;
                    default:
                        _errors.Add($"[{_file} {_line}:{_column - 1}] Invalid escape sequence '\\{escaped}'.");
                        _structuredErrors.Add(new DiagnosticError(
                            new SourceSpan(_file, _line, _column - 1, _line, _column - 1),
                            $"Invalid escape sequence '\\{escaped}'."));
                        sb.Append(escaped);
                        break;
                }
            }
            else
            {
                sb.Append(_source[_current]);
                _current++;
                _column++;
            }
        }

        if (IsAtEnd)
        {
            _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated string.");
            _structuredErrors.Add(new DiagnosticError(
                new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                "Unterminated string."));
            return;
        }

        // Consume the closing "
        _current++;
        _column++;

        string lexeme = _source[_start.._current];
        AddToken(TokenType.StringLiteral, sb.ToString(), lexeme);
    }

    /// <summary>Scans a triple-quoted (multi-line) string literal, stripping common leading indentation.</summary>
    /// <param name="prefixed">Whether the string is a raw-prefixed triple-quoted literal.</param>
    private void ScanTripleQuotedString(bool prefixed)
    {
        // Skip leading newline immediately after opening """
        if (!IsAtEnd && _source[_current] == '\n')
        {
            _current++;
            _line++;
            _column = 1;
        }
        else if (_current + 1 < _source.Length && _source[_current] == '\r' && _source[_current + 1] == '\n')
        {
            _current += 2;
            _line++;
            _column = 1;
        }

        // Pre-scan to determine if interpolation markers exist
        bool hasInterpolation = false;
        int scanPos = _current;
        while (scanPos + 2 < _source.Length)
        {
            if (_source[scanPos] == '"' && _source[scanPos + 1] == '"' && _source[scanPos + 2] == '"')
            {
                break;
            }

            if (_source[scanPos] == '\\')
            {
                scanPos += 2;
                continue;
            }
            if (!prefixed && _source[scanPos] == '$' && scanPos + 1 < _source.Length && _source[scanPos + 1] == '{')
            {
                hasInterpolation = true;
                break;
            }
            if (prefixed && _source[scanPos] == '{')
            {
                hasInterpolation = true;
                break;
            }
            scanPos++;
        }

        if (hasInterpolation)
        {
            ScanTripleQuotedInterpolated(prefixed);
            return;
        }

        // Plain triple-quoted string
        var sb = new StringBuilder();
        while (!IsAtEnd)
        {
            if (_current + 2 < _source.Length && _source[_current] == '"' && _source[_current + 1] == '"' && _source[_current + 2] == '"')
            {
                break;
            }

            if (_source[_current] == '\n')
            {
                sb.Append('\n');
                _current++;
                _line++;
                _column = 1;
            }
            else if (_source[_current] == '\\')
            {
                _current++;
                _column++;
                if (IsAtEnd)
                {
                    _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated triple-quoted string.");
                    _structuredErrors.Add(new DiagnosticError(
                        new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                        "Unterminated triple-quoted string."));
                    return;
                }
                char escaped = _source[_current];
                _current++;
                _column++;
                switch (escaped)
                {
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break;
                    default:
                        _errors.Add($"[{_file} {_line}:{_column - 1}] Invalid escape sequence '\\{escaped}'.");
                        _structuredErrors.Add(new DiagnosticError(
                            new SourceSpan(_file, _line, _column - 1, _line, _column - 1),
                            $"Invalid escape sequence '\\{escaped}'."));
                        sb.Append(escaped);
                        break;
                }
            }
            else
            {
                sb.Append(_source[_current]);
                _current++;
                _column++;
            }
        }

        if (IsAtEnd)
        {
            _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated triple-quoted string.");
            _structuredErrors.Add(new DiagnosticError(
                new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                "Unterminated triple-quoted string."));
            return;
        }

        _current += 3;
        _column += 3;

        string content = StripCommonIndent(sb.ToString());
        string lexeme = _source[_start.._current];
        AddToken(TokenType.StringLiteral, content, lexeme);
    }

    /// <summary>Scans a triple-quoted interpolated string literal, handling embedded <c>${...}</c> expressions while stripping common indentation.</summary>
    /// <param name="prefixed">Whether the string is a raw-prefixed triple-quoted literal.</param>
    private void ScanTripleQuotedInterpolated(bool prefixed)
    {
        var parts = new List<object>();
        var textSegment = new StringBuilder();

        while (!IsAtEnd)
        {
            if (_current + 2 < _source.Length && _source[_current] == '"' && _source[_current + 1] == '"' && _source[_current + 2] == '"')
            {
                break;
            }

            if (_source[_current] == '\n')
            {
                textSegment.Append('\n');
                _current++;
                _line++;
                _column = 1;
            }
            else if (_source[_current] == '\\')
            {
                _current++;
                _column++;
                if (IsAtEnd)
                {
                    _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated triple-quoted string.");
                    _structuredErrors.Add(new DiagnosticError(
                        new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                        "Unterminated triple-quoted string."));
                    return;
                }
                char escaped = _source[_current];
                _current++;
                _column++;
                switch (escaped)
                {
                    case '\\': textSegment.Append('\\'); break;
                    case '"': textSegment.Append('"'); break;
                    case 'n': textSegment.Append('\n'); break;
                    case 't': textSegment.Append('\t'); break;
                    case 'r': textSegment.Append('\r'); break;
                    case '0': textSegment.Append('\0'); break;
                    case '{': textSegment.Append('{'); break;
                    case '$': textSegment.Append('$'); break;
                    default:
                        _errors.Add($"[{_file} {_line}:{_column - 1}] Invalid escape sequence '\\{escaped}'.");
                        _structuredErrors.Add(new DiagnosticError(
                            new SourceSpan(_file, _line, _column - 1, _line, _column - 1),
                            $"Invalid escape sequence '\\{escaped}'."));
                        textSegment.Append(escaped);
                        break;
                }
            }
            else if (prefixed && _source[_current] == '{')
            {
                if (textSegment.Length > 0)
                {
                    parts.Add(textSegment.ToString());
                    textSegment.Clear();
                }
                _current++;
                _column++;
                ScanInterpolatedExpression(parts);
            }
            else if (!prefixed && _source[_current] == '$' && _current + 1 < _source.Length && _source[_current + 1] == '{')
            {
                if (textSegment.Length > 0)
                {
                    parts.Add(textSegment.ToString());
                    textSegment.Clear();
                }
                _current += 2;
                _column += 2;
                ScanInterpolatedExpression(parts);
            }
            else
            {
                textSegment.Append(_source[_current]);
                _current++;
                _column++;
            }
        }

        if (IsAtEnd)
        {
            _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated triple-quoted string.");
            _structuredErrors.Add(new DiagnosticError(
                new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                "Unterminated triple-quoted string."));
            return;
        }

        if (textSegment.Length > 0)
        {
            parts.Add(textSegment.ToString());
        }

        _current += 3;
        _column += 3;

        parts = StripCommonIndentParts(parts);

        string lexeme = _source[_start.._current];
        AddToken(TokenType.InterpolatedString, parts, lexeme);
    }

    /// <summary>Strips the common leading whitespace indentation from a multi-line string.</summary>
    /// <param name="text">The raw multi-line string content.</param>
    /// <returns>The string with common indentation removed from each line.</returns>
    private static string StripCommonIndent(string text)
    {
        if (text.EndsWith('\n'))
        {
            text = text[..^1];
        }

        string[] lines = text.Split('\n');
        int minIndent = int.MaxValue;
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int indent = 0;
            foreach (char c in line)
            {
                if (c is ' ' or '\t')
                {
                    indent++;
                }
                else
                {
                    break;
                }
            }
            minIndent = Math.Min(minIndent, indent);
        }

        if (minIndent is int.MaxValue or 0)
        {
            return text;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            sb.Append(string.IsNullOrWhiteSpace(line)
                ? (line.Length > minIndent ? line[minIndent..] : "")
                : line[minIndent..]);
            if (i < lines.Length - 1)
            {
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>Strips the common leading whitespace indentation from the parts of a triple-quoted interpolated string.</summary>
    /// <param name="parts">The list of string segments and interpolation expression tokens.</param>
    /// <returns>A new list with indentation-stripped string segments.</returns>
    private static List<object> StripCommonIndentParts(List<object> parts)
    {
        var fullText = new StringBuilder();
        foreach (var part in parts)
        {
            if (part is string text)
            {
                fullText.Append(text);
            }
            else
            {
                fullText.Append('\x00');
            }
        }

        string combined = fullText.ToString();
        bool hadTrailingNewline = combined.EndsWith('\n');
        string forIndent = hadTrailingNewline ? combined[..^1] : combined;

        string[] lines = forIndent.Split('\n');
        int minIndent = int.MaxValue;
        foreach (string line in lines)
        {
            bool allWhitespace = true;
            foreach (char c in line)
            {
                if (c != ' ' && c != '\t' && c != '\x00') { allWhitespace = false; break; }
            }
            if (string.IsNullOrEmpty(line) || allWhitespace)
            {
                continue;
            }

            int indent = 0;
            foreach (char c in line)
            {
                if (c is ' ' or '\t')
                {
                    indent++;
                }
                else
                {
                    break;
                }
            }
            minIndent = Math.Min(minIndent, indent);
        }

        if (minIndent is int.MaxValue or 0)
        {
            if (hadTrailingNewline && parts.Count > 0 && parts[^1] is string lastText && lastText.EndsWith('\n'))
            {
                parts[^1] = lastText[..^1];
            }

            return parts;
        }

        var result = new List<object>();
        bool atLineStart = true;
        int indentRemaining = minIndent;

        for (int partIdx = 0; partIdx < parts.Count; partIdx++)
        {
            if (parts[partIdx] is string text)
            {
                if (partIdx == parts.Count - 1 && hadTrailingNewline && text.EndsWith('\n'))
                {
                    text = text[..^1];
                }

                var sb = new StringBuilder();
                foreach (char c in text)
                {
                    if (c == '\n')
                    {
                        sb.Append('\n');
                        atLineStart = true;
                        indentRemaining = minIndent;
                    }
                    else if (atLineStart && indentRemaining > 0 && c is ' ' or '\t')
                    {
                        indentRemaining--;
                    }
                    else
                    {
                        atLineStart = false;
                        sb.Append(c);
                    }
                }
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                }
            }
            else
            {
                atLineStart = false;
                result.Add(parts[partIdx]);
            }
        }

        return result;
    }

    /// <summary>
    /// Scans an interpolated string and produces an <see cref="TokenType.InterpolatedString"/> token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supports two syntaxes:
    /// <list type="bullet">
    ///   <item><description><c>$"Hello {name}"</c> — prefixed form, interpolation markers are <c>{</c>...<c>}</c></description></item>
    ///   <item><description><c>"Hello ${name}"</c> — embedded form, interpolation markers are <c>${</c>...<c>}</c></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The token's <see cref="Token.Literal"/> is a <c>List&lt;object&gt;</c> where each element is
    /// either a <see cref="string"/> (text segment) or a <c>List&lt;Token&gt;</c> (the tokens of an
    /// interpolated expression, without a trailing EOF). The parser converts these into an
    /// <see cref="Stash.Parsing.AST.InterpolatedStringExpr"/>.
    /// </para>
    /// </remarks>
    /// <param name="prefixed">
    /// <c>true</c> for <c>$"..."</c> syntax (interpolation with bare <c>{</c>);
    /// <c>false</c> for <c>"...${...}..."</c> syntax (interpolation with <c>${</c>).
    /// </param>
    private void ScanInterpolatedString(bool prefixed)
    {
        var parts = new List<object>(); // string or List<Token>
        var textSegment = new StringBuilder();

        while (!IsAtEnd && _source[_current] != '"')
        {
            if (_source[_current] == '\n')
            {
                _line++;
                _column = 1;
                textSegment.Append(_source[_current]);
                _current++;
            }
            else if (_source[_current] == '\\')
            {
                _current++;
                _column++;
                if (IsAtEnd)
                {
                    _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated interpolated string.");
                    _structuredErrors.Add(new DiagnosticError(
                        new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                        "Unterminated interpolated string."));
                    return;
                }
                char escaped = _source[_current];
                _current++;
                _column++;
                switch (escaped)
                {
                    case '\\': textSegment.Append('\\'); break;
                    case '"': textSegment.Append('"'); break;
                    case 'n': textSegment.Append('\n'); break;
                    case 't': textSegment.Append('\t'); break;
                    case 'r': textSegment.Append('\r'); break;
                    case '0': textSegment.Append('\0'); break;
                    case '{': textSegment.Append('{'); break;
                    case '$': textSegment.Append('$'); break;
                    default:
                        _errors.Add($"[{_file} {_line}:{_column - 1}] Invalid escape sequence '\\{escaped}'.");
                        _structuredErrors.Add(new DiagnosticError(
                            new SourceSpan(_file, _line, _column - 1, _line, _column - 1),
                            $"Invalid escape sequence '\\{escaped}'."));
                        textSegment.Append(escaped);
                        break;
                }
            }
            else if (prefixed && _source[_current] == '{')
            {
                // Prefixed form: $"...{expr}..."
                if (textSegment.Length > 0)
                {
                    parts.Add(textSegment.ToString());
                    textSegment.Clear();
                }
                _current++; // consume '{'
                _column++;
                ScanInterpolatedExpression(parts);
            }
            else if (!prefixed && _source[_current] == '$' && _current + 1 < _source.Length && _source[_current + 1] == '{')
            {
                // Embedded form: "...${expr}..."
                if (textSegment.Length > 0)
                {
                    parts.Add(textSegment.ToString());
                    textSegment.Clear();
                }
                _current += 2; // consume '${'
                _column += 2;
                ScanInterpolatedExpression(parts);
            }
            else
            {
                textSegment.Append(_source[_current]);
                _current++;
                _column++;
            }
        }

        if (IsAtEnd)
        {
            _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated interpolated string.");
            _structuredErrors.Add(new DiagnosticError(
                new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                "Unterminated interpolated string."));
            return;
        }

        // Add any trailing text segment
        if (textSegment.Length > 0)
        {
            parts.Add(textSegment.ToString());
        }

        // Consume the closing "
        _current++;
        _column++;

        string lexeme = _source[_start.._current];
        AddToken(TokenType.InterpolatedString, parts, lexeme);
    }

    /// <summary>
    /// Scans a command literal <c>$(...)</c>. The opening <c>$(</c> has already been consumed.
    /// </summary>
    /// <remarks>
    /// Content is treated as raw shell command text. Interpolation markers <c>{expr}</c>
    /// embed Stash expressions. Parentheses are tracked for nesting so that subshells or
    /// grouped commands work correctly.
    /// </remarks>
    private void ScanCommandLiteral(bool passthrough = false)
    {
        var parts = new List<object>(); // string or List<Token>
        var textSegment = new StringBuilder();
        int depth = 1;

        bool hasInlinePipes = false;
        int segmentSourceStart = _current;
        int segmentStartLine = _startLine;
        int segmentStartColumn = _startColumn;

        while (!IsAtEnd && depth > 0)
        {
            char c = _source[_current];

            if (c == '(')
            {
                depth++;
                textSegment.Append(c);
                _current++;
                _column++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
                textSegment.Append(c);
                _current++;
                _column++;
            }
            else if (c == '|' && depth == 1 && !passthrough)
            {
                if (_current + 1 < _source.Length && _source[_current + 1] == '|')
                {
                    textSegment.Append('|');
                    textSegment.Append('|');
                    _current += 2;
                    _column += 2;
                }
                else
                {
                    hasInlinePipes = true;

                    string trimmedSeg = textSegment.ToString().TrimEnd();
                    if (trimmedSeg.Length > 0)
                        parts.Add(trimmedSeg);
                    textSegment.Clear();

                    _tokens.Add(new Token(
                        TokenType.CommandLiteral,
                        "$(" + _source[segmentSourceStart.._current].Trim() + ")",
                        new List<object>(parts),
                        new SourceSpan(_file, segmentStartLine, segmentStartColumn, _line, _column)
                    ));
                    parts = new List<object>();

                    int pipeLine = _line;
                    int pipeCol = _column;
                    _current++;
                    _column++;
                    _tokens.Add(new Token(
                        TokenType.Pipe, "|", null,
                        new SourceSpan(_file, pipeLine, pipeCol, _line, _column - 1)
                    ));

                    while (!IsAtEnd && (_source[_current] == ' ' || _source[_current] == '\t'))
                    {
                        _current++;
                        _column++;
                    }

                    segmentSourceStart = _current;
                    segmentStartLine = _line;
                    segmentStartColumn = _column;
                }
            }
            else if (c == '"' || c == '\'')
            {
                // Skip over quoted strings so parentheses inside them
                // don't affect nesting depth.
                char quote = c;
                textSegment.Append(c);
                _current++;
                _column++;
                while (!IsAtEnd && _source[_current] != quote)
                {
                    if (_source[_current] == '\\' && _current + 1 < _source.Length)
                    {
                        textSegment.Append(_source[_current]);
                        textSegment.Append(_source[_current + 1]);
                        _current += 2;
                        _column += 2;
                    }
                    else if (_source[_current] == '$' && _current + 1 < _source.Length && _source[_current + 1] == '{')
                    {
                        // Interpolation inside quoted string within command literal
                        if (textSegment.Length > 0)
                        {
                            parts.Add(textSegment.ToString());
                            textSegment.Clear();
                        }
                        _current += 2;
                        _column += 2;
                        ScanInterpolatedExpression(parts);
                    }
                    else
                    {
                        if (_source[_current] == '\n')
                        {
                            _line++;
                            _column = 0;
                        }
                        textSegment.Append(_source[_current]);
                        _current++;
                        _column++;
                    }
                }
                if (!IsAtEnd)
                {
                    textSegment.Append(_source[_current]); // closing quote
                    _current++;
                    _column++;
                }
            }
            else if (c == '$' && _current + 1 < _source.Length && _source[_current + 1] == '{')
            {
                // Start interpolation: flush accumulated text
                if (textSegment.Length > 0)
                {
                    parts.Add(textSegment.ToString());
                    textSegment.Clear();
                }
                _current += 2; // consume '${'
                _column += 2;
                ScanInterpolatedExpression(parts);
            }
            else if (c == '\n')
            {
                textSegment.Append(c);
                _current++;
                _line++;
                _column = 1;
            }
            else
            {
                textSegment.Append(c);
                _current++;
                _column++;
            }
        }

        if (depth > 0)
        {
            _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unterminated command literal.");
            _structuredErrors.Add(new DiagnosticError(
                new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                "Unterminated command literal."));
            return;
        }

        if (hasInlinePipes)
        {
            string trimmedFinal = textSegment.ToString().TrimEnd();
            if (trimmedFinal.Length > 0)
                parts.Add(trimmedFinal);

            _tokens.Add(new Token(
                TokenType.CommandLiteral,
                "$(" + _source[segmentSourceStart.._current].Trim() + ")",
                new List<object>(parts),
                new SourceSpan(_file, segmentStartLine, segmentStartColumn, _line, _column)
            ));

            _current++;
            _column++;
            return;
        }

        // Flush any remaining text
        if (textSegment.Length > 0)
        {
            parts.Add(textSegment.ToString());
        }

        // Consume the closing ')'
        _current++;
        _column++;

        string lexeme = _source[_start.._current];
        AddToken(passthrough ? TokenType.PassthroughCommandLiteral : TokenType.CommandLiteral, parts, lexeme);
    }

    /// <summary>
    /// Scans the expression inside an interpolation marker (<c>{...}</c>) within an
    /// interpolated string. The opening brace has already been consumed.
    /// </summary>
    /// <remarks>
    /// Creates a nested <see cref="Lexer"/> would be complex, so instead this method
    /// collects the raw expression text (respecting nested braces, strings, etc.),
    /// then lexes it separately and appends the resulting token list to <paramref name="parts"/>.
    /// </remarks>
    /// <param name="parts">The parts list to append the expression tokens to.</param>
    private void ScanInterpolatedExpression(List<object> parts)
    {
        int exprStart = _current;
        int depth = 1;
        int exprStartLine = _line;
        int exprStartColumn = _column;

        while (!IsAtEnd && depth > 0)
        {
            char c = _source[_current];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
            }
            else if (c == '"')
            {
                // Skip over string literals inside the expression
                _current++;
                _column++;
                while (!IsAtEnd && _source[_current] != '"')
                {
                    if (_source[_current] == '\\' && _current + 1 < _source.Length)
                    {
                        _current += 2;
                        _column += 2;
                    }
                    else if (_source[_current] == '\n')
                    {
                        _current++;
                        _line++;
                        _column = 1;
                    }
                    else
                    {
                        _current++;
                        _column++;
                    }
                }
                // The closing " will be consumed by the advance below
            }
            else if (c == '\n')
            {
                _line++;
                _column = 0; // will be incremented to 1 below
            }

            _current++;
            _column++;
        }

        if (IsAtEnd)
        {
            _errors.Add($"[{_file} {exprStartLine}:{exprStartColumn}] Unterminated interpolation expression.");
            _structuredErrors.Add(new DiagnosticError(
                new SourceSpan(_file, exprStartLine, exprStartColumn, exprStartLine, exprStartColumn),
                "Unterminated interpolation expression."));
            return;
        }

        string exprText = _source[exprStart.._current];

        // Consume the closing '}'
        _current++;
        _column++;

        if (string.IsNullOrWhiteSpace(exprText))
        {
            _errors.Add($"[{_file} {exprStartLine}:{exprStartColumn}] Empty interpolation expression.");
            _structuredErrors.Add(new DiagnosticError(
                new SourceSpan(_file, exprStartLine, exprStartColumn, exprStartLine, exprStartColumn),
                "Empty interpolation expression."));
            return;
        }

        // Lex the expression text
        var innerLexer = new Lexer(exprText, _file, exprStartLine, exprStartColumn);
        List<Token> innerTokens = innerLexer.ScanTokens();

        if (innerLexer.Errors.Count > 0)
        {
            foreach (string error in innerLexer.Errors)
            {
                _errors.Add(error);
            }
            _structuredErrors.AddRange(innerLexer.StructuredErrors);
            return;
        }

        // Remove the trailing EOF token — the parser expects raw expression tokens
        if (innerTokens.Count > 0 && innerTokens[^1].Type == TokenType.Eof)
        {
            innerTokens.RemoveAt(innerTokens.Count - 1);
        }

        parts.Add(innerTokens);
    }

    private void ScanIpAddress()
    {
        char next = Peek();

        // @ must be followed by a digit (IPv4), hex digit (IPv6 like fe80::), or colon (IPv6 ::1)
        if (!IsHexDigit(next) && next != ':')
        {
            _errors.Add($"[{_file} {_startLine}:{_startColumn}] Unexpected character '@'.");
            _structuredErrors.Add(new DiagnosticError(
                new SourceSpan(_file, _startLine, _startColumn, _startLine, _startColumn),
                "Unexpected character '@'."));
            return;
        }

        // Consume valid IP address characters
        bool inZoneId = false;
        while (!IsAtEnd)
        {
            char ch = _source[_current];
            if (ch == '%')
            {
                inZoneId = true;
                _current++;
                _column++;
            }
            else if (inZoneId)
            {
                // Zone IDs allow alphanumeric chars (e.g., eth0, wlan0, en0)
                if (IsAlphaNumeric(ch))
                {
                    _current++;
                    _column++;
                }
                else
                {
                    break;
                }
            }
            else if (IsDigit(ch) || IsHexDigit(ch) || ch == '.' || ch == ':' || ch == '/')
            {
                _current++;
                _column++;
            }
            else
            {
                break;
            }
        }

        string lexeme = _source[_start.._current];
        string addressText = lexeme[1..]; // Strip the '@' prefix

        if (StashIpAddress.TryParse(addressText, out StashIpAddress? ipAddress))
        {
            AddToken(TokenType.IpAddressLiteral, ipAddress, lexeme);
        }
        else
        {
            string detail = StashIpAddress.ValidateFormat(addressText) ?? $"Invalid IP address '{addressText}'.";
            _errors.Add($"[{_file} {_startLine}:{_startColumn}] {detail}");
            _structuredErrors.Add(new DiagnosticError(
                new SourceSpan(_file, _startLine, _startColumn, _line, _column),
                detail));
        }
    }

    /// <summary>
    /// Scans an integer or floating-point numeric literal and emits either an
    /// <see cref="TokenType.IntegerLiteral"/> or <see cref="TokenType.FloatLiteral"/> token.
    /// </summary>
    /// <remarks>
    /// A number is classified as a float only if it contains a decimal point followed by at
    /// least one digit (e.g. <c>3.14</c>). A trailing dot without digits (e.g. <c>3.</c>)
    /// is treated as the integer <c>3</c> followed by a <see cref="TokenType.Dot"/> token.
    /// Integer literals that exceed <see cref="long.MaxValue"/> are reported as errors.
    /// All parsing uses <see cref="System.Globalization.CultureInfo.InvariantCulture"/> to
    /// ensure locale-independent behavior.
    /// </remarks>
    private void ScanNumber()
    {
        char firstDigit = _source[_start];

        if (firstDigit == '0' && !IsAtEnd)
        {
            char next = _source[_current];
            if (next == 'x' || next == 'X') { ScanHexLiteral(); return; }
            if (next == 'o' || next == 'O') { ScanOctalLiteral(); return; }
            if (next == 'b' || next == 'B') { ScanBinaryLiteral(); return; }
        }

        ScanDecimalNumber();
    }

    private void ScanHexLiteral()
    {
        // Consume 'x' or 'X'
        _current++;
        _column++;

        if (!IsAtEnd && _source[_current] == '_')
        {
            ReportNumberError("Invalid digit '_' in hexadecimal literal.");
            return;
        }

        if (IsAtEnd || !IsHexDigit(_source[_current]))
        {
            string prefix = _source[_start.._current];
            ReportNumberError($"Hexadecimal literal '{prefix}' has no digits.");
            return;
        }

        bool lastWasUnderscore = false;
        while (!IsAtEnd)
        {
            char c = _source[_current];
            if (IsHexDigit(c))
            {
                lastWasUnderscore = false;
                _current++;
                _column++;
            }
            else if (c == '_')
            {
                if (lastWasUnderscore)
                {
                    ReportNumberError("Consecutive underscores in number literal.");
                    return;
                }
                lastWasUnderscore = true;
                _current++;
                _column++;
            }
            else
            {
                break;
            }
        }

        if (lastWasUnderscore)
        {
            ReportNumberError("Trailing underscore in number literal.");
            return;
        }

        string lexeme = _source[_start.._current];
        string digits = lexeme[2..].Replace("_", "");
        try
        {
            ulong unsigned = Convert.ToUInt64(digits, 16);
            if (unsigned > (ulong)long.MaxValue)
            {
                ReportNumberError($"Integer literal '{lexeme}' is too large.");
                return;
            }
            long value = (long)unsigned;
            AddToken(TokenType.IntegerLiteral, value, lexeme);
        }
        catch (OverflowException)
        {
            ReportNumberError($"Integer literal '{lexeme}' is too large.");
        }
    }

    private void ScanOctalLiteral()
    {
        // Consume 'o' or 'O'
        _current++;
        _column++;

        if (!IsAtEnd && _source[_current] == '_')
        {
            ReportNumberError("Invalid digit '_' in octal literal.");
            return;
        }

        if (IsAtEnd || !IsOctalDigit(_source[_current]))
        {
            string prefix = _source[_start.._current];
            ReportNumberError($"Octal literal '{prefix}' has no digits.");
            return;
        }

        bool lastWasUnderscore = false;
        while (!IsAtEnd)
        {
            char c = _source[_current];
            if (IsOctalDigit(c))
            {
                lastWasUnderscore = false;
                _current++;
                _column++;
            }
            else if (c == '_')
            {
                if (lastWasUnderscore)
                {
                    ReportNumberError("Consecutive underscores in number literal.");
                    return;
                }
                lastWasUnderscore = true;
                _current++;
                _column++;
            }
            else
            {
                break;
            }
        }

        if (lastWasUnderscore)
        {
            ReportNumberError("Trailing underscore in number literal.");
            return;
        }

        string lexeme = _source[_start.._current];
        string digits = lexeme[2..].Replace("_", "");
        try
        {
            ulong unsigned = Convert.ToUInt64(digits, 8);
            if (unsigned > (ulong)long.MaxValue)
            {
                ReportNumberError($"Integer literal '{lexeme}' is too large.");
                return;
            }
            long value = (long)unsigned;
            AddToken(TokenType.IntegerLiteral, value, lexeme);
        }
        catch (OverflowException)
        {
            ReportNumberError($"Integer literal '{lexeme}' is too large.");
        }
    }

    private void ScanBinaryLiteral()
    {
        // Consume 'b' or 'B'
        _current++;
        _column++;

        if (!IsAtEnd && _source[_current] == '_')
        {
            ReportNumberError("Invalid digit '_' in binary literal.");
            return;
        }

        if (IsAtEnd || !IsBinaryDigit(_source[_current]))
        {
            string prefix = _source[_start.._current];
            ReportNumberError($"Binary literal '{prefix}' has no digits.");
            return;
        }

        bool lastWasUnderscore = false;
        while (!IsAtEnd)
        {
            char c = _source[_current];
            if (IsBinaryDigit(c))
            {
                lastWasUnderscore = false;
                _current++;
                _column++;
            }
            else if (c == '_')
            {
                if (lastWasUnderscore)
                {
                    ReportNumberError("Consecutive underscores in number literal.");
                    return;
                }
                lastWasUnderscore = true;
                _current++;
                _column++;
            }
            else
            {
                break;
            }
        }

        if (lastWasUnderscore)
        {
            ReportNumberError("Trailing underscore in number literal.");
            return;
        }

        string lexeme = _source[_start.._current];
        string digits = lexeme[2..].Replace("_", "");
        try
        {
            ulong unsigned = Convert.ToUInt64(digits, 2);
            if (unsigned > (ulong)long.MaxValue)
            {
                ReportNumberError($"Integer literal '{lexeme}' is too large.");
                return;
            }
            long value = (long)unsigned;
            AddToken(TokenType.IntegerLiteral, value, lexeme);
        }
        catch (OverflowException)
        {
            ReportNumberError($"Integer literal '{lexeme}' is too large.");
        }
    }

    private void ScanDecimalNumber()
    {
        bool lastWasUnderscore = false;
        while (!IsAtEnd)
        {
            char c = _source[_current];
            if (IsDigit(c))
            {
                lastWasUnderscore = false;
                _current++;
                _column++;
            }
            else if (c == '_')
            {
                if (lastWasUnderscore)
                {
                    ReportNumberError("Consecutive underscores in number literal.");
                    return;
                }
                lastWasUnderscore = true;
                _current++;
                _column++;
            }
            else
            {
                break;
            }
        }

        if (lastWasUnderscore)
        {
            if (!IsAtEnd && _source[_current] == '.')
            {
                ReportNumberError("Underscore cannot appear adjacent to decimal point.");
            }
            else
            {
                ReportNumberError("Trailing underscore in number literal.");
            }
            return;
        }

        if (!IsAtEnd && _source[_current] == '.' && _current + 1 < _source.Length)
        {
            char afterDot = _source[_current + 1];

            if (afterDot == '_')
            {
                ReportNumberError("Underscore cannot appear adjacent to decimal point.");
                return;
            }

            if (IsDigit(afterDot))
            {
                // Consume '.'
                _current++;
                _column++;

                lastWasUnderscore = false;
                while (!IsAtEnd)
                {
                    char c = _source[_current];
                    if (IsDigit(c))
                    {
                        lastWasUnderscore = false;
                        _current++;
                        _column++;
                    }
                    else if (c == '_')
                    {
                        if (lastWasUnderscore)
                        {
                            ReportNumberError("Consecutive underscores in number literal.");
                            return;
                        }
                        lastWasUnderscore = true;
                        _current++;
                        _column++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (lastWasUnderscore)
                {
                    ReportNumberError("Trailing underscore in number literal.");
                    return;
                }

                string floatLexeme = _source[_start.._current];
                string floatStr = floatLexeme.Replace("_", "");
                if (double.TryParse(floatStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double floatValue))
                {
                    AddToken(TokenType.FloatLiteral, floatValue, floatLexeme);
                }
                else
                {
                    ReportNumberError($"Float literal '{floatLexeme}' is out of range.");
                }
                return;
            }
        }

        string lexeme = _source[_start.._current];
        string intStr = lexeme.Replace("_", "");
        if (long.TryParse(intStr, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long value))
        {
            AddToken(TokenType.IntegerLiteral, value, lexeme);
        }
        else
        {
            ReportNumberError($"Integer literal '{lexeme}' is too large.");
        }
    }

    private void ReportNumberError(string message)
    {
        _errors.Add($"[{_file} {_startLine}:{_startColumn}] {message}");
        _structuredErrors.Add(new DiagnosticError(
            new SourceSpan(_file, _startLine, _startColumn, _line, _column),
            message));
    }

    /// <summary>
    /// Scans an identifier or keyword starting at the current position. If the identifier
    /// matches a reserved word in <see cref="_keywords"/>, the corresponding keyword token
    /// type is used; otherwise an <see cref="TokenType.Identifier"/> token is emitted.
    /// </summary>
    /// <remarks>
    /// Identifier lexemes that are not keywords are interned via
    /// <see cref="string.Intern(string)"/>. Because the same variable or function name
    /// typically appears many times throughout a program, interning ensures all occurrences
    /// share a single <see cref="string"/> instance, reducing memory pressure. Keywords are
    /// not interned because their lexemes are already string literals in the
    /// <see cref="_keywords"/> dictionary.
    /// </remarks>
    private void ScanIdentifier()
    {
        while (!IsAtEnd && IsAlphaNumeric(_source[_current]))
        {
            _current++;
            _column++;
        }

        ReadOnlySpan<char> span = _source.AsSpan(_start, _current - _start);
        if (_keywordLookup.TryGetValue(span, out TokenType type))
        {
            object? literal = type switch
            {
                TokenType.True => true,
                TokenType.False => false,
                _ => null,
            };
            AddToken(type, literal, _source[_start.._current]);
        }
        else
        {
            AddToken(TokenType.Identifier, null, string.Intern(_source[_start.._current]));
        }
    }

    /// <summary>
    /// Consumes the character at <see cref="_current"/> and advances the read head by one position.
    /// </summary>
    /// <returns>The character that was consumed.</returns>
    private char Advance()
    {
        char c = _source[_current];
        _current++;
        _column++;
        return c;
    }

    /// <summary>
    /// Returns the character at the current read-head position without consuming it.
    /// </summary>
    /// <returns>The current character, or <c>'\0'</c> if at the end of the source.</returns>
    private char Peek()
    {
        if (IsAtEnd)
        {
            return '\0';
        }

        return _source[_current];
    }

    /// <summary>
    /// Returns the character one position ahead of the current read head without consuming it.
    /// </summary>
    /// <returns>The next character, or <c>'\0'</c> if there are fewer than two characters remaining.</returns>
    private char PeekNext()
    {
        if (_current + 1 >= _source.Length)
        {
            return '\0';
        }

        return _source[_current + 1];
    }

    /// <summary>
    /// Conditionally consumes the current character if it matches <paramref name="expected"/>.
    /// Used for recognizing two-character operators (e.g. <c>==</c>, <c>!=</c>).
    /// </summary>
    /// <param name="expected">The character to match against.</param>
    /// <returns>
    /// <see langword="true"/> if the current character matched and was consumed;
    /// <see langword="false"/> otherwise.
    /// </returns>
    private bool Match(char expected)
    {
        if (IsAtEnd)
        {
            return false;
        }

        if (_source[_current] != expected)
        {
            return false;
        }

        _current++;
        _column++;
        return true;
    }

    /// <summary>
    /// Gets a value indicating whether the read head has reached or passed the end of the source text.
    /// </summary>
    private bool IsAtEnd => _current >= _source.Length;

    /// <summary>
    /// Determines whether <paramref name="c"/> is an ASCII digit (<c>0</c>–<c>9</c>).
    /// </summary>
    /// <param name="c">The character to test.</param>
    /// <returns><see langword="true"/> if <paramref name="c"/> is a digit.</returns>
    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static bool IsOctalDigit(char c) => c >= '0' && c <= '7';

    private static bool IsBinaryDigit(char c) => c == '0' || c == '1';

    /// <summary>
    /// Determines whether <paramref name="c"/> is an ASCII letter or underscore, which are
    /// valid starting characters for identifiers and keywords.
    /// </summary>
    /// <param name="c">The character to test.</param>
    /// <returns><see langword="true"/> if <paramref name="c"/> is <c>a</c>–<c>z</c>, <c>A</c>–<c>Z</c>, or <c>_</c>.</returns>
    private static bool IsAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

    /// <summary>
    /// Determines whether <paramref name="c"/> is valid in the non-initial position of an
    /// identifier (letter, digit, or underscore).
    /// </summary>
    /// <param name="c">The character to test.</param>
    /// <returns><see langword="true"/> if <paramref name="c"/> is alphanumeric or <c>_</c>.</returns>
    private static bool IsAlphaNumeric(char c) => IsAlpha(c) || IsDigit(c);

    /// <summary>
    /// Creates a token whose lexeme is the substring <c>_source[_start.._current]</c> and
    /// whose <see cref="Token.Literal"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="type">The <see cref="TokenType"/> of the token to emit.</param>
    private void AddToken(TokenType type)
    {
        string lexeme = _source[_start.._current];
        _tokens.Add(new Token(type, lexeme, null,
            new SourceSpan(_file, _startLine, _startColumn, _line, _column - 1)));
    }

    /// <summary>
    /// Creates a token with an explicit literal value and lexeme string.
    /// </summary>
    /// <param name="type">The <see cref="TokenType"/> of the token to emit.</param>
    /// <param name="literal">The parsed runtime value (e.g. a <see cref="long"/>, <see cref="double"/>, or <see cref="string"/>), or <see langword="null"/>.</param>
    /// <param name="lexeme">The raw source text for this token.</param>
    private void AddToken(TokenType type, object? literal, string lexeme)
    {
        _tokens.Add(new Token(type, lexeme, literal,
            new SourceSpan(_file, _startLine, _startColumn, _line, _column - 1)));
    }
}
