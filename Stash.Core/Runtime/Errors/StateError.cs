namespace Stash.Runtime.Errors;

using Stash.Common;

[StashError]
public sealed class StateError : RuntimeError
{
    public StateError(string message, SourceSpan? span = null) : base(message, span) {}
}
