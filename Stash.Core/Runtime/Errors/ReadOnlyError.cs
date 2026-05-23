namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError(Description = "Thrown when an assignment targets a read-only namespace member, a const re-export, or a built-in namespace value.")]
public sealed class ReadOnlyError : RuntimeError
{
    public ReadOnlyError(string message, SourceSpan? span = null) : base(message, span) {}
}
