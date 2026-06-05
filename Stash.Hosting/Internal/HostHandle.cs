namespace Stash.Hosting.Internal;

using System;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;

/// <summary>
/// The opaque, by-reference wrapper that flows through the VM as a
/// <see cref="StashValue"/> carrying a live CLR host object.
/// Implements existing <c>Stash.Core</c> VM protocols so member dispatch
/// (P2/P3/P4) rides the VM's existing fall-through with zero bytecode changes.
/// </summary>
/// <remarks>
/// P1 status:
/// — <see cref="IVMTyped.VMTypeName"/> returns the registered name (enables typeof / is).
/// — <see cref="IVMStringifiable.VMToString"/> returns a readable placeholder.
/// — <see cref="IVMFieldAccessible.VMTryGetField"/> returns <c>false</c> — no members yet.
/// — <see cref="IVMFieldMutable.VMSetField"/> throws <c>RuntimeError</c> — no members yet.
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
    /// P1: returns <c>false</c> for all member names (no dispatch table yet).
    /// The VM falls through to the existing "cannot access field" error path.
    /// P2 populates the member table and makes this method consult it.
    /// </summary>
    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span)
    {
        value = StashValue.Null;
        return false;
    }

    // ── IVMFieldMutable ───────────────────────────────────────────────────

    /// <summary>
    /// P1: no mutable members yet. The VM already routes through
    /// <see cref="IVMFieldMutable"/> before throwing its generic error, so we must
    /// implement this but can throw immediately.
    /// P2 populates the member table and makes this method consult it.
    /// </summary>
    public void VMSetField(string name, StashValue value, SourceSpan? span)
    {
        throw new RuntimeError(
            $"Cannot set field '{name}' on <{_registration.VmTypeName}>: no members registered in P1.",
            span);
    }
}
