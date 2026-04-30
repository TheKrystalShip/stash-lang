using System;
using System.Collections.Generic;

namespace Stash.Cli.Completion;

internal sealed record CompletionResult(
    int ReplaceStart,
    int ReplaceEnd,
    IReadOnlyList<Candidate> Candidates,
    string CommonPrefix)
{
    public static readonly CompletionResult Empty = new(
        ReplaceStart: 0,
        ReplaceEnd: 0,
        Candidates: Array.Empty<Candidate>(),
        CommonPrefix: string.Empty);
}
