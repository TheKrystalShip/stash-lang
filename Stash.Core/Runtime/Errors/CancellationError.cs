namespace Stash.Runtime.Errors;

using Stash.Common;

[StashError]
public sealed class CancellationError : RuntimeError
{
    public CancellationError(string message, SourceSpan? span = null) : base(message, span) {}
}
