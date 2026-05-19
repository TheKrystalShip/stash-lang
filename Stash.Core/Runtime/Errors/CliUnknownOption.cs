namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

[StashError(
    Description = "An option not declared in the schema was encountered.",
    Properties = new[] { "option" },
    PropertyTypes = new[] { "string" })]
public sealed class CliUnknownOption : RuntimeError
{
    public string Option { get; }

    public CliUnknownOption(string message, string option, SourceSpan? span = null)
        : base(message, span)
    {
        Option = option;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["option"] = Option,
    };
}
