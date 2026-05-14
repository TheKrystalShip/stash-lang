namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError(Description = "External cancellation (Ctrl-C or programmatic cancellation).")]
public sealed class CancellationError : RuntimeError
{
    public CancellationError(string message, SourceSpan? span = null) : base(message, span) {}
}
