namespace Stash.Cli.History;

using System.Collections.Generic;

/// <summary>Decouples LineEditor from history file I/O.</summary>
internal interface IHistorySink
{
    /// <summary>Initial history entries loaded at REPL startup, oldest-first.</summary>
    IReadOnlyList<string> Initial { get; }

    /// <summary>Persist a single accepted line. Implementations must apply the
    /// leading-space-skips and consecutive-dup rules per spec §6.</summary>
    void Append(string entry);
}
