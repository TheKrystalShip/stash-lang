namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Runtime;

[StashError(
    Description = "A long-option prefix matches more than one declared option.",
    Properties = new[] { "option", "candidates" },
    PropertyTypes = new[] { "string", "array<string>" })]
public sealed class CliAmbiguousOption : RuntimeError
{
    public string Option { get; }
    public IReadOnlyList<string> Candidates { get; }

    public CliAmbiguousOption(string message, string option, IReadOnlyList<string> candidates, SourceSpan? span = null)
        : base(message, span)
    {
        Option = option;
        Candidates = candidates;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["option"] = Option,
        ["candidates"] = Candidates.Select(StashValue.FromObj).ToList(),
    };
}
