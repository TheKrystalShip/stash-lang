namespace Stash.Runtime.Errors;

using Stash.Common;

[StashError]
public sealed class ValueError : RuntimeError
{
    public ValueError(string message, SourceSpan? span = null) : base(message, span) {}
}
