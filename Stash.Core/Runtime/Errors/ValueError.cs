namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError]
public sealed class ValueError : RuntimeError
{
    public ValueError(string message, SourceSpan? span = null) : base(message, span, StashErrorTypes.ValueError) {}
}
