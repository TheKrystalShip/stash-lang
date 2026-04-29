using System.Collections.Generic;

namespace Stash.Cli.Shell;

/// <summary>A single stage in a shell pipeline (one process).</summary>
internal sealed record ShellStage(string Program, string RawArgs);

/// <summary>Which stdio stream a redirect targets.</summary>
internal enum RedirectStream { Stdout, Stderr, Both }

/// <summary>A single I/O redirect clause attached to the last pipeline stage.</summary>
internal sealed record RedirectClause(RedirectStream Stream, bool Append, string Target);

/// <summary>
/// Parsed representation of a complete shell input line:
/// a sequence of pipe-connected stages with optional trailing redirects.
/// </summary>
internal sealed class ShellCommandLine
{
    /// <summary>Phase 5: true when the line was prefixed with '\'.</summary>
    public bool IsForced { get; init; }

    /// <summary>Phase 5: true when the line was prefixed with '!'.</summary>
    public bool IsStrict { get; init; }

    /// <summary>Ordered list of pipeline stages (at least one).</summary>
    public required IReadOnlyList<ShellStage> Stages { get; init; }

    /// <summary>Redirect clauses parsed from the tail of the last stage (empty when none).</summary>
    public IReadOnlyList<RedirectClause> Redirects { get; init; } = System.Array.Empty<RedirectClause>();
}
