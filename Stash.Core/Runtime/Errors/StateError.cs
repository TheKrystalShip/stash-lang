namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError]
public sealed class StateError : RuntimeError
{
    public StateError(string message, SourceSpan? span = null) : base(message, span, StashErrorTypes.StateError) {}
}
