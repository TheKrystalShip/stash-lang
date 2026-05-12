namespace Stash.Runtime.Errors;

using System.Collections.Generic;
using Stash.Common;

[StashError(Properties = new[] { "path" })]
public sealed class LockError : RuntimeError
{
    public string? Path { get; }

    public LockError(string message, string? path = null, SourceSpan? span = null)
        : base(message, span)
    {
        Path = path;
    }

    protected internal override Dictionary<string, object?>? GetProperties() => new()
    {
        ["path"] = Path,
    };
}
