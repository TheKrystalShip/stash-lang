namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

[StashError(
    Description = "Schema construction failed (duplicate names, invalid default, unknown type tag).",
    Properties = new[] { "field", "reason" },
    PropertyTypes = new[] { "string", "string" })]
public sealed class CliSchemaError : RuntimeError
{
    public string Field { get; }
    public string Reason { get; }

    public CliSchemaError(string message, string field, string reason, SourceSpan? span = null)
        : base(message, span)
    {
        Field = field;
        Reason = reason;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["field"] = Field,
        ["reason"] = Reason,
    };
}
