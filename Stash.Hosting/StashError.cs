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

    /// <summary>
    /// The synthetic error kind emitted when the VM exceeds the configured step limit
    /// (<see cref="StashHostOptions.StepLimit"/>). Defined here so all three construction
    /// sites in <see cref="StashHost"/> reference the same string from one place.
    /// </summary>
    public const string KindStepLimitExceeded = "StepLimitExceeded";

    /// <summary>
    /// The error kind emitted by <see cref="IStashHost.CompileAsync"/> when the source
    /// fails lexing, parsing, or semantic resolution. Defined here so callers can match
    /// it symbolically rather than comparing against a raw string literal.
    /// </summary>
    public const string KindParseError = "ParseError";

    /// <summary>
    /// The error kind produced when a CLR exception escapes a host-registered member
    /// delegate during Stash-to-host dispatch. Corresponds to the
    /// <see cref="Stash.Runtime.Errors.HostError"/> built-in error type registered via
    /// <c>[StashError]</c> in <c>Stash.Core</c>.
    /// Defined here (beside the other Kind* constants) so every host-dispatch error
    /// construction site can reference it symbolically — never inlined as a raw string.
    /// </summary>
    public const string KindHostError = "HostError";
}
