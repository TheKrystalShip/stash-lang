namespace Stash.Cli.History;

using System;
using System.Collections.Generic;

/// <summary>No-op history sink used when persistence is disabled.</summary>
internal sealed class NullHistorySink : IHistorySink
{
    public IReadOnlyList<string> Initial => Array.Empty<string>();
    public void Append(string entry) { }
}
