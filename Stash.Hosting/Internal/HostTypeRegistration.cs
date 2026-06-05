namespace Stash.Hosting.Internal;

using System;
using System.Collections.Generic;

/// <summary>
/// Immutable snapshot of everything the host has declared for one CLR type.
/// Built by <see cref="Stash.Hosting.HostTypeBuilder{T}"/> and stored in the
/// per-host registration map keyed by <see cref="ClrType"/>.
/// </summary>
internal sealed class HostTypeRegistration
{
    /// <summary>VM type name reported by <c>typeof(obj)</c> and used by <c>obj is Name</c>.</summary>
    public string VmTypeName { get; }

    /// <summary>The CLR <see cref="Type"/> this registration covers.</summary>
    public Type ClrType { get; }

    /// <summary>Per-member dispatch table. Empty until P2/P3 populate it.</summary>
    public IReadOnlyDictionary<string, HostMemberDescriptor> Members { get; }

    /// <summary>
    /// Optional cleanup callback fired at engine dispose (once per unique target).
    /// Null until P4 populates it.
    /// </summary>
    public Action<object>? OnRelease { get; }

    internal HostTypeRegistration(
        string vmTypeName,
        Type clrType,
        IReadOnlyDictionary<string, HostMemberDescriptor> members,
        Action<object>? onRelease)
    {
        VmTypeName = vmTypeName;
        ClrType    = clrType;
        Members    = members;
        OnRelease  = onRelease;
    }
}
