using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Values;

/// <summary>
/// Conformance tests for §Values and Types — Truthiness, proving Spec Edit 3
/// (and D1 + D5 ratification).
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clauses of
/// <c>docs/Stash — Language Specification.md</c> §Values and Types → Truthiness, specifically:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Edit 3</b> — The closed falsey set is exactly six values: <c>null</c>,
///     <c>false</c>, integer <c>0</c>, float <c>0.0</c> / <c>-0.0</c>, byte <c>0</c>,
///     empty string <c>""</c>. Every other value is truthy.
///   </item>
///   <item>
///     <b>D1</b> (ratified) — Empty array <c>[]</c> and empty dict <c>{}</c> are truthy.
///     The law is corrected to match the shipped implementation. No code change.
///     The legacy <c>Truthiness_EmptyArrayIsTruthy</c> and
///     <c>Truthiness_StructInstanceIsTruthy</c> tests are moved here as conformance
///     assertions (removed from <c>InterpreterTests.cs</c>).
///   </item>
///   <item>
///     <b>D5</b> (ratified) — Every caught Error value is truthy.
///     <c>try { ... } catch (e) { if (e) { ... } }</c> enters the <c>if</c> body.
///     Code change: <c>StashError.VMIsFalsy</c> flipped from <c>true</c> to <c>false</c>.
///   </item>
/// </list>
///
/// <para>
/// The truthiness rule is consumed uniformly via the single source of truth
/// (<c>RuntimeOps.IsFalsy</c>). The consumer-uniformity tests below assert the rule
/// is reached through every consuming construct: <c>if</c>, <c>while</c>, <c>do while</c>,
/// ternary <c>?:</c>, <c>&amp;&amp;</c>, <c>||</c>, <c>!</c>, and the <c>until</c>
/// predicate in a <c>retry</c> expression.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class TruthinessConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Edit 3 + D1 — Closed falsey set: positive tests (falsey values)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>null is falsey.</summary>
    [Fact]
    public void Null_IsFalsey_PerSpecValuesTruthiness()
    {
        var result = Run("let result = null ? true : false;");
        Assert.Equal(false, result);
    }

    /// <summary>false is falsey.</summary>
    [Fact]
    public void False_IsFalsey_PerSpecValuesTruthiness()
    {
        var result = Run("let result = false ? true : false;");
        Assert.Equal(false, result);
    }

    /// <summary>The integer 0 is falsey.</summary>
    [Fact]
    public void IntegerZero_IsFalsey_PerSpecValuesTruthiness()
    {
        var result = Run("let result = 0 ? true : false;");
        Assert.Equal(false, result);
    }

    /// <summary>The float 0.0 is falsey.</summary>
    [Fact]
    public void FloatZero_IsFalsey_PerSpecValuesTruthiness()
    {
        var result = Run("let result = 0.0 ? true : false;");
        Assert.Equal(false, result);
    }

    /// <summary>The float -0.0 is falsey.</summary>
    [Fact]
    public void FloatNegativeZero_IsFalsey_PerSpecValuesTruthiness()
    {
        var result = Run("let result = -0.0 ? true : false;");
        Assert.Equal(false, result);
    }

    /// <summary>The byte 0 (via conv.toByte) is falsey.</summary>
    [Fact]
    public void ByteZero_IsFalsey_PerSpecValuesTruthiness()
    {
        var result = Run("let result = conv.toByte(0) ? true : false;");
        Assert.Equal(false, result);
    }

    /// <summary>The empty string "" is falsey.</summary>
    [Fact]
    public void EmptyString_IsFalsey_PerSpecValuesTruthiness()
    {
        var result = Run(@"let result = """" ? true : false;");
        Assert.Equal(false, result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 3 + D1 — D1 ratification: empty collections are truthy
    // (legacy tests Truthiness_EmptyArrayIsTruthy + Truthiness_StructInstanceIsTruthy
    //  moved here from InterpreterTests.cs)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Empty array [] is truthy (D1 ratification — law corrected to match impl).
    /// </summary>
    [Fact]
    public void EmptyArray_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("let result = [] ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>
    /// Empty dict {} is truthy (D1 ratification — law corrected to match impl).
    /// </summary>
    [Fact]
    public void EmptyDict_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("let result = {} ? true : false;");
        Assert.Equal(true, result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 3 — Negative space: non-empty and opaque values are truthy
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A struct instance is truthy regardless of its fields.
    /// (Moved from Truthiness_StructInstanceIsTruthy in InterpreterTests.cs.)
    /// </summary>
    [Fact]
    public void StructInstance_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("struct S { x } let s = S { x: 1 }; let result = s ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>An enum value is truthy.</summary>
    [Fact]
    public void EnumValue_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("enum Color { Red, Green } let result = Color.Red ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>A function reference is truthy.</summary>
    [Fact]
    public void FunctionReference_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("let result = io.println ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>A namespace value is truthy.</summary>
    [Fact]
    public void Namespace_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("let result = io ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>A Future value is truthy.</summary>
    [Fact]
    public void Future_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("let f = task.run(() => 1); let result = f ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>A range value is truthy (even 0..0).</summary>
    [Fact]
    public void Range_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("let result = (0..0) ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>A secret value is truthy regardless of wrapped value.</summary>
    [Fact]
    public void Secret_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run(@"let result = secret(""x"") ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>A duration value (1s) is truthy.</summary>
    [Fact]
    public void Duration_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("let result = 1s ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>A bytes value (1KB) is truthy.</summary>
    [Fact]
    public void Bytes_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("let result = 1KB ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>A semver value is truthy.</summary>
    [Fact]
    public void Semver_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run(@"let result = semver(""1.2.3"") ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>NaN (float) is truthy. Use value != value to detect NaN.</summary>
    [Fact]
    public void NaN_IsTruthy_PerSpecValuesTruthiness()
    {
        // NaN is reachable only via overflow arithmetic per the spec:
        // conv.toFloat("1e308") * 10.0 produces Infinity; Infinity - Infinity produces NaN.
        var result = Run(@"
let big = conv.toFloat(""1e308"") * 10.0;
let nan = big - big;
let result = nan ? true : false;
");
        Assert.Equal(true, result);
    }

    /// <summary>A single-space string " " is truthy.</summary>
    [Fact]
    public void SingleSpaceString_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run(@"let result = "" "" ? true : false;");
        Assert.Equal(true, result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 3 + D5 — Caught Error values are truthy
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A caught Error (thrown string → RuntimeError) is truthy.
    /// D5 ratification: StashError.VMIsFalsy flipped from true to false.
    /// try { throw "x"; } catch (e) { if (e) { ... } } enters the if body.
    /// </summary>
    [Fact]
    public void CaughtError_ThrowString_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run(@"
let r = ""falsey"";
try {
    throw ""x"";
} catch (e) {
    r = e ? ""truthy"" : ""falsey"";
}
let result = r;
");
        Assert.Equal("truthy", result);
    }

    /// <summary>
    /// A caught TypeError is truthy (named built-in error type).
    /// typeof(e) == "Error" for any caught error.
    /// </summary>
    [Fact]
    public void CaughtError_TypeError_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run(@"
let r = ""falsey"";
try {
    throw TypeError(""bad"");
} catch (e) {
    r = e ? ""truthy"" : ""falsey"";
}
let result = r;
");
        Assert.Equal("truthy", result);
    }

    /// <summary>
    /// typeof(e) == "Error" for a caught string-thrown error.
    /// </summary>
    [Fact]
    public void CaughtError_TypeofIsError_PerSpecValuesTruthiness()
    {
        var result = Run(@"
let t = """";
try {
    throw ""x"";
} catch (e) {
    t = typeof(e);
}
let result = t;
");
        Assert.Equal("Error", result);
    }

    /// <summary>
    /// typeof(e) == "Error" for a caught named error type (TypeError).
    /// </summary>
    [Fact]
    public void CaughtError_NamedType_TypeofIsError_PerSpecValuesTruthiness()
    {
        var result = Run(@"
let t = """";
try {
    throw TypeError(""bad"");
} catch (e) {
    t = typeof(e);
}
let result = t;
");
        Assert.Equal("Error", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 3 — Consumer uniformity: same rule through every consuming construct
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Truthiness via if statement — falsey case (null).</summary>
    [Fact]
    public void Consumer_If_Null_IsFalsey_PerSpecValuesTruthiness()
    {
        var result = Run(@"
let result = ""truthy"";
if (null) {
    result = ""entered"";
}
");
        Assert.Equal("truthy", result);
    }

    /// <summary>Truthiness via if statement — truthy case (1).</summary>
    [Fact]
    public void Consumer_If_IntOne_IsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run(@"
let result = ""not entered"";
if (1) {
    result = ""entered"";
}
");
        Assert.Equal("entered", result);
    }

    /// <summary>Truthiness via while — loop exits when counter reaches 0 (falsey).</summary>
    [Fact]
    public void Consumer_While_ExitsWhenFalsey_PerSpecValuesTruthiness()
    {
        var result = Run(@"
let counter = 3;
let sum = 0;
while (counter) {
    sum = sum + counter;
    counter = counter - 1;
}
let result = sum;
");
        Assert.Equal(6L, result);
    }

    /// <summary>Truthiness via do while — body executes at least once then exits when falsey.</summary>
    [Fact]
    public void Consumer_DoWhile_ExitsWhenFalsey_PerSpecValuesTruthiness()
    {
        var result = Run(@"
let ran = 0;
do {
    ran = ran + 1;
} while (false);
let result = ran;
");
        Assert.Equal(1L, result);
    }

    /// <summary>Truthiness via ternary — falsey branch chosen for null.</summary>
    [Fact]
    public void Consumer_Ternary_FalseyBranch_PerSpecValuesTruthiness()
    {
        var result = Run("let result = null ? \"yes\" : \"no\";");
        Assert.Equal("no", result);
    }

    /// <summary>Truthiness via ternary — truthy branch chosen for 1.</summary>
    [Fact]
    public void Consumer_Ternary_TruthyBranch_PerSpecValuesTruthiness()
    {
        var result = Run("let result = 1 ? \"yes\" : \"no\";");
        Assert.Equal("yes", result);
    }

    /// <summary>Truthiness via logical AND (&amp;&amp;) — short-circuits on falsey left.</summary>
    [Fact]
    public void Consumer_LogicalAnd_ShortCircuitsOnFalsey_PerSpecValuesTruthiness()
    {
        var result = Run("let result = null && true;");
        Assert.Null(result);
    }

    /// <summary>Truthiness via logical OR (||) — short-circuits on truthy left.</summary>
    [Fact]
    public void Consumer_LogicalOr_ShortCircuitsOnTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("let result = 1 || false;");
        Assert.Equal(1L, result);
    }

    /// <summary>Truthiness via logical NOT (!) — inverts falsey to true.</summary>
    [Fact]
    public void Consumer_LogicalNot_InvertsFalsey_PerSpecValuesTruthiness()
    {
        var result = Run("let result = !null;");
        Assert.Equal(true, result);
    }

    /// <summary>Truthiness via logical NOT (!) — inverts truthy to false.</summary>
    [Fact]
    public void Consumer_LogicalNot_InvertsTruthy_PerSpecValuesTruthiness()
    {
        var result = Run("let result = !1;");
        Assert.Equal(false, result);
    }

    /// <summary>
    /// Truthiness via retry until predicate — the until predicate uses the same
    /// truthiness rule. Stops when the returned counter reaches 3 (truthy via >= 3).
    /// </summary>
    [Fact]
    public void Consumer_RetryUntilPredicate_StopsWhenTruthy_PerSpecValuesTruthiness()
    {
        var result = Run(@"
let counter = 0;
let result = retry (5) until (r) => r >= 3 {
    counter = counter + 1;
    return counter;
};
");
        Assert.Equal(3L, result);
    }
}
