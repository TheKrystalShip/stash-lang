namespace Stash.Hosting.Internal;

using System;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime;

/// <summary>
/// Type-erased synchronous method invoker baked at registration time.
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
/// Type-erased async method invoker baked at registration time.
/// The first argument is the live CLR target (typed as <c>object</c>); the second is the
/// Stash-value arguments array (NOT including the target); the third is the
/// <see cref="System.Threading.CancellationToken"/> to thread into the delegate when the
/// delegate accepted a trailing <c>CancellationToken</c> parameter.
/// Returns a <c>Task&lt;object?&gt;</c> whose result is the raw CLR return value (NOT
/// yet marshalled to <see cref="StashValue"/>). Marshalling is done by
/// <see cref="InvokeHostDelegate.InvokeAsyncMethod"/> where the full registrations map
/// and observed-targets table are available.
/// Faulted-task exceptions (non-<see cref="Stash.Runtime.Errors.RuntimeError"/>) are
/// mapped to <see cref="Stash.Runtime.Errors.HostError"/> by the baked closure so that
/// <c>StashFuture.GetResult()</c>'s <c>catch(RuntimeError){throw;}</c> surfaces them intact.
/// </summary>
internal delegate Task<object?> HostAsyncMethodInvoker(object target, StashValue[] stashArgs, System.Threading.CancellationToken ct);

/// <summary>
/// Describes one registered member (property, method, or async method) on a host type.
/// P1: skeleton only — Invoke field added in P3 (sync methods), AsyncInvoke added in P4.
/// </summary>
/// <remarks>
/// <para>
/// <c>Invoke</c>: baked synchronous method invoker, non-null for
/// <see cref="HostMemberKind.Method"/>. Signature:
/// <c>(object target, StashValue[] stashArgs) → object?</c>.
/// </para>
/// <para>
/// <c>AsyncInvoke</c>: baked asynchronous method invoker, non-null for
/// <see cref="HostMemberKind.AsyncMethod"/>. Signature:
/// <c>(object target, StashValue[] stashArgs, CancellationToken) → Task&lt;object?&gt;</c>.
/// The task resolves with a marshalled <see cref="StashValue"/> boxed as <c>object?</c>.
/// </para>
/// <para>
/// <c>MethodArity</c>: Stash-visible argument count (parameter count minus the target,
/// and minus the trailing CancellationToken when present for async methods).
/// -1 for non-method members.
/// </para>
/// </remarks>
internal sealed record HostMemberDescriptor(
    HostMemberKind Kind,
    Func<object, StashValue>? Getter,
    Action<object, StashValue>? Setter,
    HostMethodInvoker? Invoke,
    int MethodArity = -1,
    HostAsyncMethodInvoker? AsyncInvoke = null);

/// <summary>Discriminator for the kind of host member.</summary>
internal enum HostMemberKind
{
    Property,
    Method,
    AsyncMethod,
}
