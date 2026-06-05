namespace Stash.Hosting;

using System;
using System.Collections.Generic;
using Stash.Hosting.Internal;

/// <summary>
/// Typed builder for declaring a CLR type's Stash-visible members.
/// Obtained via <see cref="IStashHost.RegisterType{T}"/>.
/// </summary>
/// <typeparam name="T">The CLR class being registered.</typeparam>
/// <remarks>
/// P1 supports only <see cref="Named"/> — the builder records the VM type name.
/// <c>Property</c>, <c>Method</c>, <c>AsyncMethod</c>, and <c>OnRelease</c> are
/// added in P2/P3/P4.
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
