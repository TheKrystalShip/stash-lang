using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Equality;

/// <summary>
/// Conformance tests for §Equality — Dictionary keys use SameValueZero (Edit E1, keying half, DE2/DE4).
///
/// <para>
/// P3 migrated <c>StashDictionary._entries</c> from <c>Dictionary&lt;object, StashValue&gt;</c>
/// to <c>Dictionary&lt;StashValue, StashValue&gt;</c> constructed with
/// <see cref="Stash.Runtime.StashEquality.SameValueZero"/>: integer key <c>1</c> and float key
/// <c>1.0</c> are the same dictionary key; <c>+0.0</c> and <c>-0.0</c> are unified; <c>NaN</c>
/// is self-equal by value (a <c>NaN</c> key round-trips).
/// </para>
///
/// <para>
/// Decision locks tested here:
/// <list type="bullet">
///   <item><b>DE2</b> — dict keys use SameValueZero: <c>d[1] = "a"; d[1.0]</c> is <c>"a"</c>.</item>
///   <item><b>DE4</b> — secret/aggregate reference identity preserved as dict keys: two distinct
///     <c>secret("x")</c> constructions are distinct keys; an aliased handle is the same key.
///     Arrays, dicts, struct instances, functions, namespaces, futures, ranges, and Error values
///     also key by reference identity.</item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class DictKeyConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Cross-type numeric keying: int / float / byte
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>d[1] = "a"; d[1.0]</c> returns <c>"a"</c>: integer key and float key for the same
    /// mathematical value are the same dict key (SameValueZero, Edit E1, keying half, DE2).
    /// </summary>
    [Fact]
    public void DictKey_IntKeyFloat_Lookup_ReturnsSameValue_PerSpecEqualityE1()
    {
        var result = (string?)Run("let d = {}; d[1] = \"a\"; let result = d[1.0];");
        Assert.Equal("a", result);
    }

    /// <summary>
    /// <c>d[1.0] = "a"; d[1]</c> returns <c>"a"</c>: float key then integer lookup (commutative).
    /// </summary>
    [Fact]
    public void DictKey_FloatKeyInt_Lookup_ReturnsSameValue_PerSpecEqualityE1()
    {
        var result = (string?)Run("let d = {}; d[1.0] = \"a\"; let result = d[1];");
        Assert.Equal("a", result);
    }

    /// <summary>
    /// <c>d[conv.toByte(7)] = "a"; d[7]</c> returns <c>"a"</c>: byte key and integer lookup.
    /// </summary>
    [Fact]
    public void DictKey_ByteKeyInt_Lookup_ReturnsSameValue_PerSpecEqualityE1()
    {
        var result = (string?)Run("let d = {}; d[conv.toByte(7)] = \"a\"; let result = d[7];");
        Assert.Equal("a", result);
    }

    /// <summary>
    /// <c>d[7] = "a"; d[conv.toByte(7)]</c> returns <c>"a"</c>: int key and byte lookup (commutative).
    /// </summary>
    [Fact]
    public void DictKey_IntKeyByte_Lookup_ReturnsSameValue_PerSpecEqualityE1()
    {
        var result = (string?)Run("let d = {}; d[7] = \"a\"; let result = d[conv.toByte(7)];");
        Assert.Equal("a", result);
    }

    /// <summary>
    /// <c>d[7.0] = "a"; d[conv.toByte(7)]</c> returns <c>"a"</c>: float key and byte lookup.
    /// </summary>
    [Fact]
    public void DictKey_FloatKeyByte_Lookup_ReturnsSameValue_PerSpecEqualityE1()
    {
        var result = (string?)Run("let d = {}; d[7.0] = \"a\"; let result = d[conv.toByte(7)];");
        Assert.Equal("a", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ±0 key unification
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>d[0.0] = "a"; d[-0.0]</c> returns <c>"a"</c>: <c>+0.0</c> and <c>-0.0</c> are the
    /// same dict key (SameValueZero unifies them, Edit E1, keying half).
    /// </summary>
    [Fact]
    public void DictKey_PosZeroKey_NegZeroLookup_ReturnsSameValue_PerSpecEqualityE1()
    {
        var result = (string?)Run("let d = {}; d[0.0] = \"a\"; let result = d[-0.0];");
        Assert.Equal("a", result);
    }

    /// <summary>
    /// <c>d[-0.0] = "a"; d[0.0]</c> returns <c>"a"</c>: commutative direction.
    /// </summary>
    [Fact]
    public void DictKey_NegZeroKey_PosZeroLookup_ReturnsSameValue_PerSpecEqualityE1()
    {
        var result = (string?)Run("let d = {}; d[-0.0] = \"a\"; let result = d[0.0];");
        Assert.Equal("a", result);
    }

    /// <summary>
    /// <c>d[0] = "a"; d[-0.0]</c> returns <c>"a"</c>: integer zero and <c>-0.0</c> are the same key.
    /// </summary>
    [Fact]
    public void DictKey_IntZeroKey_NegZeroLookup_ReturnsSameValue_PerSpecEqualityE1()
    {
        var result = (string?)Run("let d = {}; d[0] = \"a\"; let result = d[-0.0];");
        Assert.Equal("a", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NaN key round-trip
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A <c>NaN</c> key round-trips: a value stored under a <c>NaN</c> key is retrieved by
    /// the same <c>NaN</c> expression (SameValueZero: NaN is self-equal by value, Edit E1).
    /// </summary>
    [Fact]
    public void DictKey_NaN_RoundTrips_PerSpecEqualityE1()
    {
        var result = (string?)Run(
            "let big = conv.toFloat(\"1e308\") * 10.0; let n = big - big; " +
            "let d = {}; d[n] = \"x\"; let result = d[n];");
        Assert.Equal("x", result);
    }

    /// <summary>
    /// A <c>NaN</c> key participates in <c>in</c> correctly: <c>n in d</c> is <c>true</c>
    /// when <c>d</c> was populated with key <c>n</c> (NaN).
    /// </summary>
    [Fact]
    public void DictKey_NaN_InOperator_IsTrue_PerSpecEqualityE1()
    {
        var result = (bool?)Run(
            "let big = conv.toFloat(\"1e308\") * 10.0; let n = big - big; " +
            "let d = {}; d[n] = \"x\"; let result = n in d;");
        Assert.True(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Hash/equals consistency for SameValueZero comparer
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hash/equals consistency conformance: for any pair that is SameValueZero-equal, the
    /// comparer produces equal hash codes. Asserted via round-trip: insert with one key form,
    /// retrieve with the other — a hash mismatch corrupts silently (miss on equal key).
    /// This test covers all coercion pairs: int/float, int/byte, float/byte, ±0, and NaN.
    /// </summary>
    [Fact]
    public void DictKey_HashEqualsConsistency_AllCoercionPairs_RoundTrip_PerSpecEqualityDE2()
    {
        // int / float
        var r1 = (string?)Run("let d = {}; d[1] = \"v\"; let result = d[1.0];");
        Assert.Equal("v", r1);

        // float / int
        var r2 = (string?)Run("let d = {}; d[1.0] = \"v\"; let result = d[1];");
        Assert.Equal("v", r2);

        // int / byte
        var r3 = (string?)Run("let d = {}; d[7] = \"v\"; let result = d[conv.toByte(7)];");
        Assert.Equal("v", r3);

        // byte / int
        var r4 = (string?)Run("let d = {}; d[conv.toByte(7)] = \"v\"; let result = d[7];");
        Assert.Equal("v", r4);

        // +0.0 / -0.0
        var r5 = (string?)Run("let d = {}; d[0.0] = \"v\"; let result = d[-0.0];");
        Assert.Equal("v", r5);

        // NaN round-trip (NaN hash must be stable and consistent)
        var r6 = (string?)Run(
            "let big = conv.toFloat(\"1e308\") * 10.0; let n = big - big; " +
            "let d = {}; d[n] = \"v\"; let result = d[n];");
        Assert.Equal("v", r6);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DE4: secret reference identity as dict key
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two distinct <c>secret("x")</c> constructions are distinct dict keys (DE4 reference
    /// identity). §Equality Edit E1 keying half; secret dict-key is explicitly sealed in
    /// §Values &amp; Types D4 (secret reference identity preserved by SameValueZero).
    /// </summary>
    [Fact]
    public void DictKey_TwoDistinctSecrets_AreDistinctKeys_PerSpecEqualityDE4()
    {
        var result = Run(@"let d = {}; d[secret(""x"")] = 1; let result = d[secret(""x"")];");
        Assert.Null(result);
    }

    /// <summary>
    /// The same secret handle is the same dict key: insert and retrieve via the same binding.
    /// </summary>
    [Fact]
    public void DictKey_SameSecretHandle_IsSameKey_PerSpecEqualityDE4()
    {
        var result = Run(@"let t = secret(""x""); let d = {}; d[t] = 1; let result = d[t];");
        Assert.Equal(1L, result);
    }

    /// <summary>
    /// <c>t in d</c> is <c>true</c> when the same secret handle <c>t</c> was used as the key.
    /// </summary>
    [Fact]
    public void DictKey_SameSecretHandle_InOperator_IsTrue_PerSpecEqualityDE4()
    {
        var result = (bool?)Run(@"let t = secret(""x""); let d = {}; d[t] = 1; let result = t in d;");
        Assert.True(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Aggregate reference identity as dict key
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two distinct <c>[1, 2]</c> arrays are distinct dict keys (aggregate reference identity,
    /// Edit E1 keying half). Arrays key by identity, not structural equality.
    /// </summary>
    [Fact]
    public void DictKey_TwoDistinctArrays_AreDistinctKeys_PerSpecEqualityE1()
    {
        var result = Run("let d = {}; d[[1, 2]] = 1; let result = d[[1, 2]];");
        Assert.Null(result);
    }

    /// <summary>
    /// The same array binding used twice is the same dict key.
    /// </summary>
    [Fact]
    public void DictKey_SameArrayHandle_IsSameKey_PerSpecEqualityE1()
    {
        var result = Run("let arr = [1, 2]; let d = {}; d[arr] = 1; let result = d[arr];");
        Assert.Equal(1L, result);
    }

    /// <summary>
    /// Two distinct dict constructions are distinct keys (aggregate reference identity,
    /// Edit E1 keying half). Dicts key by identity, not structural equality.
    /// </summary>
    [Fact]
    public void DictKey_TwoDistinctDicts_AreDistinctKeys_PerSpecEqualityE1()
    {
        var result = Run("let d = {}; d[{x: 1}] = 1; let result = d[{x: 1}];");
        Assert.Null(result);
    }

    /// <summary>
    /// The same dict binding used twice is the same dict key.
    /// </summary>
    [Fact]
    public void DictKey_SameDictHandle_IsSameKey_PerSpecEqualityE1()
    {
        var result = Run("let inner = {x: 1}; let d = {}; d[inner] = 1; let result = d[inner];");
        Assert.Equal(1L, result);
    }

    /// <summary>
    /// The same range binding used twice is the same dict key (reference identity).
    /// Two distinct range literals are distinct keys.
    /// </summary>
    [Fact]
    public void DictKey_RangeHandle_IsSameKey_PerSpecEqualityE1()
    {
        // Same binding → same key
        var result1 = Run("let r = 1..3; let d = {}; d[r] = 1; let result = d[r];");
        Assert.Equal(1L, result1);

        // Two distinct range literals → distinct keys (struct comparison by identity)
        var result2 = Run("let d = {}; d[1..3] = 1; let result = d[1..3];");
        Assert.Null(result2);
    }

    /// <summary>
    /// A namespace used as a dict key is the same key via its singleton reference.
    /// A registered namespace is a singleton, so <c>d[io] = 1; d[io]</c> returns 1.
    /// </summary>
    [Fact]
    public void DictKey_NamespaceHandle_IsSameKey_PerSpecEqualityE1()
    {
        var result = Run("let d = {}; d[io] = 1; let result = d[io];");
        Assert.Equal(1L, result);
    }

    /// <summary>
    /// A function used as a dict key is the same key via its function handle.
    /// </summary>
    [Fact]
    public void DictKey_FunctionHandle_IsSameKey_PerSpecEqualityE1()
    {
        var result = Run("fn f() { return 0; }; let d = {}; d[f] = 1; let result = d[f];");
        Assert.Equal(1L, result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Value-typed keying: duration / bytes / ip / semver (§Equality keying clause)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>duration</c>, <c>bytes</c>, <c>ip</c>, and <c>semver</c> key by value
    /// (per the per-category equality rule): two distinct constructions with the same
    /// value payload are the same dict key.
    ///
    /// Each subtest inserts under one freshly-constructed key and retrieves under a
    /// separately-constructed key (same value, distinct object reference), so the
    /// round-trip only succeeds if the dict uses value equality, not reference identity.
    /// Arithmetic on literals (e.g. <c>512B + 512B</c>) forces two separate runtime
    /// allocations via <c>StashByteSize.Add</c> / <c>StashDuration.Add</c> /
    /// <c>StashIpAddress.Add</c>, defeating the constant-pool interning that would
    /// otherwise collapse two identical literals to a single shared object.
    ///
    /// §Equality keying clause — value-typed cohort (Edit E1 / F01 conformance gap closure).
    /// </summary>
    [Fact]
    public void DictKey_ValueTypedKeys_KeyByValue_PerSpecEqualityE1()
    {
        // bytes: two distinct StashByteSize instances (arithmetic forces fresh allocation)
        // disassembly: `add rX` at k1 assignment and a separate `add rY` at k2 assignment —
        // set.table uses k1's register, get.table uses k2's register (distinct references)
        var r1 = Run("let d = {}; let k1 = 512B + 512B; d[k1] = 1; let k2 = 512B + 512B; let result = d[k2];");
        Assert.Equal(1L, r1);

        // duration: two distinct StashDuration instances (arithmetic forces fresh allocation)
        var r2 = Run("let d = {}; let k1 = 3s + 2s; d[k1] = 1; let k2 = 3s + 2s; let result = d[k2];");
        Assert.Equal(1L, r2);

        // semver: constructor form `semver("1.2.3")` — two separate builtin calls
        var r3 = Run("let d = {}; d[semver(\"1.2.3\")] = 1; let result = d[semver(\"1.2.3\")];");
        Assert.Equal(1L, r3);

        // ip: two distinct StashIpAddress instances (add-zero forces fresh allocation)
        var r4 = Run("let d = {}; let k1 = @10.0.0.1 + 0; d[k1] = 1; let k2 = @10.0.0.1 + 0; let result = d[k2];");
        Assert.Equal(1L, r4);
    }
}
