namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError(Description = "Array or string index is out of bounds.")]
public sealed class IndexError : RuntimeError
{
    public IndexError(string message, SourceSpan? span = null) : base(message, span) {}
}
