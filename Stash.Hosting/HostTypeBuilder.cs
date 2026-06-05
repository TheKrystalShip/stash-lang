namespace Stash.Hosting;

using System;
using System.Collections.Generic;
using System.Reflection;
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
/// <c>AsyncMethod</c> and <c>OnRelease</c> are added in P4.
/// </remarks>
public sealed class HostTypeBuilder<T> where T : class
{
    private string _vmTypeName = typeof(T).Name;
    private readonly Dictionary<string, HostMemberDescriptor> _members = new(StringComparer.Ordinal);
#pragma warning disable CS0649 // Field assigned in P4 (OnRelease); suppress "never assigned" warning
    private Action<T>? _onRelease;
#pragma warning restore CS0649

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

        // Bake the invoker closure.
        // Per-arg marshalling (StashValue → expected CLR type) produces "arg N to T.m: ..."
        // messages when conversion fails, satisfying the P3 done_when requirement.
        //
        // DynamicInvoke is used here because the delegate's parameter types are only known
        // at registration time. This is acceptable in Stash.Hosting (an embedding host SDK
        // for managed-JIT consumers — not the VM dispatch loop or a NAOT-published binary
        // path). The per-arg marshalling above ensures type errors surface before DynamicInvoke.
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

            // Invoke the typed delegate with the marshalled args.
            // Return the raw CLR result — InvokeHostDelegate.InvokeMethod handles
            // return-value marshalling using the full host registrations map, which
            // correctly wraps registered CLR instances as HostHandle values.
            // Any CLR exception from the body is caught by InvokeHostDelegate.InvokeMethod.
            return handler.DynamicInvoke(clrArgs);
        };

        _members[name] = new HostMemberDescriptor(
            Kind:        HostMemberKind.Method,
            Getter:      null,
            Setter:      null,
            Invoke:      baked,
            MethodArity: arity);

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
