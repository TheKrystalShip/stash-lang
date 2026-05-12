namespace Stash.Runtime.Errors;

using Stash.Common;

[StashError]
public sealed class IndexError : RuntimeError
{
    public IndexError(string message, SourceSpan? span = null) : base(message, span) {}
}
