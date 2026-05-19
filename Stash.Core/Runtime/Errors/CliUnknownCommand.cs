namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Runtime;

[StashError(
    Description = "A subcommand name was not declared.",
    Properties = new[] { "name", "candidates" },
    PropertyTypes = new[] { "string", "array<string>" })]
public sealed class CliUnknownCommand : RuntimeError
{
    public string Name { get; }
    public IReadOnlyList<string> Candidates { get; }

    public CliUnknownCommand(string message, string name, IReadOnlyList<string> candidates, SourceSpan? span = null)
        : base(message, span)
    {
        Name = name;
        Candidates = candidates;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["name"] = Name,
        ["candidates"] = Candidates.Select(StashValue.FromObj).ToList(),
    };
}
