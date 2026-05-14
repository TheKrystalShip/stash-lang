namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError(Description = "Operation is not supported on this platform or for this value.")]
public sealed class NotSupportedError : RuntimeError
{
    public NotSupportedError(string message, SourceSpan? span = null) : base(message, span) {}
}
