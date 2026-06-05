namespace Stash.Hosting.Internal;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Errors;

/// <summary>
/// A host method (sync or async) bound to a specific CLR target object, implementing
/// <see cref="IStashCallable"/> so the VM's existing <c>ExecuteCall</c> path
/// invokes it with zero bytecode changes.
/// </summary>
/// <remarks>
/// <para>
/// Returned by <see cref="HostHandle.VMTryGetField"/> when the accessed member is a
/// registered synchronous or asynchronous method. The VM sees an <see cref="IStashCallable"/>
/// and calls it via <see cref="CallDirect"/> exactly as it would any other Stash callable.
/// </para>
/// <para>
/// <b>Sync dispatch chain:</b>
/// <c>HostHandle.VMTryGetField</c> → <see cref="HostBoundMethod"/> (<see cref="IStashCallable"/>)
/// → <c>ExecuteCall</c> → <see cref="CallDirect"/> → <see cref="InvokeHostDelegate.InvokeMethod"/>.
/// </para>
/// <para>
/// <b>Async dispatch chain:</b>
/// <c>HostHandle.VMTryGetField</c> → <see cref="HostBoundMethod"/> (<see cref="IStashCallable"/>)
/// → <c>ExecuteCall</c> → <see cref="CallDirect"/> → <see cref="InvokeHostDelegate.InvokeAsyncMethod"/>
/// → returns <c>StashValue(StashFuture)</c> → VM <c>await</c> opcode blocks on <c>GetResult()</c>.
/// </para>
/// <para>
/// <b>Arity.</b> <see cref="Arity"/> matches the number of Stash-visible parameters
/// (i.e. the delegate parameter count minus the target, and minus the trailing
/// <c>CancellationToken</c> for async methods). The additional check inside
/// <see cref="CallDirect"/> provides a belt-and-suspenders HostError with the descriptive
/// "TypeName.method expects N argument(s), got M" message.
/// </para>
/// </remarks>
internal sealed class HostBoundMethod : IStashCallable
{
    private readonly object _target;
    private readonly string _typeName;
    private readonly string _methodName;
    private readonly int    _arity;
    private readonly bool   _isAsync;

    // One of these is non-null depending on _isAsync.
    private readonly HostMethodInvoker? _invoker;
    private readonly HostAsyncMethodInvoker? _asyncInvoker;

    /// <summary>
    /// Full host registration map forwarded to <see cref="InvokeHostDelegate.InvokeMethod"/>
    /// so return values of registered types are wrapped as <see cref="HostHandle"/> values.
    /// May be <c>null</c> when the handle was created without a host context.
    /// </summary>
    private readonly IReadOnlyDictionary<Type, HostTypeRegistration>? _allRegistrations;

    /// <summary>
    /// Per-host observed-targets table. When non-null, any <see cref="HostHandle"/>
    /// created for a return value of a registered type is registered here so
    /// <c>DisposeAsync</c> can invoke <c>OnRelease</c> exactly once per unique target.
    /// </summary>
    private readonly ConditionalWeakTable<object, HostTypeRegistration>? _observedTargets;

    internal HostBoundMethod(
        object            target,
        string            typeName,
        string            methodName,
        int               arity,
        HostMethodInvoker? invoker,
        IReadOnlyDictionary<Type, HostTypeRegistration>? allRegistrations = null,
        bool isAsync = false,
        HostAsyncMethodInvoker? asyncInvoker = null,
        ConditionalWeakTable<object, HostTypeRegistration>? observedTargets = null)
    {
        _target           = target     ?? throw new ArgumentNullException(nameof(target));
        _typeName         = typeName   ?? throw new ArgumentNullException(nameof(typeName));
        _methodName       = methodName ?? throw new ArgumentNullException(nameof(methodName));
        _arity            = arity;
        _isAsync          = isAsync;
        _invoker          = invoker;
        _asyncInvoker     = asyncInvoker;
        _allRegistrations = allRegistrations;
        _observedTargets  = observedTargets;

        // Validate: exactly one invoker must be provided.
        if (!isAsync && invoker is null)
            throw new ArgumentNullException(nameof(invoker), "Sync invoker required for non-async method.");
        if (isAsync && asyncInvoker is null)
            throw new ArgumentNullException(nameof(asyncInvoker), "Async invoker required for async method.");
    }

    // ── IStashCallable ────────────────────────────────────────────────────

    /// <summary>
    /// Returns -1 to opt out of the VM's generic arity check.
    /// The arity check is performed inside <see cref="CallDirect"/> instead, so it can
    /// throw a <see cref="HostError"/> (Kind = <c>StashError.KindHostError</c>) rather
    /// than the VM's generic <see cref="RuntimeError"/>.
    /// </summary>
    public int Arity => -1;

    /// <inheritdoc/>
    public string? Name => $"{_typeName}.{_methodName}";

    /// <summary>
    /// Legacy call path (list-based). Bridges through <see cref="CallDirect"/>.
    /// </summary>
    public object? Call(IInterpreterContext context, List<object?> arguments)
    {
        StashValue[] svArgs = new StashValue[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
            svArgs[i] = StashValue.FromObject(arguments[i]);
        return CallDirect(context, svArgs).ToObject();
    }

    /// <summary>
    /// Zero-allocation call path invoked by the bytecode VM's <c>ExecuteCall</c>.
    /// Checks arity, marshals arguments, then delegates to the appropriate chokepoint:
    /// <see cref="InvokeHostDelegate.InvokeMethod"/> (sync) or
    /// <see cref="InvokeHostDelegate.InvokeAsyncMethod"/> (async).
    /// </summary>
    /// <remarks>
    /// For async methods, the return value is a <see cref="Stash.Runtime.Types.StashFuture"/>
    /// wrapped in a <see cref="StashValue"/>. The VM's <c>await</c> opcode blocks on it via
    /// <c>StashFuture.GetResult()</c>. The engine's cancellation token is threaded into the
    /// delegate via the linked CTS created in
    /// <see cref="InvokeHostDelegate.InvokeAsyncMethod"/>.
    /// </remarks>
    /// <exception cref="HostError">
    /// Thrown (with Kind = <c>StashError.KindHostError</c> once surfaced) when:
    /// <list type="bullet">
    ///   <item>The argument count does not match <see cref="Arity"/>.</item>
    ///   <item>An argument cannot be marshalled to the expected CLR type.</item>
    ///   <item>The delegate body throws a CLR exception (sync: immediate; async: task fault).</item>
    /// </list>
    /// </exception>
    public StashValue CallDirect(IInterpreterContext context, ReadOnlySpan<StashValue> arguments)
    {
        // Arity check — produces the precise "TypeName.method expects N argument(s), got M" message.
        if (arguments.Length != _arity)
        {
            throw new HostError(
                $"{_typeName}.{_methodName} expects {_arity} " +
                $"{(_arity == 1 ? "argument" : "arguments")}, got {arguments.Length}");
        }

        // Copy to array — the baked invoker captures the array; span cannot be stored.
        StashValue[] stashArgs = new StashValue[_arity];
        arguments.CopyTo(stashArgs);

        if (_isAsync)
        {
            // Async path: start the task and return a StashFuture.
            // The context's CancellationToken is linked into the delegate when it declared
            // a trailing CT parameter at registration time (detected once by AsyncMethod).
            return InvokeHostDelegate.InvokeAsyncMethod(
                asyncInvoker:    _asyncInvoker!,
                target:          _target,
                stashArgs:       stashArgs,
                ct:              context.CancellationToken,
                span:            null,
                allRegistrations: _allRegistrations,
                observedTargets: _observedTargets);
        }

        // Sync path: route through the single chokepoint that maps CLR exceptions to HostError.
        // Pass the full registrations map so return values of registered types are wrapped
        // as HostHandle values rather than falling through to the ArgumentException path.
        return InvokeHostDelegate.InvokeMethod(
            invoker:          _invoker!,
            target:           _target,
            stashArgs:        stashArgs,
            allRegistrations: _allRegistrations,
            span:             null,
            observedTargets:  _observedTargets);
    }

    public override string ToString() => $"<host method {_typeName}.{_methodName}>";
}
