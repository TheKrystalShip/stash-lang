namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

[StashError(
    Description = "A required positional or option was not supplied.",
    Properties = new[] { "name" },
    PropertyTypes = new[] { "string" })]
public sealed class CliMissingRequired : RuntimeError
{
    public string Name { get; }

    public CliMissingRequired(string message, string name, SourceSpan? span = null)
        : base(message, span)
    {
        Name = name;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["name"] = Name,
    };
}
