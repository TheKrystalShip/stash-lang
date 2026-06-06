using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Values;

/// <summary>
/// Conformance tests for §Values and Types — Type Coercion,
/// proving Spec Edit 5: the closed enumeration of implicit conversions.
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clauses of
/// <c>docs/Stash — Language Specification.md</c> §Values and Types → Type Coercion,
/// specifically:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Edit 5 — case 1: numeric promotion in arithmetic, relational, AND equality.</b>
///     When <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c>, <c>%</c>, <c>**</c>,
///     <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>, <c>==</c>, <c>!=</c>
///     are applied to two numeric operands of different categories (<c>int</c>,
///     <c>float</c>, <c>byte</c>), the operands are promoted to a common category.
///     Arithmetic/relational results are widened to <c>int</c> or <c>float</c>;
///     equality results are <c>bool</c> (cross-referenced from Edit 4 / P3).
///   </item>
///   <item>
///     <b>Edit 5 — case 2: byte promotion to int in arithmetic and relational operators.</b>
///     A <c>byte</c> operand is promoted to <c>int</c> before <c>+</c>, <c>-</c>,
///     <c>*</c>, <c>/</c>, <c>%</c>, <c>**</c>, <c>&lt;</c>, <c>&lt;=</c>,
///     <c>&gt;</c>, <c>&gt;=</c>. The result category is <c>int</c> or <c>float</c>,
///     never <c>byte</c>. Byte equality (<c>==</c>/<c>!=</c>) is covered by case 1.
///   </item>
///   <item>
///     <b>Edit 5 — case 3: string concatenation with <c>+</c>.</b>
///     When one operand of <c>+</c> is a <c>string</c>, the other operand is
///     stringified and the result is a <c>string</c>. A <c>secret</c> operand
///     produces a <c>secret</c> result (taint propagation; covered in Edit 6).
///   </item>
///   <item>
///     <b>Edit 5 — negative space (closed list).</b>
///     Outside the three cases, every operand mismatch raises a
///     <c>RuntimeError</c> with the operand types in the message. No
///     <c>bool</c>→<c>int</c> promotion, no <c>null</c>→anything promotion, no
///     <c>string</c>↔<c>number</c> coercion for non-<c>+</c> operations.
///   </item>
/// </list>
///
/// <para>
/// <b>Exception-type confirmation:</b> every throw exercised here reaches
/// <c>RuntimeOps.Add</c>, <c>RuntimeOps.Compare</c>, or their sibling methods,
/// all of which raise a plain <c>RuntimeError</c> (the base type, not a registered
/// <c>[StashError]</c> subtype). <c>RunCapturingError</c> uses
/// <c>Assert.ThrowsAny&lt;RuntimeError&gt;</c>, which accepts the base type.
/// </para>
///
/// <para>
/// <b>Variable-form negative-space tests.</b> Negative-space tests bind operands
/// to <c>let</c> variables rather than literals to guarantee the runtime code path
/// (<c>RuntimeOps.Add</c>, etc.) is exercised rather than the constant folder.
/// The constant folder does not fold <c>null+int</c>, <c>bool+int</c>, or
/// <c>int+array</c>, so literal forms would also work — but the variable form is
/// the documented convention for runtime-path assertions in this milestone.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class CoercionConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Case 1 — Numeric promotion: arithmetic operators (+, *, /, %)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 1 + 1.0 returns 2.0 (float); typeof result == "float".
    /// int + float → float (widening rule).
    /// </summary>
    [Fact]
    public void NumericPromotion_IntPlusFloat_ReturnsFloat_PerSpecValuesCoercion()
    {
        var value  = Run("let result = 1 + 1.0;");
        var typeof_ = Run("let r = 1 + 1.0; let result = typeof(r);");
        Assert.Equal(2.0, value);
        Assert.Equal("float", typeof_);
    }

    /// <summary>
    /// 2 * 1.5 returns 3.0 (float); typeof result == "float".
    /// int * float → float.
    /// </summary>
    [Fact]
    public void NumericPromotion_IntTimesFloat_ReturnsFloat_PerSpecValuesCoercion()
    {
        var value   = Run("let result = 2 * 1.5;");
        var typeof_ = Run("let r = 2 * 1.5; let result = typeof(r);");
        Assert.Equal(3.0, value);
        Assert.Equal("float", typeof_);
    }

    /// <summary>
    /// 4 / 2.0 returns 2.0 (float); typeof result == "float".
    /// int / float → float.
    /// </summary>
    [Fact]
    public void NumericPromotion_IntDivFloat_ReturnsFloat_PerSpecValuesCoercion()
    {
        var value   = Run("let result = 4 / 2.0;");
        var typeof_ = Run("let r = 4 / 2.0; let result = typeof(r);");
        Assert.Equal(2.0, value);
        Assert.Equal("float", typeof_);
    }

    /// <summary>
    /// 5 % 2.0 returns 1.0 (float); typeof result == "float".
    /// int % float → float.
    /// </summary>
    [Fact]
    public void NumericPromotion_IntModFloat_ReturnsFloat_PerSpecValuesCoercion()
    {
        var value   = Run("let result = 5 % 2.0;");
        var typeof_ = Run("let r = 5 % 2.0; let result = typeof(r);");
        Assert.Equal(1.0, value);
        Assert.Equal("float", typeof_);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Case 1 — Numeric promotion: relational operators
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 1 &lt; 1.5 returns true (int vs float relational comparison — numeric promotion).
    /// </summary>
    [Fact]
    public void NumericPromotion_IntLtFloat_ReturnsTrue_PerSpecValuesCoercion()
    {
        var result = (bool?)Run("let result = 1 < 1.5;");
        Assert.True(result, "1 < 1.5 must be true per spec Edit 5 numeric promotion.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Case 1 — Numeric promotion: equality (cross-reference to Edit 4 / D2)
    // Per spec, byte equality is covered by rule 1 (the numeric equivalence class)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 1 == 1.0 returns true (int ↔ float; D2 ratification, Edit 4 anchor).
    /// Asserted here as the coercion-clause anchor (Edit 5 case 1 cross-reference).
    /// </summary>
    [Fact]
    public void NumericPromotion_IntEqFloat_IsTrue_PerSpecValuesCoercion()
    {
        var result = (bool?)Run("let result = 1 == 1.0;");
        Assert.True(result, "1 == 1.0 must be true per spec Edit 5 case 1 (numeric equivalence class).");
    }

    /// <summary>
    /// 0 == 0.0 returns true (int ↔ float numeric coercion).
    /// </summary>
    [Fact]
    public void NumericPromotion_ZeroEqZeroPointZero_IsTrue_PerSpecValuesCoercion()
    {
        var result = (bool?)Run("let result = 0 == 0.0;");
        Assert.True(result, "0 == 0.0 must be true per spec Edit 5 case 1.");
    }

    /// <summary>
    /// -0.0 == 0 returns true (float ↔ int negative-zero edge, D2 ratification).
    /// </summary>
    [Fact]
    public void NumericPromotion_NegativeZeroEqZero_IsTrue_PerSpecValuesCoercion()
    {
        var result = (bool?)Run("let result = -0.0 == 0;");
        Assert.True(result, "-0.0 == 0 must be true per spec Edit 5 case 1.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Case 2 — Byte promotion to int in arithmetic and relational operators
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// conv.toByte(7) + 1 returns 8; typeof result == "int" (NOT "byte").
    /// byte + int → int (byte promoted to int before operation).
    /// </summary>
    [Fact]
    public void BytePromotion_BytePlusInt_ResultIsInt_PerSpecValuesCoercion()
    {
        var value   = Run("let result = conv.toByte(7) + 1;");
        var typeof_ = Run("let r = conv.toByte(7) + 1; let result = typeof(r);");
        Assert.Equal(8L, value);
        Assert.Equal("int", typeof_);
    }

    /// <summary>
    /// conv.toByte(7) * 2 returns 14; typeof result == "int" (NOT "byte").
    /// byte * int → int.
    /// </summary>
    [Fact]
    public void BytePromotion_ByteTimesInt_ResultIsInt_PerSpecValuesCoercion()
    {
        var value   = Run("let result = conv.toByte(7) * 2;");
        var typeof_ = Run("let r = conv.toByte(7) * 2; let result = typeof(r);");
        Assert.Equal(14L, value);
        Assert.Equal("int", typeof_);
    }

    /// <summary>
    /// conv.toByte(5) &lt; 10 returns true (byte promoted to int for relational comparison).
    /// </summary>
    [Fact]
    public void BytePromotion_ByteLtInt_ReturnsTrue_PerSpecValuesCoercion()
    {
        var result = (bool?)Run("let result = conv.toByte(5) < 10;");
        Assert.True(result, "conv.toByte(5) < 10 must be true per spec Edit 5 case 2.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Case 2 — Byte equality is case 1 (numeric equivalence class), not case 2
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// conv.toByte(7) == 7 returns true (byte ↔ int numeric equivalence class, case 1).
    /// </summary>
    [Fact]
    public void ByteEquality_ByteEqInt_IsTrue_PerSpecValuesCoercion()
    {
        var result = (bool?)Run("let result = conv.toByte(7) == 7;");
        Assert.True(result, "conv.toByte(7) == 7 must be true per spec Edit 5 case 1 (numeric equivalence class).");
    }

    /// <summary>
    /// conv.toByte(7) == 7.0 returns true (byte ↔ float numeric equivalence class, case 1).
    /// </summary>
    [Fact]
    public void ByteEquality_ByteEqFloat_IsTrue_PerSpecValuesCoercion()
    {
        var result = (bool?)Run("let result = conv.toByte(7) == 7.0;");
        Assert.True(result, "conv.toByte(7) == 7.0 must be true per spec Edit 5 case 1.");
    }

    /// <summary>
    /// conv.toByte(0) == 0 returns true (byte ↔ int zero, numeric equivalence class, case 1).
    /// </summary>
    [Fact]
    public void ByteEquality_ByteZeroEqIntZero_IsTrue_PerSpecValuesCoercion()
    {
        var result = (bool?)Run("let result = conv.toByte(0) == 0;");
        Assert.True(result, "conv.toByte(0) == 0 must be true per spec Edit 5 case 1.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Case 3 — String concatenation with +
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "x" + 1 returns "x1" (string on left, int stringified).
    /// </summary>
    [Fact]
    public void StringConcat_StringPlusInt_ReturnsStringified_PerSpecValuesCoercion()
    {
        var result = (string?)Run("let result = \"x\" + 1;");
        Assert.Equal("x1", result);
    }

    /// <summary>
    /// 1 + "x" returns "1x" (string on right, int stringified).
    /// </summary>
    [Fact]
    public void StringConcat_IntPlusString_ReturnsStringified_PerSpecValuesCoercion()
    {
        var result = (string?)Run("let result = 1 + \"x\";");
        Assert.Equal("1x", result);
    }

    /// <summary>
    /// "a" + true returns "atrue" (string + bool stringified).
    /// </summary>
    [Fact]
    public void StringConcat_StringPlusBool_ReturnsStringified_PerSpecValuesCoercion()
    {
        var result = (string?)Run("let result = \"a\" + true;");
        Assert.Equal("atrue", result);
    }

    /// <summary>
    /// "a" + null returns "anull" (string + null stringified).
    /// </summary>
    [Fact]
    public void StringConcat_StringPlusNull_ReturnsStringified_PerSpecValuesCoercion()
    {
        var result = (string?)Run("let result = \"a\" + null;");
        Assert.Equal("anull", result);
    }

    /// <summary>
    /// "a" + [1,2] returns the stringified array form: "a[1, 2]".
    /// Confirmed: io.println("a" + [1,2]) prints "a[1, 2]".
    /// </summary>
    [Fact]
    public void StringConcat_StringPlusArray_ReturnsStringifiedArray_PerSpecValuesCoercion()
    {
        var result = (string?)Run("let result = \"a\" + [1,2];");
        Assert.Equal("a[1, 2]", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Negative space — operand mismatches raise RuntimeError
    // (outside the three enumerated cases, every mismatch is an error)
    // Tests use variable-bound operands to force the runtime path.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "1" == 1 returns false (string not in the numeric equivalence class).
    /// Cross-category string vs int: not coerced, not equal.
    /// </summary>
    [Fact]
    public void NegativeSpace_StringEqInt_IsFalse_PerSpecValuesCoercion()
    {
        var result = (bool?)Run("let result = \"1\" == 1;");
        Assert.False(result, "\"1\" == 1 must be false — string is not in the numeric equivalence class.");
    }

    /// <summary>
    /// null + 1 raises RuntimeError with the operand types in the message.
    /// (null → int promotion is not an enumerated coercion case.)
    /// </summary>
    [Fact]
    public void NegativeSpace_NullPlusInt_RaisesRuntimeError_PerSpecValuesCoercion()
    {
        var err = RunCapturingError("let a = null; let b = 1; let r = a + b;");
        Assert.Contains("'null'", err.Message);
        Assert.Contains("'int'", err.Message);
    }

    /// <summary>
    /// true + 1 raises RuntimeError with the operand types in the message.
    /// (bool → int promotion in arithmetic is not an enumerated coercion case.)
    /// </summary>
    [Fact]
    public void NegativeSpace_BoolPlusInt_RaisesRuntimeError_PerSpecValuesCoercion()
    {
        var err = RunCapturingError("let a = true; let b = 1; let r = a + b;");
        Assert.Contains("'bool'", err.Message);
        Assert.Contains("'int'", err.Message);
    }

    /// <summary>
    /// 1 + [1] raises RuntimeError with the operand types in the message.
    /// (int → array concatenation is not an enumerated coercion case.)
    /// </summary>
    [Fact]
    public void NegativeSpace_IntPlusArray_RaisesRuntimeError_PerSpecValuesCoercion()
    {
        var err = RunCapturingError("let a = 1; let b = [1]; let r = a + b;");
        Assert.Contains("'int'", err.Message);
        Assert.Contains("'array'", err.Message);
    }
}
