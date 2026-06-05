namespace Stash.Hosting.Internal;

using System;
using System.Threading;
using System.Threading.Tasks;
using Stash.Runtime;

/// <summary>
/// Type-erased synchronous method marshaller baked at registration time.
/// The first argument is the live CLR target (typed as <c>object</c>); the second is the
/// Stash-value arguments array (NOT including the target).
/// Returns a <c>(object?[] clrArgs, Delegate handler)</c> tuple: the marshalled CLR argument
/// array (target at index 0, marshalled Stash args at indices 1..N) and the original registered
/// delegate. <see cref="InvokeHostDelegate.InvokeMethod"/> performs the actual
/// <c>handler.DynamicInvoke(clrArgs)</c> inside its try/catch — that call site is the
/// <b>structural chokepoint</b> for CLR-exception → <see cref="Stash.Runtime.Errors.HostError"/>
/// mapping.
/// </summary>
/// <remarks>
/// <para>
/// Reflection to determine parameter types happens ONCE at host setup (registration time),
/// never in the VM hot path. This is the same "reflection at setup, delegates at call time"
/// pattern approved in the brief's Q1 async-method delegate-signature note.
/// </para>
/// <para>
/// <b>Structural chokepoint contract.</b> The <c>Invoke</c> field on
/// <see cref="HostMemberDescriptor"/> MUST be dereferenced ONLY by
/// <see cref="InvokeHostDelegate.InvokeMethod"/>. Calling the closure directly bypasses
/// the CLR-exception → <see cref="Stash.Runtime.Errors.HostError"/> mapping that the
/// chokepoint's try/catch enforces — and, since the closure no longer calls
/// <c>DynamicInvoke</c> itself, would not even invoke the registered handler.
/// </para>
/// </remarks>
internal delegate (object?[] clrArgs, Delegate handler) HostMethodInvoker(object target, StashValue[] stashArgs);

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
/// <remarks>
/// <b>Convention-enforced chokepoint.</b> The <c>AsyncInvoke</c> field on
/// <see cref="HostMemberDescriptor"/> MUST be dereferenced ONLY by
/// <see cref="InvokeHostDelegate.InvokeAsyncMethod"/>. Calling the closure directly bypasses
/// the CLR-exception → <see cref="Stash.Runtime.Errors.HostError"/> mapping (the
/// CancellationToken-linked CTS setup, the sync-throw-at-start catch, and the bridge-task
/// fault mapping) that the chokepoint provides. This is a convention — not structural —
/// because the async closure performs the <c>DynamicInvoke</c> + <c>await</c> itself,
/// requiring the CancellationToken and task-bridging logic to remain inside the closure.
/// </remarks>
internal delegate Task<object?> HostAsyncMethodInvoker(object target, StashValue[] stashArgs, System.Threading.CancellationToken ct);

/// <summary>
/// Describes one registered member (property, method, or async method) on a host type.
/// P1: skeleton only — Invoke field added in P3 (sync methods), AsyncInvoke added in P4.
/// </summary>
/// <remarks>
/// <para>
/// <c>Invoke</c>: baked synchronous method marshaller, non-null for
/// <see cref="HostMemberKind.Method"/>. Signature:
/// <c>(object target, StashValue[] stashArgs) → (object?[] clrArgs, Delegate handler)</c>.
/// The closure marshals Stash args to CLR types; <see cref="InvokeHostDelegate.InvokeMethod"/>
/// performs the actual <c>DynamicInvoke</c> — that call site is the structural chokepoint.
/// </para>
/// <para>
/// <c>AsyncInvoke</c>: baked asynchronous method invoker, non-null for
/// <see cref="HostMemberKind.AsyncMethod"/>. Signature:
/// <c>(object target, StashValue[] stashArgs, CancellationToken) → Task&lt;object?&gt;</c>.
/// The task resolves with the raw CLR return value. <b>Must be dereferenced only by
/// <see cref="InvokeHostDelegate.InvokeAsyncMethod"/>.</b>
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
