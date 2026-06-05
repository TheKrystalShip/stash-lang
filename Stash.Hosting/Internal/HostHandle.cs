namespace Stash.Hosting.Internal;

using System;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Protocols;

/// <summary>
/// The opaque, by-reference wrapper that flows through the VM as a
/// <see cref="StashValue"/> carrying a live CLR host object.
/// Implements existing <c>Stash.Core</c> VM protocols so member dispatch
/// (P2/P3/P4) rides the VM's existing fall-through with zero bytecode changes.
/// </summary>
/// <remarks>
/// P2 status:
/// — <see cref="IVMTyped.VMTypeName"/> returns the registered name (enables typeof / is).
/// — <see cref="IVMStringifiable.VMToString"/> returns a readable placeholder.
/// — <see cref="IVMFieldAccessible.VMTryGetField"/> dispatches registered property
///   getters via <see cref="InvokeHostDelegate.InvokeGetter"/>; returns <c>false</c>
///   for unknown member names so the VM falls through to its generic error path.
/// — <see cref="IVMFieldMutable.VMSetField"/> dispatches registered property setters
///   via <see cref="InvokeHostDelegate.InvokeSetter"/>; raises <see cref="ReadOnlyError"/>
///   for read-only properties; raises <see cref="RuntimeError"/> for unknown names.
/// </remarks>
internal sealed class HostHandle : IVMFieldAccessible, IVMFieldMutable, IVMTyped, IVMStringifiable
{
    private readonly object _target;
    private readonly HostTypeRegistration _registration;

    internal HostHandle(object target, HostTypeRegistration registration)
    {
        _target       = target ?? throw new ArgumentNullException(nameof(target));
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    /// <summary>The live CLR instance carried by this handle.</summary>
    internal object Target => _target;

    /// <summary>The CLR <see cref="Type"/> of the wrapped instance.</summary>
    internal Type ClrType => _registration.ClrType;

    /// <summary>The registration that describes this type's dispatch table.</summary>
    internal HostTypeRegistration Registration => _registration;

    // ── IVMTyped ──────────────────────────────────────────────────────────

    /// <summary>Returns the registered VM type name for <c>typeof(obj)</c>.</summary>
    public string VMTypeName => _registration.VmTypeName;

    // ── IVMStringifiable ──────────────────────────────────────────────────

    /// <summary>Returns a readable placeholder for string interpolation, println, etc.</summary>
    public string VMToString() => $"<{_registration.VmTypeName}>";

    // ── IVMFieldAccessible ────────────────────────────────────────────────

    /// <summary>
    /// Dispatches property getter access for registered members.
    /// Returns <c>true</c> and writes the marshalled value into <paramref name="value"/>
    /// when <paramref name="name"/> matches a registered property descriptor.
    /// Returns <c>false</c> for unknown member names so the VM falls through to its
    /// existing "cannot access field" error path.
    /// </summary>
    /// <remarks>
    /// All delegate invocation routes through <see cref="InvokeHostDelegate.InvokeGetter"/>
    /// — the single chokepoint that maps CLR exceptions to <see cref="HostError"/>.
    /// </remarks>
    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span)
    {
        if (_registration.Members.TryGetValue(name, out HostMemberDescriptor? desc)
            && desc.Kind == HostMemberKind.Property
            && desc.Getter is not null)
        {
            // Route through the chokepoint; any CLR exception → HostError.
            value = InvokeHostDelegate.InvokeGetter(desc.Getter, _target, span);
            return true;
        }

        // Unknown member: let the VM handle the fallthrough.
        value = StashValue.Null;
        return false;
    }

    // ── IVMFieldMutable ───────────────────────────────────────────────────

    /// <summary>
    /// Dispatches property setter access for registered members.
    /// Raises <see cref="ReadOnlyError"/> when the property has no setter (read-only).
    /// Raises <see cref="RuntimeError"/> for unknown member names.
    /// </summary>
    /// <remarks>
    /// All delegate invocation routes through <see cref="InvokeHostDelegate.InvokeSetter"/>
    /// — the single chokepoint that maps CLR exceptions to <see cref="HostError"/>.
    /// </remarks>
    public void VMSetField(string name, StashValue value, SourceSpan? span)
    {
        if (_registration.Members.TryGetValue(name, out HostMemberDescriptor? desc)
            && desc.Kind == HostMemberKind.Property)
        {
            if (desc.Setter is null)
            {
                // Read-only property — raise the canonical Stash error for assignment to readonly.
                throw new ReadOnlyError(
                    $"Property '{name}' on <{_registration.VmTypeName}> is read-only.",
                    span);
            }

            // Route through the chokepoint; any CLR exception → HostError.
            InvokeHostDelegate.InvokeSetter(desc.Setter, _target, value, span);
            return;
        }

        // Unknown member: generic runtime error (same path as before P2).
        throw new RuntimeError(
            $"Cannot set field '{name}' on <{_registration.VmTypeName}>: " +
            $"no member '{name}' registered for this type.",
            span);
    }
}
