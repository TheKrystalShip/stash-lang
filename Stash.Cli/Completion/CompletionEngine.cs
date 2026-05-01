using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Bytecode;
using Stash.Cli.Completion.Completers;
using Stash.Cli.Shell;
using Stash.Runtime;

namespace Stash.Cli.Completion;

/// <summary>
/// Orchestrates the tab-completion procedure (spec §5.1–§5.7).
/// Given a buffer string and cursor position, returns a <see cref="CompletionResult"/>
/// containing filtered candidates and a longest-common-prefix string.
/// </summary>
internal sealed class CompletionEngine
{
    private readonly ShellLineClassifier _classifier;
    private readonly CompletionDeps _deps;

    private readonly CommandCompleter _commandCompleter = new();
    private readonly PathCompleter _pathCompleter = new();
    private readonly StashIdentifierCompleter _stashIdentifierCompleter = new();
    private readonly DottedMemberCompleter _dottedMemberCompleter = new();
    private readonly CustomCompleterDispatch _customDispatch = new();

    public CompletionEngine(
        VirtualMachine vm,
        PathExecutableCache pathCache,
        CustomCompleterRegistry customCompleters,
        ShellLineClassifier classifier,
        TextWriter? errorOutput = null)
    {
        _classifier = classifier;
        _deps = new CompletionDeps(vm, pathCache, customCompleters, errorOutput ?? Console.Error);
    }

    /// <summary>
    /// Runs the full completion procedure for the given buffer and cursor position.
    /// </summary>
    public CompletionResult Complete(string buffer, int cursor)
    {
        // Phase 1 — Mode classification
        LineMode lineMode = _classifier.Classify(buffer);
        CompletionMode mode = lineMode == LineMode.Stash
            ? CompletionMode.Stash
            : CompletionMode.Shell;

        // Phase 2 — Cursor context probe
        CursorContext ctx = TokenAtCursor.Probe(buffer, cursor, mode);
        if (ctx.Mode == CompletionMode.None)
            return CompletionResult.Empty;

        // Phase 3 — Glob/brace short-circuit
        string token = ctx.TokenText;
        // Also trigger on the char immediately before the token (e.g., Stash mode where * is a word
        // boundary so the token itself is empty but the user just typed a glob character).
        char charBeforeToken = ctx.ReplaceStart > 0 && ctx.ReplaceStart <= buffer.Length
            ? buffer[ctx.ReplaceStart - 1] : '\0';
        bool globBeforeToken = charBeforeToken is '*' or '?' or '[' or '{';
        if ((ContainsGlobChar(token) || globBeforeToken) && !IsTildePath(token))
            return new CompletionResult(ctx.ReplaceStart, ctx.ReplaceEnd, Array.Empty<Candidate>(), string.Empty);

        // Phase 4 — Dispatch to completer
        IReadOnlyList<Candidate> rawCandidates;
        int adjustedReplaceStart = ctx.ReplaceStart;
        string filterPrefix = token; // used in Phase 5; may be overridden for dotted members

        // §11.3 — alias/unalias sugar position completions (before mode routing).
        // The `alias` namespace name causes the classifier to route these lines as Stash,
        // but the shell-sugar semantics require alias-name / executable completion here.
        if (IsAliasNamePosition(buffer, ctx.ReplaceStart))
        {
            rawCandidates = BuildAliasCandidates(_deps);
        }
        else if (IsAliasValuePosition(buffer, ctx.ReplaceStart))
        {
            // Complete PATH executables only at the body position (alias <name> = <TAB>)
            rawCandidates = BuildExecutableCandidates(_deps);
        }
        else if (ctx.Mode == CompletionMode.Stash || ctx.Mode == CompletionMode.Substitution)
        {
            if (token.Contains('.'))
            {
                rawCandidates = _dottedMemberCompleter.Complete(ctx, _deps);

                // Adjust replaceStart to point after the last dot, so only the suffix is replaced.
                int lastDotIndex = token.LastIndexOf('.');
                adjustedReplaceStart = ctx.ReplaceStart + lastDotIndex + 1;
                // Filter Phase 5 using only the suffix (the part after the last dot).
                filterPrefix = token[(lastDotIndex + 1)..];
            }
            else
            {
                rawCandidates = _stashIdentifierCompleter.Complete(ctx, _deps);
            }
        }
        else // Shell mode
        {
            bool atCommandPosition = IsAtCommandPosition(buffer, ctx.ReplaceStart);
            bool isPathLike = IsPathLike(token);

            if (atCommandPosition && !isPathLike)
            {
                rawCandidates = _commandCompleter.Complete(ctx, _deps);
            }
            else if (!atCommandPosition)
            {
                // Determine the command name for custom completer lookup
                string commandName = ExtractCommandName(buffer, ctx.ReplaceStart);

                if (!string.IsNullOrEmpty(commandName))
                {
                    // §11.2 — template alias argument completion delegates to underlying command
                    if (_deps.Vm.AliasRegistry.TryGet(commandName, out AliasRegistry.AliasEntry? aliasEntry) &&
                             aliasEntry!.Kind == AliasRegistry.AliasKind.Template)
                    {
                        string underlyingCmd = ExtractFirstWordFromTemplate(aliasEntry.TemplateBody!);
                        if (!string.IsNullOrEmpty(underlyingCmd))
                        {
                            var customResult = _customDispatch.TryDispatch(ctx, _deps, underlyingCmd);
                            rawCandidates = customResult ?? _pathCompleter.Complete(ctx, _deps);
                        }
                        else
                        {
                            rawCandidates = _pathCompleter.Complete(ctx, _deps);
                        }
                    }
                    else
                    {
                        var customResult = _customDispatch.TryDispatch(ctx, _deps, commandName);
                        rawCandidates = customResult ?? _pathCompleter.Complete(ctx, _deps);
                    }
                }
                else
                {
                    rawCandidates = _pathCompleter.Complete(ctx, _deps);
                }
            }
            else
            {
                // Path-like token at any position
                rawCandidates = _pathCompleter.Complete(ctx, _deps);
            }
        }

        // Phase 5 — Smart-case prefix filter
        var filtered = new List<Candidate>(rawCandidates.Count);
        foreach (var c in rawCandidates)
        {
            if (SmartCaseMatcher.Matches(filterPrefix, c.Insert))
                filtered.Add(c);
        }

        // Phase 6 — Compute longest common prefix
        bool caseSensitive = SmartCaseMatcher.HasUpper(filterPrefix);
        string lcp = SmartCaseMatcher.LongestCommonPrefix(filtered.Select(c => c.Insert), caseSensitive);

        // Phase 7 — Sort + return
        filtered.Sort(static (a, b) =>
            string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));

        return new CompletionResult(adjustedReplaceStart, ctx.ReplaceEnd, filtered, lcp);
    }

    /// <summary>
    /// Writes a yes/no prompt to stdout and reads a single key.
    /// Returns <c>true</c> for y, Y, Space, or Enter.
    /// </summary>
    public bool PromptYesNo(string message)
    {
        Console.Write(message);
        try
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            Console.WriteLine();
            return key.KeyChar == 'y'
                || key.KeyChar == 'Y'
                || key.KeyChar == ' '
                || key.Key == ConsoleKey.Enter;
        }
        catch (InvalidOperationException)
        {
            // No console attached (e.g. in tests)
            Console.WriteLine();
            return false;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool ContainsGlobChar(string token)
    {
        foreach (char c in token)
        {
            if (c == '*' || c == '?' || c == '[' || c == '{')
                return true;
        }
        return false;
    }

    /// <summary>Returns true for a bare tilde or a tilde-slash prefix (exception to glob rule).</summary>
    private static bool IsTildePath(string token)
        => token == "~" || token.StartsWith("~/", StringComparison.Ordinal);

    private static bool IsPathLike(string token)
        => token.StartsWith("/", StringComparison.Ordinal)
        || token.StartsWith("./", StringComparison.Ordinal)
        || token.StartsWith("../", StringComparison.Ordinal)
        || token.StartsWith("~/", StringComparison.Ordinal)
        || (OperatingSystem.IsWindows() && token.Length > 0 && token[0] == '\\');

    /// <summary>
    /// Determines whether the cursor (at replaceStart) is at the command position of
    /// the current pipeline stage — i.e. the first token after <c>|</c>, start-of-buffer,
    /// or any leading <c>\</c> / <c>!</c> prefix.
    /// Walks back from replaceStart-1 past whitespace; if the result is start-of-buffer
    /// or a <c>|</c>, we are at command position.
    /// </summary>
    private static bool IsAtCommandPosition(string buffer, int replaceStart)
    {
        int i = replaceStart - 1;

        // Skip trailing whitespace before the token
        while (i >= 0 && char.IsWhiteSpace(buffer[i]))
            i--;

        if (i < 0)
            return true;  // nothing before the token → command position

        char c = buffer[i];
        if (c == '|')
            return true;

        // Skip leading \ or ! prefixes (they make the first real token forced/strict shell)
        if (c == '\\' || c == '!')
        {
            i--;
            while (i >= 0 && char.IsWhiteSpace(buffer[i]))
                i--;
            return i < 0;
        }

        return false;
    }

    /// <summary>
    /// Extracts the command name (the first token of the current pipeline stage)
    /// by scanning backward from <paramref name="replaceStart"/> past the current token,
    /// then collecting the word before it.
    /// Returns an empty string when the command name cannot be determined.
    /// </summary>
    private static string ExtractCommandName(string buffer, int replaceStart)
    {
        // The command name is the first non-whitespace token after |, start-of-buffer,
        // or a \ / ! prefix. We scan the slice [0..replaceStart].
        string slice = buffer.Length >= replaceStart ? buffer[..replaceStart] : buffer;

        // Find the start of the current stage by walking backward to find '|'
        int stageStart = 0;
        for (int i = slice.Length - 1; i >= 0; i--)
        {
            if (slice[i] == '|')
            {
                stageStart = i + 1;
                break;
            }
        }

        // Skip whitespace and \ / ! prefixes
        int pos = stageStart;
        while (pos < slice.Length && char.IsWhiteSpace(slice[pos]))
            pos++;
        while (pos < slice.Length && (slice[pos] == '\\' || slice[pos] == '!'))
            pos++;
        while (pos < slice.Length && char.IsWhiteSpace(slice[pos]))
            pos++;

        // Collect the command word
        int wordStart = pos;
        while (pos < slice.Length && !char.IsWhiteSpace(slice[pos]) && slice[pos] != '|')
            pos++;

        if (pos == wordStart)
            return string.Empty;

        return slice[wordStart..pos];
    }

    /// <summary>
    /// Returns <see langword="true"/> when the cursor is at the alias/command name position
    /// in <c>alias &lt;TAB&gt;</c> or <c>unalias &lt;TAB&gt;</c> — i.e. after the keyword
    /// word with no <c>=</c> character in the stage slice (§11.3).
    /// </summary>
    private static bool IsAliasNamePosition(string buffer, int replaceStart)
    {
        string stage = buffer[..Math.Min(replaceStart, buffer.Length)].TrimStart();
        // Strip leading force/strict prefixes
        int pos = 0;
        while (pos < stage.Length && (stage[pos] == '\\' || stage[pos] == '!'))
            pos++;
        stage = stage[pos..];
        return (stage.StartsWith("alias ", StringComparison.Ordinal) ||
                stage.StartsWith("unalias ", StringComparison.Ordinal)) &&
               !stage.Contains('=');
    }

    /// <summary>
    /// Returns <see langword="true"/> when the cursor is after the <c>=</c> in
    /// <c>alias name = &lt;TAB&gt;</c>, signalling that the user is completing the body
    /// executable (§11.3).
    /// </summary>
    private static bool IsAliasValuePosition(string buffer, int replaceStart)
    {
        string stage = buffer[..Math.Min(replaceStart, buffer.Length)].TrimStart();
        int pos = 0;
        while (pos < stage.Length && (stage[pos] == '\\' || stage[pos] == '!'))
            pos++;
        stage = stage[pos..];
        return stage.StartsWith("alias ", StringComparison.Ordinal) &&
               stage.Contains('=');
    }

    /// <summary>
    /// Returns the first non-placeholder word from a template alias body.
    /// Returns an empty string if the body starts with a <c>${</c> placeholder (§11.2).
    /// </summary>
    private static string ExtractFirstWordFromTemplate(string templateBody)
    {
        ReadOnlySpan<char> span = templateBody.AsSpan().TrimStart();
        int end = 0;
        while (end < span.Length && !char.IsWhiteSpace(span[end]))
            end++;
        string word = span[..end].ToString();
        return word.StartsWith("${", StringComparison.Ordinal) ? string.Empty : word;
    }

    /// <summary>
    /// Builds alias-name candidates from the VM's alias registry for use at the
    /// <c>alias</c> / <c>unalias</c> name-argument position (§11.3).
    /// </summary>
    private static IReadOnlyList<Candidate> BuildAliasCandidates(CompletionDeps deps)
    {
        var candidates = new List<Candidate>();
        foreach (string name in deps.Vm.AliasRegistry.Names())
            candidates.Add(new Candidate(name, name, CandidateKind.Alias));
        return candidates;
    }

    /// <summary>
    /// Builds PATH-executable candidates only, for use at the alias body position
    /// (<c>alias &lt;name&gt; = &lt;TAB&gt;</c>). Aliases and REPL globals are excluded (§11.3).
    /// </summary>
    private static IReadOnlyList<Candidate> BuildExecutableCandidates(CompletionDeps deps)
    {
        var candidates = new List<Candidate>();
        foreach (string exe in deps.PathCache.GetAllExecutables())
            candidates.Add(new Candidate(exe, exe, CandidateKind.Executable));
        return candidates;
    }
}
