namespace Stash.Hosting.Internal;

using System;
using System.Collections.Generic;
using System.Reflection;
using Stash.Common;
using Stash.Hosting.Marshalling;
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

    /// <summary>
    /// Invokes a baked method invoker on <paramref name="target"/> with the given
    /// Stash arguments and returns the marshalled result.
    /// </summary>
    /// <remarks>
    /// This is the <b>only</b> place in <c>Stash.Hosting</c> that executes a registered
    /// method delegate — maintaining the Construct-lite chokepoint for method dispatch.
    /// Arg-marshalling errors (kind = <see cref="HostError"/>) are thrown before reaching
    /// this method (inside the baked invoker created in
    /// <see cref="HostTypeBuilder{T}.Method"/>). Any CLR exception from the <em>body</em>
    /// of the delegate that escapes the baked closure is caught here and re-thrown as a
    /// <see cref="HostError"/>, preserving the call-site span.
    /// </remarks>
    /// <param name="invoker">The baked method invoker from the descriptor.</param>
    /// <param name="target">The live CLR host object (first delegate parameter).</param>
    /// <param name="stashArgs">
    /// Stash-value arguments for the method (excluding the target). Arity was already
    /// validated by <see cref="HostBoundMethod.CallDirect"/> before this call.
    /// </param>
    /// <param name="allRegistrations">
    /// Full host registration map used to wrap registered CLR return values as
    /// <see cref="HostHandle"/> values. May be <c>null</c> (handles created without a
    /// full host context).
    /// </param>
    /// <param name="span">The call site span, forwarded to any thrown error.</param>
    /// <returns>The marshalled return value as a <see cref="StashValue"/>.</returns>
    /// <exception cref="HostError">
    /// Thrown when the method delegate throws any <see cref="Exception"/> that is not
    /// already a <see cref="RuntimeError"/> (which includes <see cref="HostError"/>).
    /// The original exception's message is preserved.
    /// </exception>
    internal static StashValue InvokeMethod(
        HostMethodInvoker                                   invoker,
        object                                              target,
        StashValue[]                                        stashArgs,
        IReadOnlyDictionary<Type, HostTypeRegistration>?   allRegistrations,
        SourceSpan?                                         span)
    {
        object? rawResult;

        try
        {
            rawResult = invoker(target, stashArgs);
        }
        catch (RuntimeError)
        {
            // HostError and other RuntimeError subclasses already have the right shape;
            // let them propagate so the VM's catch path surfaces them correctly.
            throw;
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // DynamicInvoke wraps the real exception in TargetInvocationException;
            // unwrap and re-surface as HostError preserving the original message.
            Exception inner = tie.InnerException;
            if (inner is RuntimeError re) throw re; // already structured
            throw new HostError(inner.Message, span);
        }
        catch (Exception ex)
        {
            throw new HostError(ex.Message, span);
        }

        // Return-value marshalling is intentionally OUTSIDE the try/catch above.
        //
        // done_when #6: "an unregistered return type throws the existing ArgumentException
        // from the marshaller chokepoint." If the host registers a method with an
        // unrecognised CLR return type, ToStash throws ArgumentException. This is a HOST
        // CONFIGURATION BUG — not a catchable Stash HostError. The ArgumentException must
        // NOT be converted to HostError (which the VM's own ExecuteCall wrapper would then
        // surface as a generic RuntimeError("Built-in function error: …"), keeping it
        // uncatchable and distinct from HostError — exactly the "loud, uncatchable marshaller
        // exception" the spec requires).
        return HostMarshaller.ToStash(rawResult, allRegistrations);
    }
}
