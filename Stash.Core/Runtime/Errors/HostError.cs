namespace Stash.Runtime.Errors;

using Stash.Common;
using Stash.Runtime;

[StashError(Description = "A CLR exception escaped a host-registered member delegate during Stash-to-host dispatch.")]
public sealed class HostError : RuntimeError
{
    public HostError(string message, SourceSpan? span = null) : base(message, span) {}
}
