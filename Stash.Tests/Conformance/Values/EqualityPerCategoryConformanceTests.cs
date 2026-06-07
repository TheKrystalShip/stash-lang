using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Values;

/// <summary>
/// Conformance tests for §Values and Types — Equality (per-category: identity vs by-value),
/// proving Spec Edit 4 (per-category portion) and D4 forward-looking ratification.
///
/// <para>
/// These tests prove that the Stash implementation honors the normative per-category
/// equality table in <c>docs/Stash — Language Specification.md</c> §Values and Types →
/// Equality → <em>Per-category rule</em> (the table appended in P4):
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Primitive by value</b> — <c>null</c>, <c>bool</c>, <c>string</c> compare by value.
///     <c>"a" == "a"</c> is <c>true</c> for two distinct-construction strings;
///     <c>null == null</c> is <c>true</c>; <c>true == true</c> is <c>true</c>.
///   </item>
///   <item>
///     <b>Other primitive by value</b> — <c>duration</c>, <c>bytes</c>, <c>ip</c>, and
///     <c>semver</c> compare by value. <c>1s == 1000ms</c> is <c>true</c>;
///     <c>1KB == 1024B</c> is <c>true</c>; same-address IPs are <c>true</c>;
///     same-version semvers are <c>true</c>.
///   </item>
///   <item>
///     <b>Reference identity — array and dict</b> — Two distinct <c>[1, 2]</c> literals
///     are <b>not</b> <c>==</c>; two aliased references to the same array <b>are</b>
///     <c>==</c>. Same pair for <c>{}</c> dictionaries.
///   </item>
///   <item>
///     <b>Reference identity — struct</b> — <c>P { x: 1, y: 2 } == P { x: 1, y: 2 }</c>
///     (two distinct constructions) is <c>false</c>; aliased struct handle is <c>true</c>.
///   </item>
///   <item>
///     <b>Reference identity — range</b> — <c>1..3 == 1..3</c> (two distinct constructions)
///     is <c>false</c>; aliased range handle is <c>true</c>.
///   </item>
///   <item>
///     <b>Function and namespace identity</b> — <c>io.println == io.println</c> is
///     <c>true</c> (same registered function handle); <c>io == io</c> is <c>true</c>
///     (same namespace singleton); <c>io == math</c> is <c>false</c>; a user-defined
///     function aliased via <c>let g = ff</c> satisfies <c>ff == g</c> is <c>true</c>;
///     two distinct functions are <c>false</c>.
///   </item>
///   <item>
///     <b>Enum-value identity</b> — <c>Color.Red == Color.Red</c> is <c>true</c>;
///     <c>Color.Red == Color.Green</c> is <c>false</c>; cross-check
///     <c>typeof(Color.Red) == "enum"</c>.
///   </item>
///   <item>
///     <b>Future identity</b> — two distinct <c>task.run</c> handles are not <c>==</c>;
///     an aliased handle is <c>==</c> to itself.
///   </item>
///   <item>
///     <b>Error identity</b> — a caught Error compared to itself (aliased reference)
///     is <c>==</c>; two distinct throw events caught in separate <c>try/catch</c>
///     blocks are <b>not</b> <c>==</c>. Errors are first-class values caught by
///     <c>try/catch</c>; reference identity preserves stack-trace pointers across
///     copies — see the rationale note below.
///   </item>
///   <item>
///     <b>D4 (secret — forward-looking prose only)</b> — The per-category table lists
///     <c>secret</c> in the reference-identity bucket. The code change (flipping
///     <c>StashSecret.Equals</c> to reference identity) lands in P6. No executable
///     secret-equality assertion is written here; P6's <c>SecretConformanceTests</c>
///     owns that surface. The <c>secret("x") == secret("x") is false</c> clause is
///     normative law (sealed in the spec table), forward-proven in P6.
///   </item>
/// </list>
///
/// <para>
/// <b>Rationale: why Error uses reference identity.</b>
/// Errors are first-class values caught by <c>try/catch</c>. A caught error carries
/// a stack-trace pointer that is unique to the throw event. Comparing by reference
/// identity (a) preserves the distinction between two separate exception events even
/// when their messages are identical, (b) aligns with every mainstream language that
/// does not override <c>equals</c> on exception objects by default, and (c) avoids
/// defining a structural equality over error fields (message, type, stack) that would
/// be surprising when two independent throws happen to share the same message string.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class EqualityPerCategoryConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Primitive by value — null, bool, string
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "a" == "a" is true for two distinct-construction strings (by value).
    /// Both literal and variable forms.
    /// </summary>
    [Fact]
    public void String_DistinctConstructions_AreEqual_PerSpecValuesEqualityPerCategory()
    {
        var literal = (bool?)Run(@"let result = ""a"" == ""a"";");
        var variable = (bool?)Run(@"let a = ""a""; let b = ""a""; let result = a == b;");
        Assert.True(literal, "\"a\" == \"a\" (literal) must be true — string is by-value equality.");
        Assert.True(variable, "\"a\" == \"a\" (variable) must be true — string is by-value equality.");
    }

    /// <summary>
    /// null == null is true (null compares by value — there is only one null).
    /// </summary>
    [Fact]
    public void Null_NullEqNull_IsTrue_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let result = null == null;");
        Assert.True(result, "null == null must be true per spec Edit 4 (null is by value).");
    }

    /// <summary>
    /// true == true is true (bool compares by value).
    /// </summary>
    [Fact]
    public void Bool_TrueEqTrue_IsTrue_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let result = true == true;");
        Assert.True(result, "true == true must be true per spec Edit 4 (bool is by value).");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Other primitive by value — duration, bytes, ip, semver
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 1s == 1000ms is true (duration compares by mathematical value, not construction).
    /// </summary>
    [Fact]
    public void Duration_OneSec_EqOneThousandMs_IsTrue_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let result = 1s == 1000ms;");
        Assert.True(result, "1s == 1000ms must be true — duration compares by value per spec Edit 4.");
    }

    /// <summary>
    /// 1KB == 1024B is true (bytes quantity compares by mathematical value).
    /// </summary>
    [Fact]
    public void Bytes_OneKB_Eq1024B_IsTrue_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let result = 1KB == 1024B;");
        Assert.True(result, "1KB == 1024B must be true — bytes compares by value per spec Edit 4.");
    }

    /// <summary>
    /// Two ip literals with the same address are equal (ip compares by value).
    /// </summary>
    [Fact]
    public void Ip_SameAddress_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let ip1 = @127.0.0.1; let ip2 = @127.0.0.1; let result = ip1 == ip2;");
        Assert.True(result, "@127.0.0.1 == @127.0.0.1 must be true — ip compares by value per spec Edit 4.");
    }

    /// <summary>
    /// semver("1.2.3") == semver("1.2.3") is true (semver compares by value, per semver precedence).
    /// </summary>
    [Fact]
    public void Semver_SameVersion_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run(@"let s1 = semver(""1.2.3""); let s2 = semver(""1.2.3""); let result = s1 == s2;");
        Assert.True(result, "semver(\"1.2.3\") == semver(\"1.2.3\") must be true — semver compares by value per spec Edit 4.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reference identity — array
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two distinct [1, 2] literals are NOT == (reference identity: distinct constructions).
    /// </summary>
    [Fact]
    public void Array_DistinctConstructions_AreNotEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let a = [1, 2]; let b = [1, 2]; let result = a == b;");
        Assert.False(result,
            "[1, 2] == [1, 2] (distinct constructions) must be false — array is reference identity per spec Edit 4. " +
            "Use arr.equals for structural comparison.");
    }

    /// <summary>
    /// Two aliased references to the same array ARE == (same value handle).
    /// </summary>
    [Fact]
    public void Array_AliasedReference_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let a = [1, 2]; let b = a; let result = a == b;");
        Assert.True(result,
            "let a = [1, 2]; let b = a; a == b must be true — aliased reference is the same value handle.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reference identity — dict
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two distinct {} literals are NOT == (reference identity: distinct constructions).
    /// </summary>
    [Fact]
    public void Dict_DistinctConstructions_AreNotEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let d1 = {}; let d2 = {}; let result = d1 == d2;");
        Assert.False(result,
            "{} == {} (distinct constructions) must be false — dict is reference identity per spec Edit 4. " +
            "Use dict.equals for structural comparison.");
    }

    /// <summary>
    /// Two aliased references to the same dict ARE == (same value handle).
    /// </summary>
    [Fact]
    public void Dict_AliasedReference_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let d = {}; let e = d; let result = d == e;");
        Assert.True(result,
            "let d = {}; let e = d; d == e must be true — aliased reference is the same value handle.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reference identity — struct
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two distinct struct instances with identical fields are NOT == (reference identity).
    /// P { x: 1, y: 2 } == P { x: 1, y: 2 } is false — distinct constructions.
    /// </summary>
    [Fact]
    public void Struct_DistinctConstructions_AreNotEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run(@"
struct P { x, y }
let p = P { x: 1, y: 2 };
let q = P { x: 1, y: 2 };
let result = p == q;
");
        Assert.False(result,
            "P { x: 1, y: 2 } == P { x: 1, y: 2 } (distinct constructions) must be false — " +
            "struct is reference identity per spec Edit 4.");
    }

    /// <summary>
    /// An aliased struct reference IS == to itself (same value handle).
    /// </summary>
    [Fact]
    public void Struct_AliasedReference_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run(@"
struct P { x, y }
let p = P { x: 1, y: 2 };
let q = p;
let result = p == q;
");
        Assert.True(result,
            "let p = P { x: 1, y: 2 }; let q = p; p == q must be true — aliased reference is the same value handle.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reference identity — range
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 1..3 == 1..3 is false — two distinct range constructions are not equal
    /// (reference identity).
    /// </summary>
    [Fact]
    public void Range_DistinctConstructions_AreNotEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let r1 = 1..3; let r2 = 1..3; let result = r1 == r2;");
        Assert.False(result,
            "1..3 == 1..3 (distinct constructions) must be false — range is reference identity per spec Edit 4.");
    }

    /// <summary>
    /// An aliased range reference IS == to itself (same value handle).
    /// </summary>
    [Fact]
    public void Range_AliasedReference_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let r = 1..3; let s = r; let result = r == s;");
        Assert.True(result,
            "let r = 1..3; let s = r; r == s must be true — aliased reference is the same value handle.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reference identity — function and namespace
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// io.println == io.println is true — the same registered built-in function handle.
    /// </summary>
    [Fact]
    public void Function_BuiltIn_SameHandle_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let result = io.println == io.println;");
        Assert.True(result,
            "io.println == io.println must be true — same registered function handle per spec Edit 4.");
    }

    /// <summary>
    /// io == io is true — the same namespace singleton.
    /// </summary>
    [Fact]
    public void Namespace_SameSingleton_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let result = io == io;");
        Assert.True(result,
            "io == io must be true — same namespace singleton per spec Edit 4.");
    }

    /// <summary>
    /// io == math is false — two different namespace singletons.
    /// </summary>
    [Fact]
    public void Namespace_DifferentSingletons_AreNotEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let result = io == math;");
        Assert.False(result,
            "io == math must be false — different namespace singletons are distinct handles per spec Edit 4.");
    }

    /// <summary>
    /// A user-defined function aliased via let g = ff satisfies ff == g (true).
    /// </summary>
    [Fact]
    public void Function_UserDefined_AliasedReference_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("fn ff() { return 0; } let g = ff; let result = ff == g;");
        Assert.True(result,
            "fn ff() {...}; let g = ff; ff == g must be true — aliased user-defined function is the same handle.");
    }

    /// <summary>
    /// Two distinct user-defined functions are NOT == (reference identity).
    /// </summary>
    [Fact]
    public void Function_UserDefined_DistinctFunctions_AreNotEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("fn f1() { return 0; } fn f2() { return 0; } let result = f1 == f2;");
        Assert.False(result,
            "fn f1(){} and fn f2(){} are distinct function handles — f1 == f2 must be false per spec Edit 4.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reference identity — future
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two distinct task.run handles are NOT == (reference identity).
    /// </summary>
    [Fact]
    public void Future_DistinctHandles_AreNotEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let f1 = task.run(() => 1); let f2 = task.run(() => 2); let result = f1 == f2;");
        Assert.False(result,
            "Two distinct task.run handles must not be == — future is reference identity per spec Edit 4.");
    }

    /// <summary>
    /// An aliased future handle IS == to itself.
    /// </summary>
    [Fact]
    public void Future_AliasedHandle_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run("let f = task.run(() => 1); let g = f; let result = f == g;");
        Assert.True(result,
            "let f = task.run(() => 1); let g = f; f == g must be true — aliased future is the same handle.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reference identity — enum
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Color.Red == Color.Red is true (same enum value handle).
    /// </summary>
    [Fact]
    public void Enum_SameValue_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run(@"
enum Color { Red, Green, Blue }
let result = Color.Red == Color.Red;
");
        Assert.True(result,
            "Color.Red == Color.Red must be true — same enum value handle per spec Edit 4.");
    }

    /// <summary>
    /// Color.Red == Color.Green is false (distinct enum values).
    /// </summary>
    [Fact]
    public void Enum_DifferentValues_AreNotEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run(@"
enum Color { Red, Green, Blue }
let result = Color.Red == Color.Green;
");
        Assert.False(result,
            "Color.Red == Color.Green must be false — distinct enum values are distinct handles per spec Edit 4.");
    }

    /// <summary>
    /// typeof(Color.Red) == "enum" — cross-check that enum values have the expected typeof string.
    /// </summary>
    [Fact]
    public void Enum_TypeOf_IsEnum_PerSpecValuesEqualityPerCategory()
    {
        var result = (string?)Run(@"
enum Color { Red, Green, Blue }
let result = typeof(Color.Red);
");
        Assert.Equal("enum", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reference identity — Error
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A caught Error compared to itself (aliased reference) IS == (same value handle).
    /// Errors are first-class values; their reference identity is preserved.
    /// </summary>
    [Fact]
    public void Error_AliasedReference_IsEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run(@"
let e = null;
try {
    throw ""oops"";
} catch (err) {
    e = err;
}
let result = e == e;
");
        Assert.True(result,
            "e == e for a caught Error must be true — aliased reference is the same value handle per spec Edit 4.");
    }

    /// <summary>
    /// Two distinct throw events caught in separate try/catch blocks are NOT ==
    /// (distinct error value handles, even when the message is identical).
    /// </summary>
    [Fact]
    public void Error_DistinctThrowEvents_AreNotEqual_PerSpecValuesEqualityPerCategory()
    {
        var result = (bool?)Run(@"
let e1 = null;
let e2 = null;
try {
    throw ""oops"";
} catch (err) {
    e1 = err;
}
try {
    throw ""oops"";
} catch (err) {
    e2 = err;
}
let result = e1 == e2;
");
        Assert.False(result,
            "Two distinct throw events must not be == even with identical messages — " +
            "Error is reference identity per spec Edit 4. Stack-trace pointers are unique per throw event.");
    }
}
