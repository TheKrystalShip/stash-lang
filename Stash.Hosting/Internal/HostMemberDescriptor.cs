namespace Stash.Hosting.Internal;

using System;
using Stash.Runtime;

/// <summary>
/// Type-erased method invoker baked at registration time.
/// The first argument is the live CLR target (typed as <c>object</c>); the second is the
/// Stash-value arguments array (NOT including the target).
/// Returns the raw CLR result (<c>object?</c>) — return-value marshalling to
/// <see cref="Stash.Runtime.StashValue"/> is handled by
/// <see cref="InvokeHostDelegate.InvokeMethod"/> after the call, so it has access to the
/// full host registration map for wrapping registered CLR instances as
/// <see cref="HostHandle"/> values.
/// </summary>
/// <remarks>
/// Reflection to determine parameter types happens ONCE at host setup (registration time),
/// never in the VM hot path. This is the same "reflection at setup, delegates at call time"
/// pattern approved in the brief's Q1 async-method delegate-signature note.
/// </remarks>
internal delegate object? HostMethodInvoker(object target, StashValue[] stashArgs);

/// <summary>
/// Describes one registered member (property, method, or async method) on a host type.
/// P1: skeleton only — Invoke field added in P3 (sync methods).
/// P4 extends with async delegate storage.
/// </summary>
/// <remarks>
/// <para>
/// <c>Invoke</c>: baked synchronous method invoker, non-null for
/// <see cref="HostMemberKind.Method"/>. Signature:
/// <c>(object target, StashValue[] stashArgs) → StashValue</c>.
/// Argument and return marshalling are baked at registration time.
/// </para>
/// <para>
/// <c>MethodArity</c>: Stash-visible argument count (parameter count minus the target).
/// -1 for non-method members.
/// </para>
/// </remarks>
internal sealed record HostMemberDescriptor(
    HostMemberKind Kind,
    Func<object, StashValue>? Getter,
    Action<object, StashValue>? Setter,
    HostMethodInvoker? Invoke,
    int MethodArity = -1);

/// <summary>Discriminator for the kind of host member.</summary>
internal enum HostMemberKind
{
    Property,
    Method,
    AsyncMethod,
}
