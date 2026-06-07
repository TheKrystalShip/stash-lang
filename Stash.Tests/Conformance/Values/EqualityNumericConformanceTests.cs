using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Values;

/// <summary>
/// Conformance tests for §Values and Types — Equality (numeric/cross-type/NaN),
/// proving Spec Edit 4 and D2 ratification.
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clauses of
/// <c>docs/Stash — Language Specification.md</c> §Values and Types → Equality,
/// specifically:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Edit 4 — numeric-coercion rule</b> — The three numeric categories
///     (<c>int</c>, <c>float</c>, <c>byte</c>) form a single equivalence class for
///     <c>==</c> and <c>!=</c>. Operands from any two of these categories are
///     compared by mathematical value: <c>1 == 1.0</c> is <c>true</c>,
///     <c>0 == 0.0</c> is <c>true</c>, <c>-0.0 == 0</c> is <c>true</c>,
///     <c>byte 0 == 0</c> is <c>true</c>, <c>byte 7 == 7.0</c> is <c>true</c>.
///   </item>
///   <item>
///     <b>Edit 4 — non-numeric cross-category rule</b> — Outside the numeric
///     equivalence class, two values of different runtime categories are never equal:
///     <c>1 == true</c> is <c>false</c>, <c>0 == null</c> is <c>false</c>,
///     <c>0 == ""</c> is <c>false</c>, <c>null == false</c> is <c>false</c>.
///   </item>
///   <item>
///     <b>Edit 4 — same-type equality</b> — Same-category values: <c>1 == 1</c>
///     (true), <c>1.0 == 1.0</c> (true), <c>"a" == "a"</c> (true),
///     <c>null == null</c> (true), <c>true == true</c> (true).
///   </item>
///   <item>
///     <b>Edit 4 — floating-point edges</b> — <c>0.0 == -0.0</c> is <c>true</c>;
///     <c>NaN != NaN</c> (IEEE 754); <c>typeof(nan) == "float"</c>.
///   </item>
///   <item>
///     <b>D2 (ratified)</b> — The constant folder already folds <c>1 == 1.0</c> to
///     <c>true</c> and is correct. The runtime path (<c>RuntimeOps.IsEqual</c>) was
///     the bug; it is now fixed to coerce cross-tag numeric pairs by mathematical
///     value, matching the folder.
///   </item>
/// </list>
///
/// <para>
/// <b>Literal-vs-variable pair pattern (normative test convention for this milestone).</b>
/// Every cross-type numeric <c>==</c> assertion is written TWICE:
/// <list type="number">
///   <item>
///     <b>Literal form</b> — both operands are literals (<c>1 == 1.0</c>). This
///     routes through the compiler's constant folder.
///   </item>
///   <item>
///     <b>Variable form</b> — both operands are <c>let</c>-bound variables
///     (<c>let i = 1; let f = 1.0; i == f</c>). This routes through the runtime
///     path (<c>RuntimeOps.IsEqual</c>).
///   </item>
/// </list>
/// Both forms must produce the <b>same boolean</b>. The assertion message
/// "Constant folder diverged from runtime path — D2 regression" identifies a
/// divergence. This pair pattern is the permanent regression guard for D2 and is
/// inherited by future units that touch cross-type equality.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class EqualityNumericConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helper — assert literal and variable forms agree and both equal expected
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the literal form and the variable form of a cross-type equality assertion
    /// and verifies that both forms return the same value (expected).
    /// A disagreement means the constant folder and the runtime path have diverged — D2 regression.
    /// </summary>
    private void AssertLiteralAndVariableFormAgree(string literalScript, string variableScript, bool expected)
    {
        var literalResult  = (bool?)Run(literalScript);
        var variableResult = (bool?)Run(variableScript);

        Assert.True(
            literalResult == variableResult,
            $"Constant folder diverged from runtime path — D2 regression.\n" +
            $"  Literal form result:   {literalResult}\n" +
            $"  Variable form result:  {variableResult}\n" +
            $"  Expected:              {expected}");

        Assert.Equal(expected, literalResult);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 4 + D2 — Numeric-coercion: int ↔ float
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 1 == 1.0 is true (int ↔ float, both literal and variable forms).
    /// Folder path: 1 == 1.0. Runtime path: let i = 1; let f = 1.0; i == f.
    /// </summary>
    [Fact]
    public void IntEqFloat_OneAndOnePointZero_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = 1 == 1.0;",
            "let i = 1; let f = 1.0; let result = i == f;",
            expected: true);
    }

    /// <summary>
    /// 0 == 0.0 is true (int ↔ float, both literal and variable forms).
    /// </summary>
    [Fact]
    public void IntEqFloat_ZeroAndZeroPointZero_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = 0 == 0.0;",
            "let i = 0; let f = 0.0; let result = i == f;",
            expected: true);
    }

    /// <summary>
    /// -0.0 == 0 is true (float ↔ int, negative-zero edge, both literal and variable forms).
    /// </summary>
    [Fact]
    public void FloatEqInt_NegativeZeroAndZero_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = -0.0 == 0;",
            "let f = -0.0; let i = 0; let result = f == i;",
            expected: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 4 + D2 — Numeric-coercion: byte ↔ int
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// byte 0 == 0 is true (byte ↔ int, both literal and variable forms).
    /// Before D2, the runtime path returned false (tag-strict miss).
    /// </summary>
    [Fact]
    public void ByteEqInt_ZeroAndZero_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = conv.toByte(0) == 0;",
            "let bi = conv.toByte(0); let i = 0; let result = bi == i;",
            expected: true);
    }

    /// <summary>
    /// 0 == byte 0 is true (int ↔ byte, commutative, both literal and variable forms).
    /// </summary>
    [Fact]
    public void IntEqByte_ZeroAndZero_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = 0 == conv.toByte(0);",
            "let i = 0; let bi = conv.toByte(0); let result = i == bi;",
            expected: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 4 + D2 — Numeric-coercion: byte ↔ float
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// byte 7 == 7.0 is true (byte ↔ float, both literal and variable forms).
    /// Before D2, the runtime path returned false (tag-strict miss).
    /// </summary>
    [Fact]
    public void ByteEqFloat_SevenAndSevenPointZero_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = conv.toByte(7) == 7.0;",
            "let bi7 = conv.toByte(7); let f = 7.0; let result = bi7 == f;",
            expected: true);
    }

    /// <summary>
    /// 7.0 == byte 7 is true (float ↔ byte, commutative, both literal and variable forms).
    /// </summary>
    [Fact]
    public void FloatEqByte_SevenPointZeroAndSeven_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = 7.0 == conv.toByte(7);",
            "let f = 7.0; let bi7 = conv.toByte(7); let result = f == bi7;",
            expected: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 4 — Non-numeric cross-category: always false
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 1 == true is false (int vs bool), both literal and variable forms.
    /// </summary>
    [Fact]
    public void IntEqBool_OneAndTrue_IsFalse_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = 1 == true;",
            "let i = 1; let b = true; let result = i == b;",
            expected: false);
    }

    /// <summary>
    /// 0 == null is false (int vs null), both literal and variable forms.
    /// </summary>
    [Fact]
    public void IntEqNull_ZeroAndNull_IsFalse_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = 0 == null;",
            "let i = 0; let n = null; let result = i == n;",
            expected: false);
    }

    /// <summary>
    /// 0 == "" is false (int vs string), both literal and variable forms.
    /// </summary>
    [Fact]
    public void IntEqString_ZeroAndEmpty_IsFalse_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = 0 == \"\";",
            "let i = 0; let s = \"\"; let result = i == s;",
            expected: false);
    }

    /// <summary>
    /// null == false is false (null vs bool), both literal and variable forms.
    /// </summary>
    [Fact]
    public void NullEqBool_NullAndFalse_IsFalse_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = null == false;",
            "let n = null; let b = false; let result = n == b;",
            expected: false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 4 — Same-type equality (literal and variable forms)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>1 == 1 is true (same-type int).</summary>
    [Fact]
    public void SameType_Int_OneEqOne_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = 1 == 1;",
            "let a = 1; let b = 1; let result = a == b;",
            expected: true);
    }

    /// <summary>1.0 == 1.0 is true (same-type float).</summary>
    [Fact]
    public void SameType_Float_OnePointZeroEqOnePointZero_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = 1.0 == 1.0;",
            "let a = 1.0; let b = 1.0; let result = a == b;",
            expected: true);
    }

    /// <summary>"a" == "a" is true (same-type string).</summary>
    [Fact]
    public void SameType_String_AeqA_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = \"a\" == \"a\";",
            "let a = \"a\"; let b = \"a\"; let result = a == b;",
            expected: true);
    }

    /// <summary>null == null is true (same-type null).</summary>
    [Fact]
    public void SameType_Null_NullEqNull_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = null == null;",
            "let a = null; let b = null; let result = a == b;",
            expected: true);
    }

    /// <summary>true == true is true (same-type bool).</summary>
    [Fact]
    public void SameType_Bool_TrueEqTrue_IsTrue_PerSpecValuesEqualityNumeric()
    {
        AssertLiteralAndVariableFormAgree(
            "let result = true == true;",
            "let a = true; let b = true; let result = a == b;",
            expected: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 4 — Floating-point edges
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>0.0 == -0.0 is true (IEEE 754 positive/negative zero).</summary>
    [Fact]
    public void FloatEdge_PositiveZeroEqNegativeZero_IsTrue_PerSpecValuesEqualityNumeric()
    {
        var result = (bool?)Run("let result = 0.0 == -0.0;");
        Assert.True(result, "0.0 == -0.0 must be true per IEEE 754 and spec Edit 4.");
    }

    /// <summary>
    /// NaN != NaN (IEEE 754 — NaN is the only value not equal to itself).
    /// NaN is reached via overflow arithmetic: conv.toFloat("1e308") * 10.0 → Infinity;
    /// Infinity - Infinity → NaN.
    /// </summary>
    [Fact]
    public void FloatEdge_NaN_NotEqualToItself_PerSpecValuesEqualityNumeric()
    {
        var nanEqNan = (bool?)Run(
            "let big = conv.toFloat(\"1e308\") * 10.0; let nan = big - big; let result = nan == nan;");
        Assert.False(nanEqNan, "NaN == NaN must be false per IEEE 754 and spec Edit 4.");
    }

    /// <summary>nan != nan is true (IEEE 754).</summary>
    [Fact]
    public void FloatEdge_NaN_NotEqualNeq_IsTrue_PerSpecValuesEqualityNumeric()
    {
        var nanNeqNan = (bool?)Run(
            "let big = conv.toFloat(\"1e308\") * 10.0; let nan = big - big; let result = nan != nan;");
        Assert.True(nanNeqNan, "NaN != NaN must be true per IEEE 754 and spec Edit 4.");
    }

    /// <summary>nan == 0.0 is false (NaN is not equal to any number).</summary>
    [Fact]
    public void FloatEdge_NaN_NotEqualToZero_PerSpecValuesEqualityNumeric()
    {
        var result = (bool?)Run(
            "let big = conv.toFloat(\"1e308\") * 10.0; let nan = big - big; let result = nan == 0.0;");
        Assert.False(result, "NaN == 0.0 must be false per IEEE 754 and spec Edit 4.");
    }

    /// <summary>typeof(nan) == "float" — NaN is still a float value.</summary>
    [Fact]
    public void FloatEdge_NaN_TypeofIsFloat_PerSpecValuesEqualityNumeric()
    {
        var result = (string?)Run(
            "let big = conv.toFloat(\"1e308\") * 10.0; let nan = big - big; let result = typeof(nan);");
        Assert.Equal("float", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §Equality — Membership and keying: tag-strict divergence (sealed)
    //
    // These tests pin the current tag-strict behavior for `in`, `arr.contains`,
    // and dict key lookup, as documented in the "Membership and keying use
    // tag-strict equality" clause of §Equality.
    //
    // IMPORTANT: These tests back the *sealed divergence* clause, not a bug.
    // When the deferred SameValueZero-unification feature ships, these will
    // flip from `false`/`null` to `true`/"int" — at that point flip the
    // assertions here and prune the backlog entry.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>1 in [1.0]</c> is <c>false</c>: array membership uses tag-strict equality,
    /// not the numeric-coercion rule of <c>==</c>.
    /// Sealed in §Equality — "Membership and keying use tag-strict equality".
    /// </summary>
    [Fact]
    public void InOperator_IntNotInFloatArray_IsFalse_PerSpecValuesEquality()
    {
        var result = (bool?)Run("let result = 1 in [1.0];");
        Assert.False(result,
            "in/dict-key tag-strict (sealed in §Equality): 1 in [1.0] must be false " +
            "— array membership uses tag-strict equality, not the == numeric-coercion rule.");
    }

    /// <summary>
    /// <c>arr.contains([1], 1.0)</c> is <c>false</c>: arr.contains uses tag-strict equality.
    /// Sealed in §Equality — "Membership and keying use tag-strict equality".
    /// </summary>
    [Fact]
    public void ArrContains_IntArrayDoesNotContainFloat_IsFalse_PerSpecValuesEquality()
    {
        var result = (bool?)Run("let result = arr.contains([1], 1.0);");
        Assert.False(result,
            "in/dict-key tag-strict (sealed in §Equality): arr.contains([1], 1.0) must be false " +
            "— arr.contains uses tag-strict equality, not the == numeric-coercion rule.");
    }

    /// <summary>
    /// Integer key <c>1</c> and float key <c>1.0</c> are distinct dictionary keys.
    /// A dict populated with integer key <c>1</c> returns <c>null</c> for float key <c>1.0</c>.
    /// Sealed in §Equality — "Membership and keying use tag-strict equality".
    /// </summary>
    [Fact]
    public void DictKey_IntAndFloatAreDistinctKeys_FloatLookupReturnsNull_PerSpecValuesEquality()
    {
        var result = Run("let d = {}; d[1] = \"int\"; let result = d[1.0];");
        Assert.True(result is null,
            "in/dict-key tag-strict (sealed in §Equality): d[1.0] must be null when d was populated with integer key 1 " +
            "— dict keys use tag-strict equality; integer 1 and float 1.0 are distinct keys.");
    }

    /// <summary>
    /// Integer key <c>1</c> round-trips correctly in a dict: <c>d[1]</c> returns the stored value.
    /// Sanity cross-check to confirm the dict-assignment in the tag-strict test is valid.
    /// </summary>
    [Fact]
    public void DictKey_IntKey_RoundTripsCorrectly_PerSpecValuesEquality()
    {
        var result = (string?)Run("let d = {}; d[1] = \"int\"; let result = d[1];");
        Assert.True(result == "int",
            "Sanity: d[1] after d[1]=\"int\" must return \"int\" — same integer key must round-trip.");
    }

    /// <summary>
    /// Sanity cross-check: <c>1 == 1.0</c> is <c>true</c> (D2 numeric coercion for <c>==</c>),
    /// confirming the divergence from the tag-strict membership behavior above is observable.
    /// </summary>
    [Fact]
    public void EqOperator_IntEqualsFloat_IsTrue_SanityForMembershipDivergence_PerSpecValuesEquality()
    {
        var result = (bool?)Run("let result = 1 == 1.0;");
        Assert.True(result,
            "Sanity cross-check: 1 == 1.0 must be true (D2 numeric coercion), " +
            "confirming the observable divergence with tag-strict in/arr.contains/dict-key.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 4 — Precision guard: same-tag int equality must not use ToDouble
    // (large int64 values that are distinct must remain distinct)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Large int64 values that are distinct must not be collapsed by a ToDouble promotion.
    /// 9007199254740993 and 9007199254740992 differ by 1 but collapse to the same
    /// double (IEEE 754 mantissa limit). Same-tag int comparison stays exact.
    /// </summary>
    [Fact]
    public void SameType_Int_LargeDistinctValues_NotEqual_PerSpecValuesEqualityNumeric()
    {
        var result = (bool?)Run(
            "let a = 9007199254740993; let b = 9007199254740992; let result = a == b;");
        Assert.False(result,
            "Same-tag int comparison must use exact 64-bit integer arithmetic, not double promotion. " +
            "9007199254740993 != 9007199254740992.");
    }
}
