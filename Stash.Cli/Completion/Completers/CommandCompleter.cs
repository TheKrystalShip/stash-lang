using System;
using System.Collections.Generic;
using Stash.Cli.Shell;
using Stash.Runtime;

namespace Stash.Cli.Completion.Completers;

/// <summary>
/// Completes command names in shell mode when the cursor is on the first token
/// of a pipeline stage and the token is not path-like.
/// <para>
/// Sources (unioned, deduped by insert):
/// <list type="number">
///   <item>Shell-sugar names: <c>cd</c>, <c>pwd</c>, <c>exit</c>, <c>quit</c>.</item>
///   <item>PATH executables via <see cref="PathExecutableCache.GetAllExecutables"/>.</item>
///   <item>Callable REPL globals (functions, lambdas) via <see cref="Stash.Bytecode.VirtualMachine.EnumerateGlobals"/>.</item>
/// </list>
/// </para>
/// <para>
/// Leading <c>\</c> or <c>!</c> prefixes are stripped from <see cref="CursorContext.TokenText"/>
/// for matching but re-prepended on every <see cref="Candidate.Insert"/> value.
/// </para>
/// </summary>
internal sealed class CommandCompleter : ICompleter
{
    private static readonly string[] SugarNames = ["cd", "pwd", "exit", "quit"];

    public IReadOnlyList<Candidate> Complete(CursorContext ctx, CompletionDeps deps)
    {
        // Strip leading \ and ! prefixes (shell force/strict markers).
        string tokenText = ctx.TokenText;
        string prefix = string.Empty;
        while (tokenText.Length > 0 && (tokenText[0] == '\\' || tokenText[0] == '!'))
        {
            prefix += tokenText[0];
            tokenText = tokenText.Substring(1);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<Candidate>();

        // 1. Sugar names
        foreach (string sugar in SugarNames)
        {
            if (seen.Add(sugar))
                candidates.Add(new Candidate(sugar, prefix + sugar, CandidateKind.Sugar));
        }

        // 2. PATH executables
        foreach (string exe in deps.PathCache.GetAllExecutables())
        {
            if (seen.Add(exe))
                candidates.Add(new Candidate(exe, prefix + exe, CandidateKind.Executable));
        }

        // 3. Callable REPL globals (functions, lambdas, struct constructors)
        foreach (var (name, value, _) in deps.Vm.EnumerateGlobals())
        {
            if (value.ToObject() is IStashCallable && seen.Add(name))
                candidates.Add(new Candidate(name, prefix + name, CandidateKind.StashGlobal));
        }

        return candidates;
    }
}
