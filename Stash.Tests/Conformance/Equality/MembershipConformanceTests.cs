using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Equality;

/// <summary>
/// Conformance tests for §Equality — Array membership uses SameValueZero (Edit E1, membership half, DE2).
///
/// <para>
/// P2 migrated <c>in</c> (array branch), <c>arr.contains</c>, <c>arr.indexOf</c>,
/// <c>arr.remove</c>, <c>arr.includes</c>, <c>arr.lastIndexOf</c>, and <c>arr.unique</c>
/// from tag-strict equality to <see cref="Stash.Runtime.StashEquality.SameValueZero"/>:
/// int/float/byte compare by mathematical value (the numeric-coercion rule), <c>+0.0</c>
/// and <c>-0.0</c> are unified, and <c>NaN</c> is self-equal by value.
/// </para>
///
/// <para>
/// Decision locks tested here:
/// <list type="bullet">
///   <item><b>DE2</b> — collections use SameValueZero: <c>1 in [1.0]</c> is <c>true</c>.</item>
///   <item><b>DE4</b> — secret reference identity preserved: two distinct <c>secret("x")</c>
///     constructions are <em>not</em> <c>in</c> each other; an aliased handle is <c>in</c>
///     its own array. This proves the SameValueZero non-numeric branch delegates correctly.</item>
/// </list>
/// </para>
///
/// <para>
/// Note: dict-key SameValueZero migration is deferred to P3. The <c>in</c> operator on
/// a dictionary right-hand side and <c>dict.has</c> still use the old tag-strict behavior
/// until P3 ships.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class MembershipConformanceTests : StashTestBase
{
    // ── Numeric coercion: int ↔ float (DE2) ────────────────────────────────────

    /// <summary>
    /// <c>1 in [1.0]</c> is <c>true</c> — integer found in float array via SameValueZero.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void InOperator_IntInFloatArray_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = 1 in [1.0];");
        Assert.True(result,
            "§Equality SameValueZero (DE2): 1 in [1.0] must be true — array membership coerces int/float by mathematical value.");
    }

    /// <summary>
    /// <c>1.0 in [1]</c> is <c>true</c> — float found in integer array via SameValueZero.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void InOperator_FloatInIntArray_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = 1.0 in [1];");
        Assert.True(result,
            "§Equality SameValueZero (DE2): 1.0 in [1] must be true — SameValueZero coerces float/int by mathematical value.");
    }

    /// <summary>
    /// <c>arr.contains([1], 1.0)</c> is <c>true</c> — SameValueZero in arr.contains.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void ArrContains_IntArrayContainsFloat_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = arr.contains([1], 1.0);");
        Assert.True(result,
            "§Equality SameValueZero (DE2): arr.contains([1], 1.0) must be true.");
    }

    /// <summary>
    /// <c>arr.contains([1.0], 1)</c> is <c>true</c> — SameValueZero in arr.contains, reversed.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void ArrContains_FloatArrayContainsInt_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = arr.contains([1.0], 1);");
        Assert.True(result,
            "§Equality SameValueZero (DE2): arr.contains([1.0], 1) must be true.");
    }

    // ── Numeric coercion: byte ↔ int ↔ float (DE2) ─────────────────────────────

    /// <summary>
    /// <c>conv.toByte(7) in [7]</c> is <c>true</c> — byte found in int array via SameValueZero.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void InOperator_ByteInIntArray_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = conv.toByte(7) in [7];");
        Assert.True(result,
            "§Equality SameValueZero (DE2): conv.toByte(7) in [7] must be true — byte/int coerced by mathematical value.");
    }

    /// <summary>
    /// <c>7 in [conv.toByte(7)]</c> is <c>true</c> — int found in byte array via SameValueZero.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void InOperator_IntInByteArray_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = 7 in [conv.toByte(7)];");
        Assert.True(result,
            "§Equality SameValueZero (DE2): 7 in [conv.toByte(7)] must be true.");
    }

    /// <summary>
    /// <c>conv.toByte(7) in [7.0]</c> is <c>true</c> — byte found in float array via SameValueZero.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void InOperator_ByteInFloatArray_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = conv.toByte(7) in [7.0];");
        Assert.True(result,
            "§Equality SameValueZero (DE2): conv.toByte(7) in [7.0] must be true — byte/float coerced by mathematical value.");
    }

    /// <summary>
    /// <c>arr.contains</c> with a byte value in an int array.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void ArrContains_ByteInIntArray_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = arr.contains([7], conv.toByte(7));");
        Assert.True(result,
            "§Equality SameValueZero (DE2): arr.contains([7], conv.toByte(7)) must be true.");
    }

    // ── ±0 membership (DE2) ────────────────────────────────────────────────────

    /// <summary>
    /// <c>0.0 in [-0.0]</c> is <c>true</c> — ±0 are unified by SameValueZero.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void InOperator_PosZeroInNegZeroArray_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = 0.0 in [-0.0];");
        Assert.True(result,
            "§Equality SameValueZero (DE2): 0.0 in [-0.0] must be true — ±0 are unified.");
    }

    /// <summary>
    /// <c>arr.contains([0], -0.0)</c> is <c>true</c> — int 0 and float -0.0 are unified by SameValueZero.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void ArrContains_IntZeroContainsNegZeroFloat_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = arr.contains([0], -0.0);");
        Assert.True(result,
            "§Equality SameValueZero (DE2): arr.contains([0], -0.0) must be true — ±0 unified, int/float coerced.");
    }

    /// <summary>
    /// <c>arr.contains([0.0], 0)</c> is <c>true</c> — float 0.0 and int 0 are equal by SameValueZero.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void ArrContains_FloatZeroContainsIntZero_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = arr.contains([0.0], 0);");
        Assert.True(result,
            "§Equality SameValueZero (DE2): arr.contains([0.0], 0) must be true — ±0 unified, float/int coerced.");
    }

    // ── NaN self-equal by value (DE2) ──────────────────────────────────────────

    /// <summary>
    /// <c>NaN in [NaN]</c> is <c>true</c> — NaN is self-equal by value under SameValueZero.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void InOperator_NaN_IsSelfEqual_PerSpecEqualityMembership()
    {
        var result = (bool?)Run(
            "let big = conv.toFloat(\"1e308\") * 10.0; let n = big - big; let result = n in [n];");
        Assert.True(result,
            "§Equality SameValueZero (DE2): NaN in [NaN] must be true — NaN is self-equal by value under SameValueZero.");
    }

    /// <summary>
    /// <c>arr.contains([NaN], NaN)</c> is <c>true</c> — NaN self-equal in arr.contains.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void ArrContains_NaN_IsSelfEqual_PerSpecEqualityMembership()
    {
        var result = (bool?)Run(
            "let big = conv.toFloat(\"1e308\") * 10.0; let n = big - big; let result = arr.contains([n], n);");
        Assert.True(result,
            "§Equality SameValueZero (DE2): arr.contains([NaN], NaN) must be true — NaN self-equal by value.");
    }

    /// <summary>
    /// Two distinct NaN values (computed separately) are mutually <c>in</c> each other.
    /// SameValueZero: all NaN payloads are equal by value.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void InOperator_TwoDistinctNaN_AreMutuallyIn_PerSpecEqualityMembership()
    {
        var result = (bool?)Run(@"
let big = conv.toFloat(""1e308"") * 10.0;
let n1 = big - big;
let n2 = big - big;
let result = n1 in [n2];");
        Assert.True(result,
            "§Equality SameValueZero (DE2): NaN in [NaN] (two distinct NaN computations) must be true — all NaN payloads equal by value.");
    }

    // ── Secret reference identity preserved (DE4) ──────────────────────────────

    /// <summary>
    /// Two distinct <c>secret("x")</c> constructions are <em>not</em> <c>in</c> each other.
    /// SameValueZero's non-numeric branch preserves reference identity per §Values D4 (DE4-lock).
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void InOperator_TwoDistinctSecrets_AreNotEqual_PerSpecEqualityDE4()
    {
        var result = (bool?)Run("let a = secret(\"x\"); let b = secret(\"x\"); let result = a in [b];");
        Assert.False(result,
            "§Equality DE4 (secret reference identity): two distinct secret(\"x\") constructions must not be in each other. " +
            "SameValueZero's non-numeric branch delegates to reference identity, per §Values D4 USER-OVERRIDE.");
    }

    /// <summary>
    /// An aliased <c>secret</c> handle IS <c>in</c> its own array.
    /// §Equality Edit E1, membership half. DE4 conformance from the <c>in</c> surface.
    /// </summary>
    [Fact]
    public void InOperator_AliasedSecret_IsInItsOwnArray_PerSpecEqualityDE4()
    {
        var result = (bool?)Run("let t = secret(\"x\"); let result = t in [t];");
        Assert.True(result,
            "§Equality DE4: aliased secret handle must be in its own array (same reference, reference-identity equal).");
    }

    // ── Reference identity for other aggregates ────────────────────────────────

    /// <summary>
    /// <c>[1] in [[1]]</c> is <c>false</c> — distinct array constructions with same elements
    /// are not equal under SameValueZero (reference identity for aggregates).
    /// §Equality — non-numeric branch.
    /// </summary>
    [Fact]
    public void InOperator_DistinctArrayConstructions_AreNotEqual_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = [1] in [[1]];");
        Assert.False(result,
            "§Equality SameValueZero non-numeric branch: [1] in [[1]] must be false " +
            "— distinct array constructions are not equal (reference identity).");
    }

    /// <summary>
    /// <c>{x:1} in [{x:1}]</c> is <c>false</c> — distinct struct/dict constructions are not equal.
    /// §Equality — non-numeric branch (reference identity for aggregates).
    /// </summary>
    [Fact]
    public void InOperator_DistinctDictConstructions_AreNotEqual_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = {x:1} in [{x:1}];");
        Assert.False(result,
            "§Equality SameValueZero non-numeric branch: {x:1} in [{x:1}] must be false " +
            "— distinct dict constructions are not equal (reference identity).");
    }

    // ── arr search family: indexOf + remove (DE2) ─────────────────────────────

    /// <summary>
    /// <c>arr.indexOf([1.0, 2.0], 1)</c> is <c>0</c> — integer 1 found at index 0 via SameValueZero.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void ArrIndexOf_IntInFloatArray_ReturnsIndex_PerSpecEqualityMembership()
    {
        var result = Run("let result = arr.indexOf([1.0, 2.0], 1);");
        // arr.indexOf([1.0, 2.0], 1) must return 0 — int found at index 0 via SameValueZero (DE2).
        Assert.Equal(0L, result);
    }

    /// <summary>
    /// <c>arr.remove([1.0], 1)</c> removes integer 1 from a float array via SameValueZero.
    /// After removal, the array is empty.
    /// §Equality Edit E1, membership half.
    /// </summary>
    [Fact]
    public void ArrRemove_IntFromFloatArray_RemovesElement_PerSpecEqualityMembership()
    {
        var result = Run("let a = [1.0]; arr.remove(a, 1); let result = a;");
        var list = Assert.IsType<System.Collections.Generic.List<object?>>(result);
        Assert.Empty(list);
    }

    // ── arr.unique: SameValueZero dedup direction ──────────────────────────────

    /// <summary>
    /// <c>arr.unique([1, 1.0])</c> returns <c>[1]</c> — int 1 and float 1.0 are SameValueZero-equal,
    /// so only the first occurrence (int) is kept.
    /// §Equality Edit E1, membership half. Decision: arr.unique deduplicates by SameValueZero (consistent).
    /// </summary>
    [Fact]
    public void ArrUnique_IntAndFloatAreDeduplicated_PerSpecEqualityMembership()
    {
        var result = Run("let result = arr.unique([1, 1.0]);");
        var list = Assert.IsType<System.Collections.Generic.List<object?>>(result);
        Assert.Single(list);
    }

    // ── arr.includes / arr.lastIndexOf: SameValueZero (DE2) ───────────────────

    /// <summary>
    /// <c>arr.includes([1.0], 1)</c> is <c>true</c> — integer 1 found in float array via SameValueZero.
    /// §Equality Edit E1, membership half; closes F02 conformance gap for <c>arr.includes</c>.
    /// </summary>
    [Fact]
    public void ArrIncludes_IntInFloatArray_IsTrue_PerSpecEqualityMembership()
    {
        var result = (bool?)Run("let result = arr.includes([1.0], 1);");
        Assert.True(result,
            "§Equality SameValueZero (DE2): arr.includes([1.0], 1) must be true — arr.includes uses SameValueZero, coercing int/float by mathematical value.");
    }

    /// <summary>
    /// <c>arr.lastIndexOf([1.0, 2, 1.0], 1)</c> returns <c>2</c> — integer 1 matches the last
    /// float 1.0 at index 2 via SameValueZero.
    /// §Equality Edit E1, membership half; closes F02 conformance gap for <c>arr.lastIndexOf</c>.
    /// </summary>
    [Fact]
    public void ArrLastIndexOf_IntInFloatArray_ReturnsLastIndex_PerSpecEqualityMembership()
    {
        // §Equality SameValueZero (DE2): arr.lastIndexOf([1.0, 2, 1.0], 1) must return 2
        // — arr.lastIndexOf uses SameValueZero, coercing int/float by mathematical value.
        var result = Run("let result = arr.lastIndexOf([1.0, 2, 1.0], 1);");
        Assert.Equal(2L, result);
    }
}
