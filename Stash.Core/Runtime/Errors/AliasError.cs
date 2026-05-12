namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime;

[StashError(Properties = new[] { "aliasName", "detail" })]
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
