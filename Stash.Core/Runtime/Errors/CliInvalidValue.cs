namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

[StashError(
    Description = "Type conversion or choices membership failed.",
    Properties = new[] { "option", "value", "expected" },
    PropertyTypes = new[] { "string?", "string", "string" })]
public sealed class CliInvalidValue : RuntimeError
{
    public string? Option { get; }
    public string Value { get; }
    public string Expected { get; }

    public CliInvalidValue(string message, string? option, string value, string expected, SourceSpan? span = null)
        : base(message, span)
    {
        Option = option;
        Value = value;
        Expected = expected;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["option"] = Option,
        ["value"] = Value,
        ["expected"] = Expected,
    };
}
