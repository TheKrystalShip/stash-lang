using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Stash.Runtime;

/// <summary>
/// Single source of truth for all runtime equality decisions in the Stash VM.
///
/// <para>
/// Three public equality modes are exposed — one per use site — over a shared private
/// <see cref="NumericCoercingEquals"/> core that handles the int/float/byte coercion and
/// ±0 unification. The two callers differ in exactly the NaN-self-equality rule:
/// </para>
/// <list type="bullet">
///   <item>
///     <b><see cref="OperatorEquals"/></b> — the <c>==</c>/<c>!=</c> operator (DE1).
///     NaN is not self-equal (IEEE 754). Passes <c>nanSelfEqual: false</c>.
///   </item>
///   <item>
///     <b><see cref="SameValueZero"/></b> — collection membership and dict keys (DE2).
///     NaN is self-equal by value. Passes <c>nanSelfEqual: true</c>.
///   </item>
///   <item>
///     <b><see cref="StrictEquals"/></b> — the <c>assert.equal</c>/<c>assert.notEqual</c>
///     mode (DE3). Tag-strict: operands of different <c>typeof</c> are never equal even
///     within the numeric class. Within a tag, uses value equality via <see cref="object.Equals"/>
///     (which is <see cref="double.Equals"/> for float: ±0 unified, NaN reflexive — the
///     probed today behavior that is now spec'd normatively).
///   </item>
/// </list>
///
/// <para>
/// The non-numeric branch of <see cref="OperatorEquals"/> and <see cref="SameValueZero"/>
/// delegates to <see cref="StashValue.Equals"/> (preserving secret reference identity per
/// §Values D4; aggregate reference identity; enum/struct/function identity).
/// </para>
///
/// <para>
/// The constant-pool interning key is <b>not</b> a fourth equality mode. It is
/// <see cref="StashValue"/>'s own <see cref="IEquatable{T}"/> — bit-level on the Float tag
/// — consumed solely by <c>ChunkBuilder._constantMap</c>. See the comment in
/// <c>StashValue.cs</c> and the Decision Log in <c>brief.md</c>.
/// </para>
///
/// <para>
/// This module lives in <c>Stash.Core</c> so every consumer —
/// <c>Stash.Bytecode</c>, <c>Stash.Stdlib</c>, <c>Stash.Hosting</c>, the LSP/DAP runtimes —
/// shares one equality semantics by construction, without inverted dependencies.
/// </para>
/// </summary>
public static class StashEquality
{
    // ── Mode 1: OperatorEquals (== / != operator, DE1) ───────────────────────

    /// <summary>
    /// Equality for the <c>==</c> and <c>!=</c> operators (DE1 — unchanged from §Values D2).
    /// <list type="bullet">
    ///   <item>int/float/byte compare by mathematical value (numeric-coercion rule).</item>
    ///   <item><c>+0.0 == -0.0</c> is <c>true</c> (IEEE 754).</item>
    ///   <item><c>NaN != NaN</c> (IEEE 754 — not self-equal).</item>
    ///   <item>Non-numeric cross-category pairs are unequal.</item>
    ///   <item>Within <see cref="StashValueTag.Obj"/>, delegates to
    ///     <see cref="StashValue.Equals"/> (reference identity for aggregates/secrets;
    ///     user-defined <c>IVMEquatable.VMEquals</c> is called by the VM dispatch layer
    ///     <em>before</em> reaching this method, so user struct/enum <c>==</c> is
    ///     unaffected).</item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool OperatorEquals(StashValue a, StashValue b)
    {
        // Fast same-tag paths (inlined for hot == operator path)
        if (a.Tag != b.Tag) return NumericCoercingEquals(a, b, nanSelfEqual: false);
        if (a.Tag == StashValueTag.Int)  return a.AsInt == b.AsInt;
        if (a.Tag == StashValueTag.Bool) return a.AsBool == b.AsBool;
        return OperatorEqualsSlow(a, b);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool OperatorEqualsSlow(StashValue a, StashValue b) => a.Tag switch
    {
        StashValueTag.Null  => true,
        StashValueTag.Byte  => a.AsInt == b.AsInt,
        StashValueTag.Float => a.AsFloat == b.AsFloat, // IEEE 754: NaN!=NaN, ±0 unified
        StashValueTag.Obj   => a.Equals(b),            // StashValue.Equals → reference identity
        _                   => false,
    };

    // ── Mode 2: SameValueZero (collections — DE2) ─────────────────────────────

    /// <summary>
    /// An <see cref="IEqualityComparer{T}"/> that implements SameValueZero semantics
    /// for use in collection containers (<c>Dictionary</c>, <c>HashSet</c>, etc.).
    ///
    /// <para>
    /// Same numeric-coercion rule as <see cref="OperatorEquals"/> with one delta:
    /// NaN is self-equal by value (a key put in is a key found back). Consumers:
    /// <c>in</c> operator, <c>arr.contains</c>, <c>arr.indexOf</c>, <c>arr.remove</c>,
    /// related array-search built-ins, and dict key storage.
    /// </para>
    /// </summary>
    public static IEqualityComparer<StashValue> SameValueZero { get; } = new SameValueZeroComparer();

    private sealed class SameValueZeroComparer : IEqualityComparer<StashValue>
    {
        public bool Equals(StashValue a, StashValue b) => SameValueZeroEquals(a, b);

        public int GetHashCode(StashValue sv) => SameValueZeroHashCode(sv);
    }

    /// <summary>
    /// SameValueZero equality — used by collection membership and dict keys.
    /// Same coercion as <see cref="OperatorEquals"/>, but NaN is self-equal by value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool SameValueZeroEquals(StashValue a, StashValue b)
    {
        if (a.Tag != b.Tag) return NumericCoercingEquals(a, b, nanSelfEqual: true);
        if (a.Tag == StashValueTag.Int)  return a.AsInt == b.AsInt;
        if (a.Tag == StashValueTag.Bool) return a.AsBool == b.AsBool;
        return SameValueZeroSlow(a, b);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool SameValueZeroSlow(StashValue a, StashValue b) => a.Tag switch
    {
        StashValueTag.Null  => true,
        StashValueTag.Byte  => a.AsInt == b.AsInt,
        // NaN self-equal: use bit-level comparison (same bits → same NaN payload).
        // ±0 unified: both map to bit pattern 0, which is identical → unified.
        // Wait — float bit-level makes NaN self-equal but makes ±0 DISTINCT.
        // We want NaN self-equal AND ±0 unified. Use: double.Equals for ±0 unification
        // combined with a NaN-self-equality override.
        StashValueTag.Float => SameValueZeroFloat(a.AsFloat, b.AsFloat),
        StashValueTag.Obj   => a.Equals(b), // StashValue.Equals → reference identity
        _                   => false,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SameValueZeroFloat(double x, double y)
    {
        // SameValueZero for floats:
        // - NaN is self-equal: if both are NaN, return true.
        // - ±0 are unified: +0.0 == -0.0 → true (same as double ==).
        // - All other comparisons: same as double ==.
        if (double.IsNaN(x) && double.IsNaN(y)) return true;
        return x == y;
    }

    /// <summary>
    /// Hash code consistent with <see cref="SameValueZeroEquals"/>:
    /// equal values (under SameValueZero) must produce the same hash code.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SameValueZeroHashCode(StashValue sv)
    {
        return sv.Tag switch
        {
            StashValueTag.Null  => 0,
            StashValueTag.Bool  => HashCode.Combine(StashValueTag.Bool, sv.AsBool),
            // Numeric equivalence class: all numerics that are equal under SameValueZero
            // must hash the same. Promote to double and use the double hash.
            // Special cases:
            // - NaN: all NaN values hash to the same sentinel (canonical NaN bits).
            // - ±0: both map to 0.0 hash (0.0 == -0.0 → same hash).
            StashValueTag.Int   => HashCode.Combine(StashValueTag.Float, NumericHashDouble((double)sv.AsInt)),
            StashValueTag.Byte  => HashCode.Combine(StashValueTag.Float, NumericHashDouble((double)sv.AsByte)),
            StashValueTag.Float => HashCode.Combine(StashValueTag.Float, NumericHashDouble(sv.AsFloat)),
            StashValueTag.Obj   => sv.GetHashCode(), // StashValue.GetHashCode() → object.GetHashCode()
            _                   => 0,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double NumericHashDouble(double d)
    {
        // Normalize NaN to a canonical NaN so all NaN payloads hash the same.
        if (double.IsNaN(d)) return double.NaN;
        // ±0 → +0 so they hash the same.
        if (d == 0.0) return 0.0;
        return d;
    }

    // ── Mode 3: StrictEquals (assert.equal / assert.notEqual — DE3) ──────────

    /// <summary>
    /// Tag-strict equality for <c>assert.equal</c> and <c>assert.notEqual</c> (DE3).
    /// <list type="bullet">
    ///   <item>Operands of different <c>typeof</c> are not equal even within the numeric class
    ///     — <c>assert.equal(1, 1.0)</c> raises because <c>int != float</c>.</item>
    ///   <item>Within a tag, uses <see cref="object.Equals"/> (which is
    ///     <see cref="double.Equals"/> for float): <c>±0</c> are unified and <c>NaN</c>
    ///     is reflexive — the probed behavior now spec'd normatively under DE3.</item>
    ///   <item>Aggregates/secrets compare by reference identity via
    ///     <see cref="StashValue.Equals"/>.</item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool StrictEquals(StashValue a, StashValue b)
    {
        // Tag-strict: different tags → never equal (this is the key DE3 constraint
        // that makes assert.equal(1, 1.0) throw even though 1 == 1.0 is true).
        if (a.Tag != b.Tag) return false;
        return StrictEqualsSameTag(a, b);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool StrictEqualsSameTag(StashValue a, StashValue b) => a.Tag switch
    {
        StashValueTag.Null  => true,
        StashValueTag.Bool  => a.AsBool == b.AsBool,
        StashValueTag.Int   => a.AsInt == b.AsInt,
        StashValueTag.Byte  => a.AsInt == b.AsInt,
        // Within Float tag, use Double.Equals: ±0 unified, NaN self-equal
        // (the probed assert behavior: assert.equal(0.0,-0.0) PASSES; assert.equal(NaN,NaN) PASSES)
        StashValueTag.Float => double.Equals(a.AsFloat, b.AsFloat),
        StashValueTag.Obj   => a.Equals(b), // StashValue.Equals → object.Equals → reference identity
        _                   => false,
    };

    // ── Shared private core ───────────────────────────────────────────────────

    /// <summary>
    /// Numeric cross-tag coercion core, called only when <paramref name="a"/>.Tag ≠
    /// <paramref name="b"/>.Tag. Handles int/float/byte equivalence class.
    ///
    /// <para>
    /// The two caller modes differ in exactly one rule:
    /// <paramref name="nanSelfEqual"/> = <c>false</c> for <see cref="OperatorEquals"/>
    /// (IEEE 754); = <c>true</c> for <see cref="SameValueZeroEquals"/> (collection membership).
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool NumericCoercingEquals(StashValue a, StashValue b, bool nanSelfEqual)
    {
        // Promote byte → int so ToDouble never sees a Byte tag.
        if (a.IsByte) a = StashValue.FromInt(a.AsByte);
        if (b.IsByte) b = StashValue.FromInt(b.AsByte);

        // Both must be numeric (Int or Float) after byte promotion.
        if (!a.IsNumeric || !b.IsNumeric) return false;

        double da = ToDouble(a);
        double db = ToDouble(b);

        if (nanSelfEqual && double.IsNaN(da) && double.IsNaN(db)) return true;

        // Standard double ==: ±0 unified, NaN not self-equal (IEEE 754)
        return da == db;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ToDouble(StashValue v) => v.Tag switch
    {
        StashValueTag.Int   => (double)v.AsInt,
        StashValueTag.Float => v.AsFloat,
        _                   => throw new System.InvalidOperationException("Not a number"),
    };
}
