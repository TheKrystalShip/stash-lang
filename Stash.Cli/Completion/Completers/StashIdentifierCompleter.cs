using System;
using System.Collections.Generic;
using Stash.Stdlib;

namespace Stash.Cli.Completion.Completers;

/// <summary>
/// Completes Stash identifiers in REPL Stash mode or inside <c>${…}</c> substitutions.
/// Activated when the token does not contain a <c>.</c> (otherwise
/// <see cref="DottedMemberCompleter"/> takes over).
/// <para>
/// Candidate sources (unioned, deduped by insert):
/// <list type="number">
///   <item>Stash keywords from <see cref="StdlibRegistry.Keywords"/>.</item>
///   <item>Global built-in functions from <see cref="StdlibRegistry.Functions"/>.</item>
///   <item>Stdlib namespace names from <see cref="StdlibRegistry.NamespaceNames"/>.</item>
///   <item>REPL globals (all, callable or not) from the VM.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class StashIdentifierCompleter : ICompleter
{
    public IReadOnlyList<Candidate> Complete(CursorContext ctx, CompletionDeps deps)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<Candidate>();

        // 1. Keywords
        foreach (string kw in StdlibRegistry.Keywords)
        {
            if (seen.Add(kw))
                candidates.Add(new Candidate(kw, kw, CandidateKind.StashKeyword));
        }

        // 2. Global built-in functions (e.g. println, print, readLine)
        // Note: println/print/readLine are hardcoded in StdlibRegistry but not in Functions list.
        foreach (string hardcoded in new[] { "println", "print", "readLine" })
        {
            if (seen.Add(hardcoded))
                candidates.Add(new Candidate(hardcoded, hardcoded, CandidateKind.StashFunction));
        }
        foreach (var fn in StdlibRegistry.Functions)
        {
            if (seen.Add(fn.Name))
                candidates.Add(new Candidate(fn.Name, fn.Name, CandidateKind.StashFunction));
        }

        // 3. Stdlib namespace names (e.g. fs, arr, str, math …)
        foreach (string ns in StdlibRegistry.NamespaceNames)
        {
            if (seen.Add(ns))
                candidates.Add(new Candidate(ns, ns, CandidateKind.StashNamespace));
        }

        // 4. REPL globals (all, callable or not)
        foreach (var (name, _, _) in deps.Vm.EnumerateGlobals())
        {
            if (seen.Add(name))
                candidates.Add(new Candidate(name, name, CandidateKind.StashGlobal));
        }

        return candidates;
    }
}
