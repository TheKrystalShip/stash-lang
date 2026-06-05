namespace Stash.Hosting;

using System;
using System.Collections.Generic;
using Stash.Hosting.Internal;
using Stash.Hosting.Marshalling;
using Stash.Runtime;

/// <summary>
/// Typed builder for declaring a CLR type's Stash-visible members.
/// Obtained via <see cref="IStashHost.RegisterType{T}"/>.
/// </summary>
/// <typeparam name="T">The CLR class being registered.</typeparam>
/// <remarks>
/// P1 supports only <see cref="Named"/> — the builder records the VM type name.
/// P2 adds <c>Property</c> overloads (read-only and read-write).
/// <c>Method</c>, <c>AsyncMethod</c>, and <c>OnRelease</c> are added in P3/P4.
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
