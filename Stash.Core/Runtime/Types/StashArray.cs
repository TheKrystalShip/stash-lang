namespace Stash.Runtime.Types;

using System.Collections.Generic;

/// <summary>
/// The runtime carrier for Stash plain arrays — an always-present wrapper over
/// <see cref="List{T}">List&lt;StashValue&gt;</see> that adds an in-place
/// <see cref="IsFrozen"/> bit.
///
/// <para>
/// <b>Identity invariant.</b> Using a subclass (rather than a separate wrapper
/// holding an inner list) means every <c>is List&lt;StashValue&gt;</c> check in
/// the VM, stdlib, and Stringify paths continues to match without modification —
/// so the only write-path change is a single additional frozen guard.
/// </para>
///
/// <para>
/// <b>Design.</b> Previously plain Stash arrays were bare <c>List&lt;StashValue&gt;</c>
/// objects pushed onto the stack by <c>NewArray</c>.  P3 of the <c>readonly-modifier</c>
/// feature replaces every construction site with <c>new StashArray(capacity)</c> /
/// <c>new StashArray(items)</c>.  All frozen arrays are uniformly
/// <c>StashArray { IsFrozen = true }</c> — there is no separate frozen-array wrapper type.
/// </para>
/// </summary>
public sealed class StashArray : List<StashValue>
{
    /// <summary>Creates an empty array with the given initial capacity hint.</summary>
    public StashArray(int capacity = 0) : base(capacity) { }

    /// <summary>Creates an array pre-populated from <paramref name="source"/>.</summary>
    public StashArray(System.Collections.Generic.IEnumerable<StashValue> source) : base(source) { }

    /// <summary>Whether this array is frozen (all write operations throw <see cref="Errors.ReadOnlyError"/>).</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Freezes the array in place. Subsequent write operations throw
    /// <see cref="Errors.ReadOnlyError"/>. Idempotent.
    /// </summary>
    public void Freeze() => IsFrozen = true;
}
