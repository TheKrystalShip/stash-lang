using Stash.Runtime;
using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Equality;

/// <summary>
/// Conformance tests for §Equality — Strict assert equality (Edit E2, DE3).
///
/// <para>
/// P4 migrated <c>assert.equal</c> and <c>assert.notEqual</c> from the legacy
/// <c>RuntimeValues.IsEqual</c> object-boxing path to
/// <see cref="Stash.Runtime.StashEquality.StrictEquals"/>: operands of different
/// <c>typeof</c> are never equal even within the numeric equivalence class.
/// </para>
///
/// <para>
/// Decision locks tested here:
/// <list type="bullet">
///   <item><b>DE3</b> — assert uses tag-strict equality: <c>assert.equal(1, 1.0)</c> raises
///     <c>AssertionError</c> even though <c>1 == 1.0</c> is <c>true</c>. Within a tag,
///     <c>±0</c> is unified and <c>NaN</c> is reflexive.</item>
///   <item><b>DE4</b> — secret/aggregate reference identity preserved: two distinct
///     <c>secret("x")</c> constructions are not equal; an aliased handle is equal to itself.
///     Same for arrays.</item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class StrictAssertConformanceTests : StashTestBase
{
    // ── Tag-strict: cross-type pairs (DE3) ────────────────────────────────────

    /// <summary>
    /// <c>assert.equal(1, 1.0)</c> raises <c>AssertionError</c> — int vs float, different tags.
    /// §Equality Edit E2, DE3: tag-strict; <c>1 == 1.0</c> is <c>true</c> but <c>assert.equal</c> is not.
    /// </summary>
    [Fact]
    public void Equal_IntVsFloat_ThrowsAssertionError_PerSpecDE3TagStrict()
    {
        var ex = RunCapturingError("assert.equal(1, 1.0);");
        Assert.IsType<AssertionError>(ex);
    }

    /// <summary>
    /// <c>assert.equal(1.0, 1)</c> raises <c>AssertionError</c> — float vs int, different tags.
    /// §Equality Edit E2, DE3: tag-strict symmetric.
    /// </summary>
    [Fact]
    public void Equal_FloatVsInt_ThrowsAssertionError_PerSpecDE3TagStrict()
    {
        var ex = RunCapturingError("assert.equal(1.0, 1);");
        Assert.IsType<AssertionError>(ex);
    }

    /// <summary>
    /// <c>assert.equal(conv.toByte(0), 0)</c> raises <c>AssertionError</c> — byte vs int.
    /// §Equality Edit E2, DE3: tag-strict — byte and int are different tags even when value is equal.
    /// </summary>
    [Fact]
    public void Equal_ByteVsInt_ThrowsAssertionError_PerSpecDE3TagStrict()
    {
        var ex = RunCapturingError("assert.equal(conv.toByte(0), 0);");
        Assert.IsType<AssertionError>(ex);
    }

    /// <summary>
    /// <c>assert.equal(1, true)</c> raises <c>AssertionError</c> — int vs bool, different tags.
    /// §Equality Edit E2, DE3: tag-strict non-numeric cross-category.
    /// </summary>
    [Fact]
    public void Equal_IntVsBool_ThrowsAssertionError_PerSpecDE3TagStrict()
    {
        var ex = RunCapturingError("assert.equal(1, true);");
        Assert.IsType<AssertionError>(ex);
    }

    /// <summary>
    /// <c>assert.equal(0, null)</c> raises <c>AssertionError</c> — int vs null, different tags.
    /// §Equality Edit E2, DE3: tag-strict.
    /// </summary>
    [Fact]
    public void Equal_IntVsNull_ThrowsAssertionError_PerSpecDE3TagStrict()
    {
        var ex = RunCapturingError("assert.equal(0, null);");
        Assert.IsType<AssertionError>(ex);
    }

    /// <summary>
    /// <c>assert.equal(0, "")</c> raises <c>AssertionError</c> — int vs string, different tags.
    /// §Equality Edit E2, DE3: tag-strict.
    /// </summary>
    [Fact]
    public void Equal_IntVsString_ThrowsAssertionError_PerSpecDE3TagStrict()
    {
        var ex = RunCapturingError("assert.equal(0, \"\");");
        Assert.IsType<AssertionError>(ex);
    }

    // ── Within-tag value equality (DE3) ────────────────────────────────────────

    /// <summary>
    /// <c>assert.equal(0.0, -0.0)</c> passes — ±0 unified within float tag.
    /// §Equality Edit E2, DE3: within Float tag, <c>double.Equals</c> unifies ±0.
    /// </summary>
    [Fact]
    public void Equal_PlusZeroVsMinusZero_Passes_PerSpecDE3ZeroUnified()
    {
        RunStatements("assert.equal(0.0, -0.0);");
    }

    /// <summary>
    /// <c>assert.equal(0, 0)</c> passes — same int value.
    /// §Equality Edit E2, DE3: within Int tag, value equality.
    /// </summary>
    [Fact]
    public void Equal_SameInt_Passes_PerSpecDE3WithinTag()
    {
        RunStatements("assert.equal(0, 0);");
    }

    /// <summary>
    /// <c>assert.equal(1.0, 1.0)</c> passes — same float value.
    /// §Equality Edit E2, DE3: within Float tag, value equality.
    /// </summary>
    [Fact]
    public void Equal_SameFloat_Passes_PerSpecDE3WithinTag()
    {
        RunStatements("assert.equal(1.0, 1.0);");
    }

    /// <summary>
    /// <c>assert.equal("a", "a")</c> passes — string by value.
    /// §Equality Edit E2, DE3: within string tag, value equality.
    /// </summary>
    [Fact]
    public void Equal_SameString_Passes_PerSpecDE3WithinTag()
    {
        RunStatements("assert.equal(\"a\", \"a\");");
    }

    /// <summary>
    /// <c>assert.equal(null, null)</c> passes — null is self-equal.
    /// §Equality Edit E2, DE3: within null tag.
    /// </summary>
    [Fact]
    public void Equal_NullNull_Passes_PerSpecDE3WithinTag()
    {
        RunStatements("assert.equal(null, null);");
    }

    /// <summary>
    /// <c>assert.equal(true, true)</c> passes — same bool value.
    /// §Equality Edit E2, DE3: within bool tag, value equality.
    /// </summary>
    [Fact]
    public void Equal_TrueTrue_Passes_PerSpecDE3WithinTag()
    {
        RunStatements("assert.equal(true, true);");
    }

    // ── NaN reflexive under StrictEquals (DE3) ────────────────────────────────

    /// <summary>
    /// <c>assert.equal(NaN, NaN)</c> passes — NaN is reflexive under strict assert equality.
    /// §Equality Edit E2, DE3: within Float tag, <c>double.Equals</c> is NaN-reflexive
    /// (unlike IEEE 754 <c>==</c>).
    /// </summary>
    [Fact]
    public void Equal_NaNSelf_Passes_PerSpecDE3NaNReflexive()
    {
        RunStatements(@"
let big = conv.toFloat(""1e308"") * 10.0;
let n = big - big;
assert.equal(n, n);
");
    }

    // ── DE4: secret reference identity ────────────────────────────────────────

    /// <summary>
    /// Two distinct <c>secret("x")</c> constructions are not equal — reference identity.
    /// §Equality Edit E2, DE3 + §Secret Values DE4.
    /// </summary>
    [Fact]
    public void Equal_DistinctSecrets_ThrowsAssertionError_PerSpecDE4ReferenceIdentity()
    {
        var ex = RunCapturingError("assert.equal(secret(\"x\"), secret(\"x\"));");
        Assert.IsType<AssertionError>(ex);
    }

    /// <summary>
    /// An aliased secret handle is equal to itself — same object.
    /// §Equality Edit E2, DE3 + §Secret Values DE4.
    /// </summary>
    [Fact]
    public void Equal_AliasedSecret_Passes_PerSpecDE4ReferenceIdentity()
    {
        RunStatements("let t = secret(\"x\"); assert.equal(t, t);");
    }

    /// <summary>
    /// <c>assert.notEqual(secret("x"), secret("x"))</c> passes — distinct constructions.
    /// §Equality Edit E2, DE3 + §Secret Values DE4.
    /// </summary>
    [Fact]
    public void NotEqual_DistinctSecrets_Passes_PerSpecDE4ReferenceIdentity()
    {
        RunStatements("assert.notEqual(secret(\"x\"), secret(\"x\"));");
    }

    // ── DE4 / aggregate reference identity ────────────────────────────────────

    /// <summary>
    /// Two arrays with identical elements but distinct constructions are not equal.
    /// §Equality Edit E2, DE3: aggregate reference identity for arrays.
    /// </summary>
    [Fact]
    public void Equal_DistinctArrays_ThrowsAssertionError_PerSpecAggrRefIdentity()
    {
        var ex = RunCapturingError("assert.equal([1], [1]);");
        Assert.IsType<AssertionError>(ex);
    }

    /// <summary>
    /// An aliased array handle is equal to itself.
    /// §Equality Edit E2, DE3: same object reference.
    /// </summary>
    [Fact]
    public void Equal_AliasedArray_Passes_PerSpecAggrRefIdentity()
    {
        RunStatements("let a = [1]; assert.equal(a, a);");
    }
}
