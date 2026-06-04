namespace Stash.Hosting;

using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// A structured error produced by a Stash script and surfaced to the host.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> names the Stash error type (e.g. <c>"ValueError"</c>,
/// <c>"TypeError"</c>, <c>"IOError"</c>) using the canonical name from
/// <see cref="Stash.Runtime.Errors.BuiltInErrorRegistry"/>, or the user-supplied
/// type name for <c>throw { type: "..." }</c> errors, or <see cref="KindCancelled"/>
/// for host-synthesized cancellation results.
/// </remarks>
public sealed record StashError(
    string Kind,
    string Message,
    SourceSpan? Span,
    IReadOnlyList<StackFrameInfo> CallStack)
{
    /// <summary>
    /// The synthetic error kind emitted by <see cref="IStashHost.TryCallAsync{T}"/> when
    /// the underlying call was cancelled via a <see cref="System.Threading.CancellationToken"/>.
    /// Defined here so it appears in exactly one place and callers can match it symbolically.
    /// </summary>
    public const string KindCancelled = "Cancelled";
}
