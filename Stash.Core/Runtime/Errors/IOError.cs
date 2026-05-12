namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError]
public sealed class IOError : RuntimeError
{
    public IOError(string message, SourceSpan? span = null) : base(message, span, StashErrorTypes.IOError) {}
}
