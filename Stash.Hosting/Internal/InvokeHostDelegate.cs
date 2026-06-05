namespace Stash.Hosting.Internal;

using System;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Errors;

/// <summary>
/// The single function in <c>Stash.Hosting</c> that invokes a registered host delegate.
/// All member dispatch paths (property getter, property setter; method and async in P3/P4)
/// route through here. The try/catch converts any CLR exception that escapes the delegate
/// into a <see cref="HostError"/> so it surfaces to Stash as a structured, catchable error.
/// </summary>
/// <remarks>
/// <para>
/// <b>Construct-lite chokepoint.</b> No other code in <c>Stash.Hosting</c> may invoke
/// a registered delegate directly. This design mirrors the MVP's <c>HostMarshaller</c>
/// pattern: the wrapping try/catch cannot be skipped because there is no other API to
/// call a delegate through.
/// </para>
/// <para>
/// <b>Exception mapping.</b> Any <see cref="Exception"/> that escapes the delegate is
/// caught and re-thrown as <see cref="HostError"/> (a <c>[StashError]</c>-registered
/// built-in error type). <see cref="HostError"/> extends <see cref="RuntimeError"/>, so
/// the existing <c>StashHost</c> error-extraction path (<c>BuildStashError</c>) converts
/// it to a <see cref="Hosting.StashError"/> with
/// <c>Kind = StashError.KindHostError</c> automatically — no new catch branches needed.
/// </para>
/// </remarks>
internal static class InvokeHostDelegate
{
    /// <summary>
    /// Invokes a property getter on <paramref name="target"/> and returns the
    /// marshalled <see cref="StashValue"/>.
    /// </summary>
    /// <param name="getter">The registered getter delegate.</param>
    /// <param name="target">The live CLR host object.</param>
    /// <param name="span">The call site span, forwarded to any thrown error.</param>
    /// <returns>The marshalled result as a <see cref="StashValue"/>.</returns>
    /// <exception cref="HostError">
    /// Thrown when the getter throws any <see cref="Exception"/>; wraps the inner
    /// exception message and preserves the call-site span.
    /// </exception>
    internal static StashValue InvokeGetter(
        Func<object, StashValue> getter,
        object target,
        SourceSpan? span)
    {
        try
        {
            return getter(target);
        }
        catch (Exception ex) when (ex is not RuntimeError)
        {
            throw new HostError(ex.Message, span);
        }
    }

    /// <summary>
    /// Invokes a property setter on <paramref name="target"/> with
    /// <paramref name="value"/>.
    /// </summary>
    /// <param name="setter">The registered setter delegate.</param>
    /// <param name="target">The live CLR host object.</param>
    /// <param name="value">The marshalled Stash value to write.</param>
    /// <param name="span">The call site span, forwarded to any thrown error.</param>
    /// <exception cref="HostError">
    /// Thrown when the setter throws any <see cref="Exception"/>; wraps the inner
    /// exception message and preserves the call-site span.
    /// </exception>
    internal static void InvokeSetter(
        Action<object, StashValue> setter,
        object target,
        StashValue value,
        SourceSpan? span)
    {
        try
        {
            setter(target, value);
        }
        catch (Exception ex) when (ex is not RuntimeError)
        {
            throw new HostError(ex.Message, span);
        }
    }
}
