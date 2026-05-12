namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError]
public sealed class CancellationError : RuntimeError
{
    public CancellationError(string message, SourceSpan? span = null) : base(message, span, StashErrorTypes.CancellationError) {}
}
