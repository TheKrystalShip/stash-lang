namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

[StashError(
    Description = "Extra positional after all positional slots are filled.",
    Properties = new[] { "value" },
    PropertyTypes = new[] { "string" })]
public sealed class CliUnexpectedPositional : RuntimeError
{
    public string Value { get; }

    public CliUnexpectedPositional(string message, string value, SourceSpan? span = null)
        : base(message, span)
    {
        Value = value;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["value"] = Value,
    };
}
