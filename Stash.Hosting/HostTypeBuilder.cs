namespace Stash.Hosting;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Stash.Hosting.Internal;
using Stash.Hosting.Marshalling;
using Stash.Runtime;
using Stash.Runtime.Errors;

/// <summary>
/// Typed builder for declaring a CLR type's Stash-visible members.
/// Obtained via <see cref="IStashHost.RegisterType{T}"/>.
/// </summary>
/// <typeparam name="T">The CLR class being registered.</typeparam>
/// <remarks>
/// P1 supports only <see cref="Named"/> — the builder records the VM type name.
/// P2 adds <c>Property</c> overloads (read-only and read-write).
/// P3 adds <c>Method</c> (sync).
/// P4 adds <c>AsyncMethod</c> (Task-returning) and <c>OnRelease</c> (lifetime hook).
/// </remarks>
public sealed class HostTypeBuilder<T> where T : class
{
    private string _vmTypeName = typeof(T).Name;
    private readonly Dictionary<string, HostMemberDescriptor> _members = new(StringComparer.Ordinal);
    private Action<T>? _onRelease;

    /// <summary>
    /// Override the VM type name reported by <c>typeof(obj)</c> and used by <c>obj is Name</c>.
    /// Defaults to <c>typeof(T).Name</c>.
    /// </summary>
    /// <param name="vmTypeName">The name the script sees.</param>
    /// <returns>This builder (fluent API).</returns>
    public HostTypeBuilder<T> Named(string vmTypeName)
    {
        if (string.IsNullOrWhiteSpace(vmTypeName))
            throw new ArgumentException("VM type name must not be null or whitespace.", nameof(vmTypeName));
        _vmTypeName = vmTypeName;
        return this;
    }

    /// <summary>
    /// Registers a read-only property. The getter is invoked when Stash code reads
    /// <c>obj.<paramref name="name"/></c>; the result is marshalled via
    /// <see cref="HostMarshaller.ToStash(object?)"/>.
    /// </summary>
    /// <typeparam name="TValue">The CLR return type of the property.</typeparam>
    /// <param name="name">The member name visible in Stash.</param>
    /// <param name="get">Delegate that reads the property value from the CLR instance.</param>
    /// <returns>This builder (fluent API).</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="name"/> or <paramref name="get"/> is <c>null</c>.
    /// </exception>
    public HostTypeBuilder<T> Property<TValue>(string name, Func<T, TValue> get)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (get is null) throw new ArgumentNullException(nameof(get));

        // Bake marshalling into the getter closure so InvokeHostDelegate receives
        // a plain Func<object, StashValue> — no generic leakage into the dispatch path.
        Func<object, StashValue> getter = target => HostMarshaller.ToStash(get((T)target));

        _members[name] = new HostMemberDescriptor(
            Kind:    HostMemberKind.Property,
            Getter:  getter,
            Setter:  null,
            Invoke:  null);

        return this;
    }

    /// <summary>
    /// Registers a read-write property. The getter is invoked on read; the setter on
    /// <c>obj.<paramref name="name"/> = value</c>. Both are marshalled via
    /// <see cref="HostMarshaller"/>.
    /// </summary>
    /// <typeparam name="TValue">The CLR type of the property value.</typeparam>
    /// <param name="name">The member name visible in Stash.</param>
    /// <param name="get">Delegate that reads the property value from the CLR instance.</param>
    /// <param name="set">Delegate that writes the property value to the CLR instance.</param>
    /// <returns>This builder (fluent API).</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any argument is <c>null</c>.
    /// </exception>
    public HostTypeBuilder<T> Property<TValue>(string name, Func<T, TValue> get, Action<T, TValue> set)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (get is null) throw new ArgumentNullException(nameof(get));
        if (set is null) throw new ArgumentNullException(nameof(set));

        // Getter: bake out-marshalling.
        Func<object, StashValue> getter = target => HostMarshaller.ToStash(get((T)target));

        // Setter: bake in-marshalling. FromStash<TValue> converts the Stash value back to
        // the typed CLR value before calling the registered setter.
        Action<object, StashValue> setter = (target, sv) => set((T)target, HostMarshaller.FromStash<TValue>(sv)!);

        _members[name] = new HostMemberDescriptor(
            Kind:   HostMemberKind.Property,
            Getter: getter,
            Setter: setter,
            Invoke: null);

        return this;
    }

    /// <summary>
    /// Registers a synchronous method. When Stash code calls
    /// <c>obj.<paramref name="name"/>(...)</c>, the call is dispatched to
    /// <paramref name="handler"/> with the CLR instance as the first argument.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The delegate signature must have the CLR host type <typeparamref name="T"/> as its
    /// first parameter (the target), followed by zero or more typed parameters that map
    /// to the Stash arguments. Example: <c>(Player p, long dmg) =&gt; p.Attack(dmg)</c>
    /// registers a one-argument method.
    /// </para>
    /// <para>
    /// Parameter and return type marshalling are baked at registration time via
    /// <see cref="HostMarshaller"/>. Reflection is used ONCE at host setup to determine
    /// parameter types — never in the VM hot path (NAOT-clean per the brief's Q1 note).
    /// </para>
    /// <para>
    /// The arity reported to Stash is <c>handler.Method.GetParameters().Length - 1</c>
    /// (the first parameter — the target — is excluded from the Stash argument count).
    /// </para>
    /// </remarks>
    /// <param name="name">The method name visible in Stash.</param>
    /// <param name="handler">
    /// Delegate whose first parameter is the CLR host instance of type
    /// <typeparamref name="T"/>; subsequent parameters are marshalled from the Stash
    /// argument list.
    /// </param>
    /// <returns>This builder (fluent API).</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="name"/> or <paramref name="handler"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="handler"/>'s first parameter is not assignable from
    /// <typeparamref name="T"/>, meaning the delegate does not accept the registered type.
    /// </exception>
    public HostTypeBuilder<T> Method(string name, Delegate handler)
    {
        if (name is null)    throw new ArgumentNullException(nameof(name));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        MethodInfo mi = handler.Method;
        ParameterInfo[] parameters = mi.GetParameters();

        // Validate: first param must be T (or a supertype that T is assignable to).
        if (parameters.Length == 0 || !parameters[0].ParameterType.IsAssignableFrom(typeof(T)))
        {
            throw new ArgumentException(
                $"Method delegate's first parameter must be assignable from '{typeof(T).Name}'. " +
                $"Expected first parameter type '{typeof(T).Name}' (or a base class), " +
                $"but got '{(parameters.Length == 0 ? "<none>" : parameters[0].ParameterType.Name)}'.",
                nameof(handler));
        }

        // Determine arity: exclude the target (first) parameter.
        int arity = parameters.Length - 1;

        // Snapshot parameter types for marshalling (positions 1..N are the Stash args).
        // This reflection runs ONCE at registration time — NAOT-clean (brief Q1).
        Type[] argTypes = new Type[arity];
        for (int i = 0; i < arity; i++)
            argTypes[i] = parameters[i + 1].ParameterType;

        // Capture for closures.
        string typeName   = typeof(T).Name;
        string memberName = name;

        // Bake the marshaller closure.
        // This closure marshals Stash args to CLR types and returns
        // (clrArgs, handler) — the structural chokepoint InvokeHostDelegate.InvokeMethod
        // performs the actual DynamicInvoke(clrArgs) inside its own try/catch, making it
        // the sole call site for invoking the registered delegate.
        //
        // Per-arg marshalling (StashValue → expected CLR type) produces "arg N to T.m: ..."
        // messages when conversion fails, satisfying the P3 done_when requirement.
        //
        // DynamicInvoke is used by the chokepoint because the delegate's parameter types are
        // only known at registration time. This is acceptable in Stash.Hosting (an embedding
        // host SDK for managed-JIT consumers — not the VM dispatch loop or a NAOT-published
        // binary path). The per-arg marshalling ensures type errors surface before DynamicInvoke.
        HostMethodInvoker baked = (target, stashArgs) =>
        {
            // Marshal each Stash arg to the expected CLR type.
            object?[] clrArgs = new object?[arity + 1];
            clrArgs[0] = target; // typed instance as first arg

            for (int i = 0; i < arity; i++)
            {
                StashValue sv = stashArgs[i];
                try
                {
                    clrArgs[i + 1] = HostMarshaller.FromStashArg(sv, argTypes[i]);
                }
                catch (Exception ex) when (ex is not RuntimeError)
                {
                    throw new Stash.Runtime.Errors.HostError(
                        $"arg {i + 1} to {typeName}.{memberName}: {ex.Message}");
                }
            }

            // Return the marshalled args and the original handler.
            // InvokeHostDelegate.InvokeMethod performs the actual DynamicInvoke(clrArgs)
            // inside its try/catch — that is the structural chokepoint for CLR-exception
            // → HostError mapping.
            return (clrArgs, handler);
        };

        _members[name] = new HostMemberDescriptor(
            Kind:        HostMemberKind.Method,
            Getter:      null,
            Setter:      null,
            Invoke:      baked,
            MethodArity: arity);

        return this;
    }

    /// <summary>
    /// Registers an asynchronous method. When Stash code calls
    /// <c>await obj.<paramref name="name"/>(...)</c>, the call is dispatched to
    /// <paramref name="handler"/> and the resulting <see cref="Task"/> is wrapped as a
    /// <see cref="Stash.Runtime.Types.StashFuture"/> for Stash's <c>await</c> operator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The delegate signature must have the CLR host type <typeparamref name="T"/> as its
    /// first parameter (the target), followed by zero or more typed parameters that map to
    /// the Stash arguments. An optional trailing <see cref="CancellationToken"/> parameter
    /// is detected ONCE at registration time (this reflection runs at host setup, never in
    /// the VM hot path — NAOT-clean per the brief's Q1 note) and, when present, receives
    /// the engine's call cancellation token at invocation time.
    /// </para>
    /// <para>
    /// The delegate must return <c>Task</c> or <c>Task&lt;TResult&gt;</c>. A faulted task
    /// surfaces as <see cref="HostError"/> (Kind = <c>StashError.KindHostError</c>) preserving
    /// the inner exception message. Cancellation propagates via the trailing
    /// <see cref="CancellationToken"/> (when present) and surfaces from the awaiting
    /// <c>CallAsync&lt;T&gt;</c> call.
    /// </para>
    /// </remarks>
    /// <param name="name">The method name visible in Stash.</param>
    /// <param name="handler">
    /// Async delegate whose first parameter is the CLR host instance of type
    /// <typeparamref name="T"/>; subsequent parameters are marshalled from the Stash
    /// argument list; an optional trailing <see cref="CancellationToken"/> receives the
    /// engine's cancellation token.
    /// </param>
    /// <returns>This builder (fluent API).</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="name"/> or <paramref name="handler"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="handler"/>'s first parameter is not assignable from
    /// <typeparamref name="T"/>, or when the return type is not <c>Task</c> /
    /// <c>Task&lt;TResult&gt;</c>.
    /// </exception>
    public HostTypeBuilder<T> AsyncMethod(string name, Delegate handler)
    {
        if (name is null)    throw new ArgumentNullException(nameof(name));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        MethodInfo mi = handler.Method;
        ParameterInfo[] parameters = mi.GetParameters();

        // Validate: first param must be T (or a supertype).
        if (parameters.Length == 0 || !parameters[0].ParameterType.IsAssignableFrom(typeof(T)))
        {
            throw new ArgumentException(
                $"AsyncMethod delegate's first parameter must be assignable from '{typeof(T).Name}'. " +
                $"Expected first parameter type '{typeof(T).Name}' (or a base class), " +
                $"but got '{(parameters.Length == 0 ? "<none>" : parameters[0].ParameterType.Name)}'.",
                nameof(handler));
        }

        // Validate return type: Task or Task<T>.
        Type returnType = mi.ReturnType;
        if (returnType != typeof(Task) &&
            !(returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>)))
        {
            throw new ArgumentException(
                $"AsyncMethod delegate must return Task or Task<TResult>, " +
                $"but '{name}' returns '{returnType.Name}'.",
                nameof(handler));
        }

        // Detect trailing CancellationToken (once, at registration — NAOT-clean).
        bool trailingCt = parameters.Length > 1
            && parameters[parameters.Length - 1].ParameterType == typeof(CancellationToken);

        // Arity: exclude the target (first param) and optional trailing CT.
        int arity = parameters.Length - 1 - (trailingCt ? 1 : 0);

        // Snapshot parameter types for Stash-visible args (positions 1..arity).
        Type[] argTypes = new Type[arity];
        for (int i = 0; i < arity; i++)
            argTypes[i] = parameters[i + 1].ParameterType;

        // Capture for closures.
        string typeName   = typeof(T).Name;
        string memberName = name;
        bool   hasTask_T  = returnType.IsGenericType; // Task<TResult> vs plain Task

        // Bake the async invoker closure.
        // The baked invoker returns the RAW CLR result (not yet StashValue-marshalled);
        // InvokeHostDelegate.InvokeAsyncMethod handles marshalling so it has access to
        // the full registrations map and observed-targets table at call time.
        HostAsyncMethodInvoker baked = async (target, stashArgs, ct) =>
        {
            // Marshal Stash args to CLR types.
            object?[] clrArgs = new object?[arity + 1 + (trailingCt ? 1 : 0)];
            clrArgs[0] = target;

            for (int i = 0; i < arity; i++)
            {
                StashValue sv = stashArgs[i];
                try
                {
                    clrArgs[i + 1] = HostMarshaller.FromStashArg(sv, argTypes[i]);
                }
                catch (Exception ex) when (ex is not RuntimeError)
                {
                    throw new HostError(
                        $"arg {i + 1} to {typeName}.{memberName}: {ex.Message}");
                }
            }

            if (trailingCt)
                clrArgs[clrArgs.Length - 1] = ct;

            // Start the delegate task. Sync throws from DynamicInvoke are caught as HostError.
            Task taskResult;
            try
            {
                object? invoked = handler.DynamicInvoke(clrArgs);
                taskResult = (Task)invoked!;
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
            {
                Exception inner = tie.InnerException;
                if (inner is RuntimeError re) throw re;
                throw new HostError(inner.Message);
            }
            catch (Exception ex) when (ex is not RuntimeError)
            {
                throw new HostError(ex.Message);
            }

            // Await the task. Map faults to HostError INSIDE the bridge so
            // StashFuture.GetResult()'s catch(RuntimeError){throw;} surfaces it intact.
            try
            {
                await taskResult.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Let OCE propagate — GetResult() converts it to RuntimeError("Future was cancelled.")
                // which StashHost surfaces as KindCancelled, not KindHostError.
                throw;
            }
            catch (Exception ex) when (ex is not RuntimeError)
            {
                throw new HostError(ex.Message);
            }

            // Return the raw CLR result so InvokeAsyncMethod can marshal it with the
            // full registrations map (wrapping registered types as HostHandle).
            if (hasTask_T)
            {
                // Use reflection to read .Result on Task<TResult>.
                // This reflection runs at call time but is NOT in the VM dispatch loop
                // hot path — acceptable for the Hosting SDK layer.
                return ((dynamic)taskResult).Result as object;
            }

            // Plain Task (void-returning async method) → null result.
            return null;
        };

        _members[name] = new HostMemberDescriptor(
            Kind:        HostMemberKind.AsyncMethod,
            Getter:      null,
            Setter:      null,
            Invoke:      null,
            MethodArity: arity,
            AsyncInvoke: baked);

        return this;
    }

    /// <summary>
    /// Registers a cleanup hook that is invoked exactly once per unique observed target
    /// when the <see cref="StashHost"/> is disposed via <see cref="IAsyncDisposable.DisposeAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The callback fires for every CLR instance of <typeparamref name="T"/> that the
    /// engine has <em>observed</em> — i.e. that was wrapped in a <see cref="Stash.Hosting.Internal.HostHandle"/>
    /// and placed into VM state (via <see cref="IStashHost.SetGlobal"/> or returned from a
    /// host method / property). Instances the host never passed into the engine are never
    /// released here — the host owns them.
    /// </para>
    /// <para>
    /// The callback is invoked at most once per unique target identity (reference equality),
    /// even if the same instance was observed multiple times.
    /// </para>
    /// </remarks>
    /// <param name="release">Callback that performs per-instance cleanup.</param>
    /// <returns>This builder (fluent API).</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="release"/> is <c>null</c>.
    /// </exception>
    public HostTypeBuilder<T> OnRelease(Action<T> release)
    {
        _onRelease = release ?? throw new ArgumentNullException(nameof(release));
        return this;
    }

    // ── Internal build ────────────────────────────────────────────────────

    /// <summary>
    /// Materialise a <see cref="HostTypeRegistration"/> from the builder's current state.
    /// Called by <see cref="StashHost"/> after the user's configure delegate has run.
    /// </summary>
    internal HostTypeRegistration Build()
    {
        Action<object>? releaseWrapper = _onRelease is null
            ? null
            : obj => _onRelease((T)obj);

        return new HostTypeRegistration(
            vmTypeName: _vmTypeName,
            clrType:    typeof(T),
            members:    _members,
            onRelease:  releaseWrapper);
    }
}
