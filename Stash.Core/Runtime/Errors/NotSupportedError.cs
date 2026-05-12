namespace Stash.Runtime.Errors;

using Stash.Common;

[StashError]
public sealed class NotSupportedError : RuntimeError
{
    public NotSupportedError(string message, SourceSpan? span = null) : base(message, span) {}
}
