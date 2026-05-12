namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError]
public sealed class ParseError : RuntimeError
{
    public ParseError(string message, SourceSpan? span = null) : base(message, span, StashErrorTypes.ParseError) {}
}
