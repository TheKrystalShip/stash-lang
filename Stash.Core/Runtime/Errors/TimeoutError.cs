namespace Stash.Runtime.Errors;

using Stash.Common;

[StashError]
public sealed class TimeoutError : RuntimeError
{
    public TimeoutError(string message, SourceSpan? span = null) : base(message, span) {}
}
