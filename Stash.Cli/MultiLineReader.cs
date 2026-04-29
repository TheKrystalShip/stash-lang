using System;
using System.Collections.Generic;
using System.Text;
using Stash.Lexing;

namespace Stash;

/// <summary>
/// Wraps a physical-line read function to return a "complete logical line" by
/// buffering physical lines until input is syntactically complete.
///
/// Continuation triggers (per spec §9):
///   1. Trailing backslash on the input line (both modes) — strip the '\',
///      append a space, request another line.
///   2. Unbalanced '(', '[', '{' or unterminated '"'/'\'' (Stash mode only) —
///      request another line until balanced.
///
/// Empty lines do NOT terminate continuation.
/// </summary>
public sealed class MultiLineReader
{
    private readonly Func<string, string?> _readLine;
    private readonly string _firstPrompt;
    private readonly string _continuationPrompt;

    /// <summary>
    /// Optional hook for shell-mode completeness checking.
    /// When set, a line ending with a bare <c>|</c> is treated as an incomplete
    /// shell pipeline and more input is requested.
    /// The delegate receives the entire accumulated logical line so far.
    /// </summary>
    public Func<string, bool>? IsShellIncomplete { get; set; }

    /// <summary>
    /// Constructor used in tests: accepts any delegate for reading a physical line.
    /// </summary>
    /// <param name="readLine">Delegate that accepts a prompt string and returns the next line, or null on EOF.</param>
    /// <param name="firstPrompt">Prompt shown on the first line of input.</param>
    /// <param name="continuationPrompt">Prompt shown on continuation lines.</param>
    public MultiLineReader(Func<string, string?> readLine, string firstPrompt = "stash> ", string continuationPrompt = "... ")
    {
        _readLine = readLine;
        _firstPrompt = firstPrompt;
        _continuationPrompt = continuationPrompt;
    }

    /// <summary>
    /// Constructor that wraps a <see cref="LineEditor"/> for production use.
    /// </summary>
    public MultiLineReader(LineEditor editor, string firstPrompt = "stash> ", string continuationPrompt = "... ")
        : this(editor.ReadLine, firstPrompt, continuationPrompt)
    {
    }

    /// <summary>
    /// Read one logical line (possibly spanning multiple physical lines).
    /// Returns null on EOF (Ctrl+D) at the FIRST prompt. EOF in the middle
    /// of a multi-line entry returns whatever has been accumulated so far.
    /// </summary>
    public string? ReadLogicalLine()
    {
        string? first = _readLine(_firstPrompt);
        if (first is null)
        {
            return null;
        }

        var accumulator = new StringBuilder(first);

        while (true)
        {
            string current = accumulator.ToString();

            // Check for trailing backslash continuation.
            if (HasTrailingContinuationBackslash(current))
            {
                // Strip the trailing backslash and append a space.
                accumulator.Remove(accumulator.Length - 1, 1);
                if (accumulator.Length > 0 && accumulator[^1] != ' ')
                {
                    accumulator.Append(' ');
                }

                string? next = _readLine(_continuationPrompt);
                if (next is null)
                {
                    return accumulator.ToString();
                }

                accumulator.Append(next);
                continue;
            }

            // Shell-mode: trailing pipe means the pipeline continues on the next line.
            if (IsShellIncomplete?.Invoke(current) == true)
            {
                string? next = _readLine(_continuationPrompt);
                if (next is null)
                {
                    return accumulator.ToString();
                }

                accumulator.Append('\n');
                accumulator.Append(next);
                continue;
            }

            // Check Stash input completeness.
            if (IsStashInputComplete(current))
            {
                return current;
            }

            // Input is incomplete — read another physical line.
            string? continuation = _readLine(_continuationPrompt);
            if (continuation is null)
            {
                return current;
            }

            accumulator.Append('\n');
            accumulator.Append(continuation);
        }
    }

    /// <summary>
    /// Returns true if the text ends with an odd number of backslashes,
    /// indicating a line-continuation escape (not a literal backslash).
    /// </summary>
    internal static bool HasTrailingContinuationBackslash(string text)
    {
        // Strip only trailing whitespace that isn't part of the backslash sequence.
        ReadOnlySpan<char> span = text.AsSpan().TrimEnd(' ').TrimEnd('\t');
        int count = 0;
        int i = span.Length - 1;
        while (i >= 0 && span[i] == '\\')
        {
            count++;
            i--;
        }

        return count % 2 == 1;
    }

    /// <summary>
    /// Determines whether the accumulated input is syntactically complete by
    /// running the Stash lexer and checking for unbalanced delimiters or
    /// unterminated string literals.
    /// </summary>
    internal static bool IsStashInputComplete(string text)
    {
        var lexer = new Lexer(text, "<stdin>");
        List<Token> tokens = lexer.ScanTokens();

        // Check for unterminated string literals.
        foreach (var error in lexer.StructuredErrors)
        {
            if (error.Message.Contains("Unterminated string", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Count bracket depth.
        int depth = 0;
        foreach (var token in tokens)
        {
            switch (token.Type)
            {
                case TokenType.LeftParen:
                case TokenType.LeftBracket:
                case TokenType.LeftBrace:
                    depth++;
                    break;
                case TokenType.RightParen:
                case TokenType.RightBracket:
                case TokenType.RightBrace:
                    depth--;
                    break;
            }
        }

        return depth <= 0;
    }
}
