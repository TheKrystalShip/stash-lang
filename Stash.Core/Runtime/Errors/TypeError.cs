namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError(Description = "Argument or operand has the wrong type.")]
public sealed class TypeError : RuntimeError
{
    public TypeError(string message, SourceSpan? span = null) : base(message, span) {}
}
