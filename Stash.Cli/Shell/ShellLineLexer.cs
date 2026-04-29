using System;
using System.Collections.Generic;
using System.Text;

namespace Stash.Cli.Shell;

/// <summary>
/// Parses a raw shell input line into a <see cref="ShellCommandLine"/> AST.
///
/// Algorithm (§5.6):
///   1. Split on top-level '|' (not inside quotes or ${...}).
///   2. For the last stage, peel trailing redirect clauses.
///   3. For each stage: first whitespace-delimited token = program; rest = RawArgs.
///
/// Phase 4: '\' and '!' prefixes are NOT recognised here (classifier returns Stash for them).
/// </summary>
internal static class ShellLineLexer
{
    /// <summary>
    /// Parse the given shell input line and return a <see cref="ShellCommandLine"/>.
    /// The line may span multiple physical lines joined by '\n' (pipe-continuation).
    /// </summary>
    public static ShellCommandLine Parse(string line)
    {
        // Phase 5: peel optional '!' (strict) and '\' (force) prefixes before
        // splitting the line.  Only the combination !\ (in that order) is supported;
        // \! falls through to Stash and is never sent here.
        bool isStrict = false;
        bool isForced = false;

        int prefixEnd = 0;
        while (prefixEnd < line.Length && (line[prefixEnd] == ' ' || line[prefixEnd] == '\t'))
            prefixEnd++;

        if (prefixEnd < line.Length && line[prefixEnd] == '!')
        {
            isStrict = true;
            prefixEnd++;
        }

        if (prefixEnd < line.Length && line[prefixEnd] == '\\')
        {
            isForced = true;
            prefixEnd++;
        }

        string effectiveLine = prefixEnd > 0 ? line[prefixEnd..] : line;

        // Step 1: split on top-level '|' boundaries.
        var rawStages = SplitOnPipes(effectiveLine);

        // Step 2: peel redirect clauses from the last stage.
        string lastRaw = rawStages[rawStages.Count - 1];
        var (lastRawTrimmed, redirects) = PeelRedirects(lastRaw);
        rawStages[rawStages.Count - 1] = lastRawTrimmed;

        // Step 3: parse each stage into (Program, RawArgs).
        var stages = new List<ShellStage>(rawStages.Count);
        foreach (string raw in rawStages)
        {
            var (program, rawArgs) = SplitProgramAndArgs(raw.Trim());
            stages.Add(new ShellStage(program, rawArgs));
        }

        return new ShellCommandLine
        {
            Stages = stages,
            Redirects = redirects,
            IsStrict = isStrict,
            IsForced = isForced,
        };
    }

    // ── Step 1: top-level pipe split ────────────────────────────────────────

    private static List<string> SplitOnPipes(string line)
    {
        var stages = new List<string>();
        var current = new StringBuilder();
        int i = 0;
        int len = line.Length;
        bool inSingle = false;
        bool inDouble = false;
        int dollarDepth = 0; // depth of ${ } regions

        while (i < len)
        {
            char c = line[i];

            // Track quote state.
            if (!inDouble && c == '\'' && dollarDepth == 0)
            {
                inSingle = !inSingle;
                current.Append(c);
                i++;
                continue;
            }
            if (!inSingle && c == '"' && dollarDepth == 0)
            {
                inDouble = !inDouble;
                current.Append(c);
                i++;
                continue;
            }

            // Track ${ depth (so | inside ${expr} is not a pipe boundary).
            if (!inSingle && !inDouble && c == '$' && i + 1 < len && line[i + 1] == '{')
            {
                dollarDepth++;
                current.Append(c);
                i++;
                continue;
            }
            if (!inSingle && !inDouble && c == '{' && dollarDepth > 0)
            {
                dollarDepth++;
                current.Append(c);
                i++;
                continue;
            }
            if (!inSingle && !inDouble && c == '}' && dollarDepth > 0)
            {
                dollarDepth--;
                current.Append(c);
                i++;
                continue;
            }

            // Handle escape in double-quoted context.
            if (inDouble && c == '\\' && i + 1 < len)
            {
                current.Append(c);
                current.Append(line[i + 1]);
                i += 2;
                continue;
            }

            // Top-level pipe (not inside any quote or ${}).
            if (!inSingle && !inDouble && dollarDepth == 0 && c == '|')
            {
                stages.Add(current.ToString());
                current.Clear();
                i++;
                continue;
            }

            current.Append(c);
            i++;
        }

        // Add the final (or only) stage.
        stages.Add(current.ToString());
        return stages;
    }

    // ── Step 2: peel redirect clauses ───────────────────────────────────────

    private static readonly string[] RedirectOps =
    [
        "&>>",   // append both stdout+stderr
        "&>",    // truncate both
        "2>>",   // append stderr
        "2>",    // truncate stderr
        ">>",    // append stdout
        ">",     // truncate stdout
    ];

    private static (string Raw, List<RedirectClause> Redirects) PeelRedirects(string raw)
    {
        var redirects = new List<RedirectClause>();

        while (true)
        {
            string trimmed = raw.TrimEnd();
            bool found = false;

            foreach (string op in RedirectOps)
            {
                int opIdx = FindRedirectOp(trimmed, op);
                if (opIdx < 0) continue;

                // Everything after the op is the target.
                string afterOp = trimmed[(opIdx + op.Length)..].Trim();
                string target = UnquoteTarget(afterOp);

                var (stream, append) = op switch
                {
                    "&>>" => (RedirectStream.Both, true),
                    "&>"  => (RedirectStream.Both, false),
                    "2>>" => (RedirectStream.Stderr, true),
                    "2>"  => (RedirectStream.Stderr, false),
                    ">>"  => (RedirectStream.Stdout, true),
                    ">"   => (RedirectStream.Stdout, false),
                    _     => (RedirectStream.Stdout, false),
                };

                redirects.Insert(0, new RedirectClause(stream, append, target));
                raw = trimmed[..opIdx];
                found = true;
                break;
            }

            if (!found) break;
        }

        return (raw, redirects);
    }

    /// <summary>
    /// Find the last occurrence of <paramref name="op"/> in <paramref name="text"/>
    /// that is not inside quotes or ${...}.
    /// Returns -1 if not found.
    /// </summary>
    private static int FindRedirectOp(string text, string op)
    {
        int len = text.Length;
        bool inSingle = false, inDouble = false;
        int dollarDepth = 0;
        int last = -1;

        for (int i = 0; i < len; i++)
        {
            char c = text[i];

            if (!inDouble && c == '\'' && dollarDepth == 0) { inSingle = !inSingle; continue; }
            if (!inSingle && c == '"' && dollarDepth == 0) { inDouble = !inDouble; continue; }
            if (!inSingle && !inDouble && c == '$' && i + 1 < len && text[i + 1] == '{') { dollarDepth++; continue; }
            if (!inSingle && !inDouble && c == '{' && dollarDepth > 0) { dollarDepth++; continue; }
            if (!inSingle && !inDouble && c == '}' && dollarDepth > 0) { dollarDepth--; continue; }
            if (inSingle || inDouble || dollarDepth > 0) continue;

            if (i + op.Length <= len && text.AsSpan(i, op.Length).SequenceEqual(op.AsSpan()))
                last = i;
        }

        return last;
    }

    private static string UnquoteTarget(string target)
    {
        if (target.Length >= 2)
        {
            if ((target[0] == '"' && target[^1] == '"') ||
                (target[0] == '\'' && target[^1] == '\''))
                return target[1..^1];
        }
        // Return first whitespace-delimited token (in case of trailing junk).
        int ws = target.IndexOf(' ');
        return ws >= 0 ? target[..ws] : target;
    }

    // ── Step 3: split program and args ──────────────────────────────────────

    private static (string Program, string RawArgs) SplitProgramAndArgs(string stage)
    {
        if (string.IsNullOrEmpty(stage)) return ("", "");

        // The program is the first whitespace-delimited token (respecting quotes).
        int i = 0;
        int len = stage.Length;
        var sb = new StringBuilder();
        bool inSingle = false, inDouble = false;

        while (i < len)
        {
            char c = stage[i];
            if (!inDouble && c == '\'') { inSingle = !inSingle; sb.Append(c); i++; continue; }
            if (!inSingle && c == '"') { inDouble = !inDouble; sb.Append(c); i++; continue; }
            if (!inSingle && !inDouble && (c == ' ' || c == '\t')) break;
            sb.Append(c);
            i++;
        }

        string program = sb.ToString();
        // Strip any quotes from the program name.
        if (program.Length >= 2 &&
            ((program[0] == '"' && program[^1] == '"') ||
             (program[0] == '\'' && program[^1] == '\'')))
        {
            program = program[1..^1];
        }

        string rawArgs = i < len ? stage[i..].TrimStart() : "";
        return (program, rawArgs);
    }
}
