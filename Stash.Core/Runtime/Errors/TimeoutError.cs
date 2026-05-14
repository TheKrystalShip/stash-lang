namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError(Description = "Operation timed out (timeout block or stdlib timeout parameter).")]
public sealed class TimeoutError : RuntimeError
{
    public TimeoutError(string message, SourceSpan? span = null) : base(message, span) {}
}
