namespace Stash.Cli.Completion;

internal enum CandidateKind
{
    File,
    Directory,
    Executable,
    Sugar,
    Alias,
    StashGlobal,
    StashNamespace,
    StashFunction,
    StashKeyword,
    StashMember,
    Custom
}

internal sealed record Candidate(string Display, string Insert, CandidateKind Kind);
