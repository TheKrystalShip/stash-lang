namespace Stash.Runtime.Errors;

using Stash.Common;

[StashError]
public sealed class TypeError : RuntimeError
{
    public TypeError(string message, SourceSpan? span = null) : base(message, span) {}
}
