using System;

namespace Stash.Cli.Shell;

/// <summary>
/// Kind of the token returned by <see cref="PeekTokenizer.Peek"/>.
/// Whitespace/EOL are never returned (they are skipped internally).
/// </summary>
internal enum PeekKind
{
    EndOfLine,
    Identifier,     // alphanumeric + '_', starts with letter or '_'
    Number,         // digit-leading token
    StringStart,    // '"' or '\''
    PathLike,       // contains '/', or starts with "./" "../" "~/" "~" (or ".\\" on Windows)
    OpenParen,      // (
    OpenBracket,    // [
    OpenBrace,      // {
    Operator,       // +, -, =, !, etc. (operator-leading, not a dollar-command)
    DollarCommand,  // $( $>( $!( $!>(
    Backslash,      // bare leading '\'
    Bang,           // bare leading '!'
    Other,
}

/// <summary>
/// Summary of the first two non-whitespace tokens in a REPL line.
/// </summary>
internal readonly record struct PeekResult(
    PeekKind Kind,
    string FirstToken,
    PeekKind NextKind,
    char? NextLeadChar);

/// <summary>
/// Minimal line scanner that reads just enough to let <see cref="ShellLineClassifier"/>
/// apply the §4 disambiguation rules without invoking the full Stash lexer.
/// </summary>
internal static class PeekTokenizer
{
    public static PeekResult Peek(string line)
    {
        int i = 0;
        int len = line.Length;

        // Skip leading whitespace.
        while (i < len && IsAsciiWhitespace(line[i])) i++;

        if (i >= len)
            return new PeekResult(PeekKind.EndOfLine, "", PeekKind.EndOfLine, null);

        char c = line[i];

        PeekKind firstKind;
        string firstToken;

        // ── Detect dollar-command prefixes: $(  $>(  $!(  $!>( ──────────────
        if (c == '$' && i + 1 < len && line[i + 1] == '(')
        {
            firstKind = PeekKind.DollarCommand;
            firstToken = "$(";
            i += 2;
        }
        else if (c == '$' && i + 1 < len && line[i + 1] == '>' && i + 2 < len && line[i + 2] == '(')
        {
            firstKind = PeekKind.DollarCommand;
            firstToken = "$>(";
            i += 3;
        }
        else if (c == '$' && i + 1 < len && line[i + 1] == '!' && i + 2 < len && line[i + 2] == '(' )
        {
            firstKind = PeekKind.DollarCommand;
            firstToken = "$!(";
            i += 3;
        }
        else if (c == '$' && i + 1 < len && line[i + 1] == '!' && i + 2 < len && line[i + 2] == '>' && i + 3 < len && line[i + 3] == '(')
        {
            firstKind = PeekKind.DollarCommand;
            firstToken = "$!>(";
            i += 4;
        }
        // ── Backslash prefix ────────────────────────────────────────────────
        else if (c == '\\')
        {
            firstKind = PeekKind.Backslash;
            firstToken = "\\";
            i++;
        }
        // ── Bang prefix — distinguish from "!=" or "!(" ────────────────────
        else if (c == '!')
        {
            firstKind = PeekKind.Bang;
            firstToken = "!";
            i++;
        }
        // ── Opening delimiters ──────────────────────────────────────────────
        else if (c == '(')
        {
            firstKind = PeekKind.OpenParen;
            firstToken = "(";
            i++;
        }
        else if (c == '[')
        {
            firstKind = PeekKind.OpenBracket;
            firstToken = "[";
            i++;
        }
        else if (c == '{')
        {
            firstKind = PeekKind.OpenBrace;
            firstToken = "{";
            i++;
        }
        // ── String start ────────────────────────────────────────────────────
        else if (c == '"' || c == '\'')
        {
            firstKind = PeekKind.StringStart;
            firstToken = c.ToString();
            i++;
        }
        // ── Number ──────────────────────────────────────────────────────────
        else if (char.IsAsciiDigit(c))
        {
            int start = i;
            while (i < len && (char.IsAsciiLetterOrDigit(line[i]) || line[i] == '.' || line[i] == '_'))
                i++;
            firstKind = PeekKind.Number;
            firstToken = line[start..i];
        }
        // ── Path-like: starts with / ./ ../ ~/ ~ ───────────────────────────
        else if (IsPathLikeStart(line, i))
        {
            int start = i;
            // Capture until first unquoted whitespace
            while (i < len && !IsAsciiWhitespace(line[i]))
                i++;
            firstKind = PeekKind.PathLike;
            firstToken = line[start..i];
        }
        // ── Identifier ──────────────────────────────────────────────────────
        else if (c == '_' || char.IsAsciiLetter(c))
        {
            int start = i;
            while (i < len && (char.IsAsciiLetterOrDigit(line[i]) || line[i] == '_'))
                i++;
            string ident = line[start..i];

            // After the identifier, check if the remaining line makes it path-like
            // (e.g. "foo/bar" → PathLike because of the embedded slash).
            if (i < len && line[i] == '/')
            {
                // The identifier continues into a path component.
                while (i < len && !IsAsciiWhitespace(line[i]))
                    i++;
                firstKind = PeekKind.PathLike;
                firstToken = line[start..i];
            }
            else
            {
                firstKind = PeekKind.Identifier;
                firstToken = ident;
            }
        }
        // ── Operator / Other ────────────────────────────────────────────────
        else
        {
            firstKind = PeekKind.Operator;
            firstToken = c.ToString();
            i++;
        }

        // Skip whitespace to reach the second token.
        while (i < len && IsAsciiWhitespace(line[i])) i++;

        if (i >= len)
            return new PeekResult(firstKind, firstToken, PeekKind.EndOfLine, null);

        // Classify the lead character of the second token.
        char nc = line[i];
        PeekKind nextKind = ClassifyLeadChar(nc, line, i);
        return new PeekResult(firstKind, firstToken, nextKind, nc);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static bool IsAsciiWhitespace(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

    /// <summary>
    /// Returns true if the position in the line looks like the start of a path-like token:
    /// starts with '/', starts with './' or '../', starts with '~/' or is bare '~'.
    /// Also handles Windows '.\' and '..\'.
    /// </summary>
    private static bool IsPathLikeStart(string line, int i)
    {
        char c = line[i];
        int remaining = line.Length - i;

        if (c == '/') return true;          // absolute POSIX path
        if (c == '~')
        {
            // bare '~' (followed by whitespace or EOL) or '~/'
            if (remaining == 1) return true;
            char next = line[i + 1];
            return next == '/' || next == '\\' || IsAsciiWhitespace(next);
        }
        if (c == '.')
        {
            if (remaining == 1) return false;
            char next = line[i + 1];
            if (next == '/' || next == '\\') return true;   // ./
            if (next == '.' && remaining > 2)
            {
                char nn = line[i + 2];
                if (nn == '/' || nn == '\\') return true;   // ../
            }
        }
        return false;
    }

    private static PeekKind ClassifyLeadChar(char c, string line, int i)
    {
        int remaining = line.Length - i;
        if (c == '$' && remaining > 1 && line[i + 1] == '(') return PeekKind.DollarCommand;
        if (c == '(') return PeekKind.OpenParen;
        if (c == '[') return PeekKind.OpenBracket;
        if (c == '{') return PeekKind.OpenBrace;
        if (c == '"' || c == '\'') return PeekKind.StringStart;
        if (char.IsAsciiDigit(c)) return PeekKind.Number;
        if (c == '_' || char.IsAsciiLetter(c)) return PeekKind.Identifier;
        if (c == '\\') return PeekKind.Backslash;
        if (c == '!') return PeekKind.Bang;
        if (IsPathLikeStart(line, i)) return PeekKind.PathLike;
        return PeekKind.Operator;
    }
}
