using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Values;

/// <summary>
/// Conformance tests for §Values and Types — Secret Values,
/// proving Spec Edit 6 and the D4 (safety-by-default, user override) ratification.
///
/// <para>
/// D4 (ratified 2026-06-06, user override): secret equality is <b>reference identity</b>.
/// The architect's original recommendation was by-inner-value (document the information-leak
/// window). The user chose safety-by-default: == cannot be used as an oracle for the wrapped
/// value, period. The code change: <c>StashSecret.Equals</c> → <c>ReferenceEquals(this, other)</c>;
/// <c>GetHashCode</c> → <c>RuntimeHelpers.GetHashCode(this)</c> (identity hash).
/// </para>
///
/// <para>
/// These tests cover:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Display conformance</b> — <c>conv.toStr</c>, <c>io.println</c>, string interpolation,
///     and <c>+</c> concatenation all produce <c>"******"</c> for the secret itself; <c>+</c>
///     with a non-secret produces a secret-typed taint result.
///   </item>
///   <item>
///     <b>Idempotent wrap</b> — <c>secret(secret(x))</c> does not nest; the inner value is
///     unwrapped. Under D4, the outer and inner handles are distinct objects (the constructor
///     always allocates a new <c>StashSecret</c>), so <c>outer == inner</c> is <c>false</c>.
///     The non-nesting contract is verified by <c>reveal(outer) == reveal(inner)</c>
///     and <c>typeof(outer) == "secret"</c>.
///   </item>
///   <item>
///     <b>Taint propagation</b> — <c>secret("a") + "b"</c> → <c>secret</c>; <c>"a" + secret("b")</c>
///     → <c>secret</c>; <c>1 + secret("a")</c> → <c>secret</c>; revealed content is the
///     concatenation.
///   </item>
///   <item>
///     <b>Equality — reference identity (D4)</b> — Distinct constructions are <b>not</b> equal;
///     the same handle aliased via <c>let t = secret("x")</c> is equal to itself.
///   </item>
///   <item>
///     <b>Dict-key consequence</b> — The implementation restricts dict keys to
///     <c>string</c>, <c>int</c>, <c>float</c>, and <c>bool</c>; passing a <c>secret</c> as a
///     dict key raises a <c>RuntimeError</c>. Were secrets permitted as dict keys, they would
///     key by identity (spec §Secret Values, "keys by identity" clause).
///   </item>
///   <item>
///     <b>Type and truthiness</b> — <c>typeof(secret("x")) == "secret"</c>;
///     <c>nameof(secret("x")) == "secret"</c>; every secret is truthy regardless of inner value.
///   </item>
///   <item>
///     <b>Reveal conformance</b> — <c>reveal(secret("x")) == "x"</c>;
///     <c>typeof(reveal(secret("x"))) == "string"</c>; <c>reveal</c> on a non-secret raises
///     a <c>RuntimeError</c>.
///   </item>
/// </list>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class SecretConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Display conformance — "******" (six asterisks) in all stringification contexts
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// conv.toStr(secret("abc123")) returns "******" (six asterisks).
    /// Spec Edit 6: every stringification context produces the redacted form.
    /// </summary>
    [Fact]
    public void Display_ConvToStr_ReturnsRedacted_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let t = secret(""abc123""); let result = conv.toStr(t);");
        Assert.Equal("******", result);
    }

    /// <summary>
    /// String interpolation "${secret}" produces "******".
    /// </summary>
    [Fact]
    public void Display_Interpolation_ReturnsRedacted_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let t = secret(""abc123""); let result = ""${t}"";");
        Assert.Equal("******", result);
    }

    /// <summary>
    /// "prefix=${secret}" produces "prefix=******" — the secret portion is redacted in interpolation.
    /// </summary>
    [Fact]
    public void Display_InterpolationWithPrefix_ReturnsRedactedPortion_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let t = secret(""abc123""); let result = ""prefix=${t}"";");
        Assert.Equal("prefix=******", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Taint-propagating +
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "prefix=" + secret("abc123") produces a secret-typed value (taint propagation).
    /// Spec Edit 6: taint-propagating +.
    /// </summary>
    [Fact]
    public void Taint_StringPlusSecret_ProducesSecret_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let t = secret(""abc123""); let result = typeof(""prefix="" + t);");
        Assert.Equal("secret", result);
    }

    /// <summary>
    /// secret("a") + "b" produces a secret-typed value (taint propagation, secret on left).
    /// </summary>
    [Fact]
    public void Taint_SecretPlusString_ProducesSecret_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let t = secret(""a""); let result = typeof(t + ""b"");");
        Assert.Equal("secret", result);
    }

    /// <summary>
    /// 1 + secret("a") produces a secret-typed value (taint propagation with numeric operand).
    /// </summary>
    [Fact]
    public void Taint_IntPlusSecret_ProducesSecret_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let t = secret(""a""); let result = typeof(1 + t);");
        Assert.Equal("secret", result);
    }

    /// <summary>
    /// reveal(secret("a") + "b") == "ab" — the concatenation is the revealed content.
    /// </summary>
    [Fact]
    public void Taint_RevealOfTaintResult_IsConcatenation_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let t = secret(""a""); let result = reveal(t + ""b"");");
        Assert.Equal("ab", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Idempotent wrap — non-nesting contract
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// secret(secret("x")) does not nest: typeof(outer) == "secret".
    /// The idempotent-wrap contract per spec Edit 6: secrets do not nest.
    /// </summary>
    [Fact]
    public void IdempotentWrap_OuterIsSecret_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let inner = secret(""x""); let outer = secret(inner); let result = typeof(outer);");
        Assert.Equal("secret", result);
    }

    /// <summary>
    /// reveal(secret(secret("x"))) == "x" — the inner value is unwrapped, not nested.
    /// Spec Edit 6: idempotent wrap unwraps the inner secret's value.
    /// </summary>
    [Fact]
    public void IdempotentWrap_RevealOfOuter_IsInnerValue_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let inner = secret(""x""); let outer = secret(inner); let result = reveal(outer);");
        Assert.Equal("x", result);
    }

    /// <summary>
    /// Under D4 (reference identity), secret(inner) creates a NEW handle: outer != inner.
    /// The idempotent-wrap is about value non-nesting, NOT handle identity.
    /// Spec Edit 6: "Idempotent wrap. secret(secret(x)) is equivalent to secret(x).
    /// Secrets do not nest; the inner secret's value is unwrapped before wrapping."
    /// The constructor allocates a fresh object, so outer and inner are distinct handles.
    /// </summary>
    [Fact]
    public void IdempotentWrap_OuterAndInner_AreDistinctHandles_PerSpecValuesSecret()
    {
        // D4: reference identity — the new StashSecret() creates a distinct object.
        // outer != inner even though reveal(outer) == reveal(inner).
        var result = (bool?)Run(@"let inner = secret(""x""); let outer = secret(inner); let result = outer == inner;");
        Assert.False(result,
            "secret(inner) allocates a new StashSecret handle; under D4 reference identity, " +
            "outer != inner. Idempotent wrap means value non-nesting, not handle reuse.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Equality — D4 reference identity
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// secret("x") == secret("x") returns false — two distinct constructions are distinct handles.
    /// D4: == cannot be used as an oracle for the wrapped value.
    /// </summary>
    [Fact]
    public void Equality_DistinctConstructions_AreNotEqual_PerSpecValuesSecret()
    {
        var result = (bool?)Run(@"let result = secret(""x"") == secret(""x"");");
        Assert.False(result,
            "secret(\"x\") == secret(\"x\") must be false (distinct constructions are distinct handles). " +
            "D4 ratification: safety-by-default — == cannot be used as an oracle for the wrapped value.");
    }

    /// <summary>
    /// let t = secret("x"); t == t returns true — same handle is equal to itself.
    /// </summary>
    [Fact]
    public void Equality_SameHandle_IsEqual_PerSpecValuesSecret()
    {
        var result = (bool?)Run(@"let t = secret(""x""); let result = t == t;");
        Assert.True(result,
            "let t = secret(\"x\"); t == t must be true — same handle is equal to itself (D4 reference identity).");
    }

    /// <summary>
    /// secret("x") != secret("x") returns true (logical negation of ==).
    /// </summary>
    [Fact]
    public void Equality_DistinctConstructions_NotEqualIsTrue_PerSpecValuesSecret()
    {
        var result = (bool?)Run(@"let result = secret(""x"") != secret(""x"");");
        Assert.True(result,
            "secret(\"x\") != secret(\"x\") must be true — != is the logical negation of ==.");
    }

    /// <summary>
    /// secret("x") == secret("y") returns false — different inner values, different handles, not equal.
    /// </summary>
    [Fact]
    public void Equality_DifferentInnerValues_AreNotEqual_PerSpecValuesSecret()
    {
        var result = (bool?)Run(@"let result = secret(""x"") == secret(""y"");");
        Assert.False(result,
            "secret(\"x\") == secret(\"y\") must be false — distinct constructions are not equal (D4).");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dict-key consequence
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A secret may be used as a dict key; it keys by reference identity (Edit E1 keying half, §Equality).
    /// Two distinct <c>secret("x")</c> constructions are distinct keys; the same handle is the same key.
    /// This is the sealed §Equality / §Secret Values behavior as of Edit E1.
    /// </summary>
    [Fact]
    public void DictKey_SecretAsKey_KeysByReferenceIdentity_PerSpecEqualityE1()
    {
        // Two distinct constructions → distinct keys; second lookup misses
        var result1 = Run(@"let d = {}; d[secret(""x"")] = 1; let result = d[secret(""x"")];");
        Assert.Null(result1);

        // Same handle → same key; lookup hits
        var result2 = Run(@"let t = secret(""x""); let d = {}; d[t] = 1; let result = d[t];");
        Assert.Equal(1L, result2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Type and truthiness
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// typeof(secret("x")) == "secret".
    /// </summary>
    [Fact]
    public void TypeOf_Secret_IsSecret_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let result = typeof(secret(""x""));");
        Assert.Equal("secret", result);
    }

    /// <summary>
    /// nameof(secret("x")) == "secret".
    /// </summary>
    [Fact]
    public void NameOf_Secret_IsSecret_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let result = nameof(secret(""x""));");
        Assert.Equal("secret", result);
    }

    /// <summary>
    /// secret(0) is truthy — every secret is truthy regardless of inner value.
    /// </summary>
    [Fact]
    public void Truthiness_SecretOfZero_IsTruthy_PerSpecValuesSecret()
    {
        var result = (bool?)Run(@"let result = secret(0) ? true : false;");
        Assert.True(result, "secret(0) must be truthy — every secret is truthy regardless of inner value.");
    }

    /// <summary>
    /// secret(null) is truthy.
    /// </summary>
    [Fact]
    public void Truthiness_SecretOfNull_IsTruthy_PerSpecValuesSecret()
    {
        var result = (bool?)Run(@"let result = secret(null) ? true : false;");
        Assert.True(result, "secret(null) must be truthy — every secret is truthy regardless of inner value.");
    }

    /// <summary>
    /// secret("") is truthy (inner empty string does not make the secret falsey).
    /// </summary>
    [Fact]
    public void Truthiness_SecretOfEmptyString_IsTruthy_PerSpecValuesSecret()
    {
        var result = (bool?)Run(@"let result = secret("""") ? true : false;");
        Assert.True(result, "secret(\"\") must be truthy — every secret is truthy regardless of inner value.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reveal conformance
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// reveal(secret("x")) == "x" — the wrapped value is returned.
    /// </summary>
    [Fact]
    public void Reveal_ReturnsWrappedValue_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let result = reveal(secret(""x""));");
        Assert.Equal("x", result);
    }

    /// <summary>
    /// typeof(reveal(secret("x"))) == "string" — the revealed type is the inner value's type.
    /// </summary>
    [Fact]
    public void Reveal_TypeOfRevealed_IsInnerType_PerSpecValuesSecret()
    {
        var result = (string?)Run(@"let result = typeof(reveal(secret(""x"")));");
        Assert.Equal("string", result);
    }

    /// <summary>
    /// reveal on a non-secret raises a RuntimeError.
    /// Spec Edit 6: "reveal requires a secret argument. Passing a non-secret value to reveal
    /// raises a RuntimeError."
    /// </summary>
    [Fact]
    public void Reveal_NonSecret_ThrowsRuntimeError_PerSpecValuesSecret()
    {
        var err = RunCapturingError("reveal(42);");
        Assert.Contains("must be a secret", err.Message);
    }

    /// <summary>
    /// reveal on a plain string raises a RuntimeError.
    /// </summary>
    [Fact]
    public void Reveal_PlainString_ThrowsRuntimeError_PerSpecValuesSecret()
    {
        var err = RunCapturingError(@"reveal(""plain"");");
        Assert.Contains("must be a secret", err.Message);
    }
}
