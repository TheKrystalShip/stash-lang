namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError(Description = "Value is invalid for the operation (out-of-range, empty, or malformed).")]
public sealed class ValueError : RuntimeError
{
    public ValueError(string message, SourceSpan? span = null) : base(message, span) {}
}
