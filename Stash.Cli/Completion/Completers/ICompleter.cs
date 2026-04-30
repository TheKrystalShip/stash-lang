using System.Collections.Generic;

namespace Stash.Cli.Completion.Completers;

/// <summary>
/// Contract for tab-completion sources. Each completer produces a list of candidates
/// for the current cursor context without filtering — the engine applies smart-case
/// prefix filtering after collection.
/// </summary>
internal interface ICompleter
{
    IReadOnlyList<Candidate> Complete(CursorContext ctx, CompletionDeps deps);
}
