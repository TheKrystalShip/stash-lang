namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

[StashError(
    Description = "An option that requires a value appeared without one.",
    Properties = new[] { "option" },
    PropertyTypes = new[] { "string" })]
public sealed class CliMissingValue : RuntimeError
{
    public string Option { get; }

    public CliMissingValue(string message, string option, SourceSpan? span = null)
        : base(message, span)
    {
        Option = option;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["option"] = Option,
    };
}
