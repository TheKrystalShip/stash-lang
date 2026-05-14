namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

[StashError(
    Description = "Alias name is invalid or conflicts with an existing definition.",
    Properties = new[] { "aliasName", "detail" },
    PropertyTypes = new[] { "string?", "string?" })]
public sealed class AliasError : RuntimeError
{
    public string? AliasName { get; }
    public string? Detail { get; }

    public AliasError(string message, string? aliasName = null, string? detail = null, SourceSpan? span = null)
        : base(message, span)
    {
        AliasName = aliasName;
        Detail = detail;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["aliasName"] = AliasName,
        ["detail"] = Detail,
    };
}
