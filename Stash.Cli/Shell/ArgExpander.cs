using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Stash.Bytecode;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Runtime;

namespace Stash.Cli.Shell;

/// <summary>
/// Implements the §6 argument-expansion pipeline for shell-mode commands:
///
///   1. <c>${expr}</c> interpolation  — evaluated against the REPL VM
///   2. Tilde expansion              — leading <c>~</c> or <c>~/</c> → home dir
///   3. Word splitting               — unquoted whitespace splits into separate args
///   4. Glob expansion               — unquoted glob chars matched against filesystem
///
/// Brace expansion (§6 step 2) is Phase 10 — deliberately omitted here.
///
/// Quoting rules:
///   • <c>"…"</c> and <c>'…'</c> both suppress word-splitting and glob expansion.
///   • Both quote types interpolate <c>${expr}</c> (Stash quoting semantics).
///   • <c>\$</c> produces a literal <c>$</c>; <c>\\</c> produces a literal <c>\</c>.
///
/// Interpolation does NOT word-split the result: <c>${"a b"}</c> → single arg <c>"a b"</c>.
/// </summary>
internal static class ArgExpander
{
    /// <summary>
    /// Expand the raw argument string from a <see cref="ShellStage"/> into a final
    /// argument list ready to pass to the OS.
    /// </summary>
    public static List<string> Expand(string rawArgs, VirtualMachine vm, SourceSpan? span)
    {
        if (string.IsNullOrEmpty(rawArgs))
            return new List<string>(0);

        // Parse rawArgs into a list of fragments: (text, isQuoted).
        // Adjacent fragments belonging to the same word are concatenated before
        // word-splitting. A fragment is marked as "any portion quoted" if any
        // character in it came from inside a quoted region.
        var words = new List<(string Text, bool AnyQuoted)>();
        ParseIntoWords(rawArgs, vm, span, words);

        // Apply glob expansion to unquoted words.
        var result = new List<string>(words.Count);
        foreach (var (text, anyQuoted) in words)
        {
            if (!anyQuoted && GlobExpander.HasGlobChars(text))
            {
                var matches = GlobExpander.Expand(text);
                if (matches.Count == 0)
                    throw new RuntimeError(
                        $"glob pattern '{text}' did not match any files",
                        span, StashErrorTypes.CommandError);
                result.AddRange(matches);
            }
            else
            {
                result.Add(text);
            }
        }

        return result;
    }

    // ── Core parser ──────────────────────────────────────────────────────────

    /// <summary>
    /// Walk rawArgs left-to-right tracking quote state and ${} depth.
    /// Produce a list of complete words (each with an "any-quoted" flag).
    /// </summary>
    private static void ParseIntoWords(
        string rawArgs, VirtualMachine vm, SourceSpan? span,
        List<(string, bool)> words)
    {
        var wordBuf = new StringBuilder();
        bool anyQuoted = false;
        bool inWord = false;
        bool inSingle = false;
        bool inDouble = false;
        int i = 0;
        int len = rawArgs.Length;

        void FlushWord()
        {
            if (inWord)
            {
                words.Add((wordBuf.ToString(), anyQuoted));
                wordBuf.Clear();
                anyQuoted = false;
                inWord = false;
            }
        }

        while (i < len)
        {
            char c = rawArgs[i];

            // ── Escape sequences ──────────────────────────────────────────
            if (c == '\\' && i + 1 < len)
            {
                char next = rawArgs[i + 1];
                if (next == '$')
                {
                    // \$ → literal '$'
                    wordBuf.Append('$');
                    inWord = true;
                    i += 2;
                    continue;
                }
                if (next == '\\')
                {
                    wordBuf.Append('\\');
                    inWord = true;
                    i += 2;
                    continue;
                }
                if (inDouble && next == '"')
                {
                    wordBuf.Append('"');
                    anyQuoted = true;
                    inWord = true;
                    i += 2;
                    continue;
                }
                if (inSingle && next == '\'')
                {
                    wordBuf.Append('\'');
                    anyQuoted = true;
                    inWord = true;
                    i += 2;
                    continue;
                }
                // Other escapes: pass through literally.
                wordBuf.Append(c);
                wordBuf.Append(next);
                inWord = true;
                i += 2;
                continue;
            }

            // ── Quote toggle ──────────────────────────────────────────────
            if (!inDouble && c == '\'')
            {
                inSingle = !inSingle;
                anyQuoted = true;
                inWord = true;
                i++;
                continue;
            }
            if (!inSingle && c == '"')
            {
                inDouble = !inDouble;
                anyQuoted = true;
                inWord = true;
                i++;
                continue;
            }

            // ── ${expr} interpolation ─────────────────────────────────────
            if (c == '$' && i + 1 < len && rawArgs[i + 1] == '{')
            {
                // Find matching '}'.
                int exprStart = i + 2;
                int depth = 1;
                int j = exprStart;
                while (j < len && depth > 0)
                {
                    if (rawArgs[j] == '{') depth++;
                    else if (rawArgs[j] == '}') depth--;
                    j++;
                }

                string exprText = depth == 0
                    ? rawArgs[exprStart..(j - 1)]
                    : rawArgs[exprStart..]; // unclosed — best effort

                string interpolated = EvaluateExpression(exprText, vm, span);

                // Interpolation result is NOT word-split — it becomes part of
                // the current word fragment (even if it contains spaces).
                wordBuf.Append(interpolated);
                inWord = true;
                // Interpolation results are treated as quoted (no glob on the result).
                anyQuoted = true;
                i = j; // skip past the closing '}'
                continue;
            }

            // ── Word splitting (unquoted whitespace) ──────────────────────
            if (!inSingle && !inDouble && (c == ' ' || c == '\t' || c == '\n'))
            {
                FlushWord();
                i++;
                continue;
            }

            // ── Tilde expansion at start of an unquoted word ──────────────
            if (!inSingle && !inDouble && c == '~' && !inWord)
            {
                // Leading ~ or ~/... → home dir.
                if (i + 1 >= len || rawArgs[i + 1] == ' ' || rawArgs[i + 1] == '\t' ||
                    rawArgs[i + 1] == '/' || rawArgs[i + 1] == '\\')
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    wordBuf.Append(home);
                    inWord = true;
                    i++;
                    // Skip the separator if it was '/' so it merges cleanly.
                    if (i < len && rawArgs[i] == '/')
                    {
                        wordBuf.Append(Path.DirectorySeparatorChar);
                        i++;
                    }
                    continue;
                }
            }

            // ── Regular character ─────────────────────────────────────────
            wordBuf.Append(c);
            inWord = true;
            i++;
        }

        FlushWord();
    }

    // ── Expression evaluation ────────────────────────────────────────────────

    private static string EvaluateExpression(string exprText, VirtualMachine vm, SourceSpan? span)
    {
        if (string.IsNullOrWhiteSpace(exprText))
            return "";

        try
        {
            var lexer = new Lexer(exprText, "<shell-interp>");
            List<Token> tokens = lexer.ScanTokens();

            if (lexer.Errors.Count > 0)
                throw new RuntimeError(
                    $"interpolation error in shell command: {lexer.Errors[0]}",
                    span, StashErrorTypes.CommandError);

            var parser = new Parser(tokens);
            Expr expr = parser.Parse();

            if (parser.Errors.Count > 0)
                throw new RuntimeError(
                    $"interpolation error in shell command: {parser.Errors[0]}",
                    span, StashErrorTypes.CommandError);

            Chunk chunk = Compiler.CompileExpression(expr);
            object? result = vm.Execute(chunk);
            return RuntimeValues.Stringify(result);
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeError(
                $"interpolation error in shell command: {ex.Message}",
                span, StashErrorTypes.CommandError);
        }
    }
}
