using System;
using System.Collections.Generic;
using Stash.Stdlib;

namespace Stash.Cli.Completion.Completers;

/// <summary>
/// Completes namespace member names after a <c>.</c> in Stash or <c>${…}</c> mode.
/// Activated when <see cref="CursorContext.TokenText"/> contains a <c>.</c>.
/// <para>
/// Splits the token at the last <c>.</c> into <c>prefix</c> and <c>suffix</c>.
/// If <c>prefix</c> is a known stdlib namespace name, returns the namespace's
/// functions and constants (short names only, not qualified).
/// Otherwise returns no candidates (no type inference in v1).
/// </para>
/// </summary>
/// <remarks>
/// <b>Engine note (Phase 3):</b> when computing the replacement region, the engine
/// must set <c>replaceStart = ctx.TokenStart + lastDotIndex + 1</c> so that only
/// the <c>suffix</c> portion (after the last <c>.</c>) is replaced in the buffer.
/// This completer returns short member names; the engine is responsible for the offset.
/// </remarks>
internal sealed class DottedMemberCompleter : ICompleter
{
    public IReadOnlyList<Candidate> Complete(CursorContext ctx, CompletionDeps deps)
    {
        string token = ctx.TokenText;

        int lastDot = token.LastIndexOf('.');
        if (lastDot < 0)
            return [];

        string prefix = token.Substring(0, lastDot);

        // Only resolve stdlib namespace names — no type inference for user expressions.
        if (!StdlibRegistry.IsBuiltInNamespace(prefix))
            return [];

        var candidates = new List<Candidate>();

        // Namespace functions (short name)
        foreach (var fn in StdlibRegistry.GetNamespaceMembers(prefix))
        {
            string shortName = fn.Name;
            candidates.Add(new Candidate(shortName, shortName, CandidateKind.StashMember));
        }

        // Namespace constants (short name)
        foreach (var c in StdlibRegistry.GetNamespaceConstants(prefix))
        {
            string shortName = c.Name;
            candidates.Add(new Candidate(shortName, shortName, CandidateKind.StashMember));
        }

        return candidates;
    }
}
