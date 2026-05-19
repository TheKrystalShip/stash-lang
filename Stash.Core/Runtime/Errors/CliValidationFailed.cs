namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

[StashError(
    Description = "min/max/pattern/validate rejected the value.",
    Properties = new[] { "option", "message" },
    PropertyTypes = new[] { "string?", "string" })]
public sealed class CliValidationFailed : RuntimeError
{
    public string? Option { get; }
    public string ValidationMessage { get; }

    public CliValidationFailed(string message, string? option, string validationMessage, SourceSpan? span = null)
        : base(message, span)
    {
        Option = option;
        ValidationMessage = validationMessage;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["option"] = Option,
        ["message"] = ValidationMessage,
    };
}
