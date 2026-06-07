using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Values;

/// <summary>
/// Conformance tests for §Values and Types — Type Model, <c>typeof</c>, <c>nameof</c>,
/// and §Ranges, proving Spec Edits 1, 2, and 7 (and D3 ratification).
///
/// <para>
/// These tests prove that the Stash implementation honors the normative clauses of
/// <c>docs/Stash — Language Specification.md</c> §Values and Types (L570–L664), specifically:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Edit 1</b> — The type table is extended with a <c>range</c> row and corrected from
///     <c>bytesize</c> to <c>bytes</c>. The closed-set sentence declares the list normative.
///   </item>
///   <item>
///     <b>Edit 2</b> — <c>typeof</c> is total (never raises), covers the closed vocabulary,
///     and the aggregate/opaque/user-defined cases (struct, enum, Future, Error, typed array)
///     are explicitly pinned.
///   </item>
///   <item>
///     <b>Edit 7</b> — A <c>range</c> is a first-class value with <c>typeof</c> string
///     <c>"range"</c>, reference-identity equality, and unconditional truthiness.
///   </item>
///   <item>
///     <b>D3</b> (ratified) — The runtime returns <c>"bytes"</c> for byte-quantity values;
///     the spec table is the side that was corrected (code unchanged).
///     <c>typeof(1KB) == "bytes"</c>.
///   </item>
/// </list>
///
/// <para>
/// This class is the <em>vocabulary registry</em> test: every distinct <c>typeof</c> string
/// the spec table names is asserted here. A new runtime category without a matching clause
/// in the spec table is caught by Review Priority 2.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class TypeModelConformanceTests : StashTestBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Edit 1 + Edit 2 — typeof vocabulary: primitive categories
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>typeof(0) == "int"</summary>
    [Fact]
    public void TypeOf_IntLiteral_ReturnsInt_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(0);");
        Assert.Equal("int", result);
    }

    /// <summary>typeof(0.0) == "float"</summary>
    [Fact]
    public void TypeOf_FloatLiteral_ReturnsFloat_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(0.0);");
        Assert.Equal("float", result);
    }

    /// <summary>typeof(true) == "bool"</summary>
    [Fact]
    public void TypeOf_BoolLiteral_ReturnsBool_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(true);");
        Assert.Equal("bool", result);
    }

    /// <summary>typeof(null) == "null" — totality: does not raise for null.</summary>
    [Fact]
    public void TypeOf_Null_ReturnsNull_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(null);");
        Assert.Equal("null", result);
    }

    /// <summary>typeof("") == "string"</summary>
    [Fact]
    public void TypeOf_StringLiteral_ReturnsString_PerSpecValuesTypeModel()
    {
        var result = Run(@"let result = typeof("""");");
        Assert.Equal("string", result);
    }

    /// <summary>typeof([]) == "array"</summary>
    [Fact]
    public void TypeOf_EmptyArray_ReturnsArray_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof([]);");
        Assert.Equal("array", result);
    }

    /// <summary>typeof({}) == "dict"</summary>
    [Fact]
    public void TypeOf_EmptyDict_ReturnsDict_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof({});");
        Assert.Equal("dict", result);
    }

    /// <summary>typeof(1..3) == "range" — Edit 1 (range added to table) + Edit 7.</summary>
    [Fact]
    public void TypeOf_RangeLiteral_ReturnsRange_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(1..3);");
        Assert.Equal("range", result);
    }

    /// <summary>typeof(1s) == "duration"</summary>
    [Fact]
    public void TypeOf_DurationLiteral_ReturnsDuration_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(1s);");
        Assert.Equal("duration", result);
    }

    /// <summary>
    /// typeof(1KB) == "bytes" — D3 ratified: the runtime string "bytes" is law;
    /// the spec table was corrected from "bytesize" to "bytes". No code change.
    /// </summary>
    [Fact]
    public void TypeOf_ByteQuantity_ReturnsBytesNotBytesize_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(1KB);");
        Assert.Equal("bytes", result);
    }

    /// <summary>typeof(io) == "namespace"</summary>
    [Fact]
    public void TypeOf_Namespace_ReturnsNamespace_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(io);");
        Assert.Equal("namespace", result);
    }

    /// <summary>typeof(io.println) == "function"</summary>
    [Fact]
    public void TypeOf_BuiltInFunction_ReturnsFunction_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(io.println);");
        Assert.Equal("function", result);
    }

    /// <summary>typeof(conv.toByte(7)) == "byte"</summary>
    [Fact]
    public void TypeOf_Byte_ReturnsByte_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(conv.toByte(7));");
        Assert.Equal("byte", result);
    }

    /// <summary>typeof(semver("1.2.3")) == "semver"</summary>
    [Fact]
    public void TypeOf_SemVer_ReturnsSemver_PerSpecValuesTypeModel()
    {
        var result = Run(@"let result = typeof(semver(""1.2.3""));");
        Assert.Equal("semver", result);
    }

    /// <summary>typeof(secret("x")) == "secret"</summary>
    [Fact]
    public void TypeOf_Secret_ReturnsSecret_PerSpecValuesTypeModel()
    {
        var result = Run(@"let result = typeof(secret(""x""));");
        Assert.Equal("secret", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 2 — typeof for aggregate, opaque, and user-defined values
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// typeof(struct_instance) == "struct"; nameof(struct_instance) returns the
    /// user-visible struct name.
    /// </summary>
    [Fact]
    public void TypeOf_StructInstance_ReturnsStruct_PerSpecValuesTypeModel()
    {
        var result = Run(@"
struct P { x }
let p = P { x: 1 };
let result = typeof(p);
");
        Assert.Equal("struct", result);
    }

    /// <summary>
    /// nameof(struct_instance) returns the user-visible struct name (e.g. "P").
    /// </summary>
    [Fact]
    public void Nameof_StructInstance_ReturnsStructName_PerSpecValuesTypeModel()
    {
        var result = Run(@"
struct P { x }
let p = P { x: 1 };
let result = nameof(p);
");
        Assert.Equal("P", result);
    }

    /// <summary>
    /// typeof(enum_value) == "enum". Both the enum type identifier and an enum member
    /// value return "enum".
    /// </summary>
    [Fact]
    public void TypeOf_EnumValue_ReturnsEnum_PerSpecValuesTypeModel()
    {
        var result = Run(@"
enum Color { Red, Green, Blue }
let result = typeof(Color.Red);
");
        Assert.Equal("enum", result);
    }

    /// <summary>
    /// typeof(future) == "Future" (capitalized; the capitalization is normative).
    /// </summary>
    [Fact]
    public void TypeOf_Future_ReturnsFutureCapitalized_PerSpecValuesTypeModel()
    {
        var result = Run(@"
let f = task.run(() => 1);
let result = typeof(f);
");
        Assert.Equal("Future", result);
    }

    /// <summary>
    /// typeof(thrown_error_in_catch) == "Error" for any first-class Error value
    /// caught by try/catch.
    /// </summary>
    [Fact]
    public void TypeOf_CaughtError_ReturnsError_PerSpecValuesTypeModel()
    {
        var result = Run(@"
let t = """";
try {
    throw ""oops"";
} catch (e) {
    t = typeof(e);
}
let result = t;
");
        Assert.Equal("Error", result);
    }

    /// <summary>
    /// typeof(byte[]) returns "byte[]" — the element-type string suffixed with "[]".
    /// Constructed via buf.from which produces the runtime's typed-array path.
    /// </summary>
    [Fact]
    public void TypeOf_ByteTypedArray_ReturnsByteArray_PerSpecValuesTypeModel()
    {
        var result = Run(@"let result = typeof(buf.from(""Hello""));");
        Assert.Equal("byte[]", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 2 — typeof totality: never raises
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// typeof(null) == "null" does not raise — typeof is total for every value
    /// including null.
    /// </summary>
    [Fact]
    public void TypeOf_Null_IsTotalDoesNotRaise_PerSpecValuesTypeModel()
    {
        // If typeof raised for null, Run would throw and the test would fail.
        var result = Run("let result = typeof(null);");
        Assert.Equal("null", result);
    }

    /// <summary>
    /// typeof(NaN) == "float" — NaN is a float value. NaN is reachable only via
    /// overflow arithmetic: conv.toFloat("1e308") * 10.0 produces Infinity;
    /// Infinity - Infinity produces NaN.
    /// </summary>
    [Fact]
    public void TypeOf_NaN_ReturnsFloat_PerSpecValuesTypeModel()
    {
        var result = Run(@"
let big = conv.toFloat(""1e308"") * 10.0;
let n = big - big;
let result = typeof(n);
");
        Assert.Equal("float", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 2 — typeof vocabulary: ip and interface (closed-set completeness)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// typeof(@127.0.0.1) == "ip" — the ip literal category is part of the
    /// closed typeof vocabulary (spec table §Values and Types L582-605).
    /// </summary>
    [Fact]
    public void TypeOf_IpLiteral_ReturnsIp_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(@127.0.0.1);");
        Assert.Equal("ip", result);
    }

    /// <summary>
    /// typeof(Runnable) == "interface" — the interface type identifier returns
    /// "interface" from typeof (spec table row; type identifier case).
    /// </summary>
    [Fact]
    public void TypeOf_InterfaceType_ReturnsInterface_PerSpecValuesTypeModel()
    {
        var result = Run("interface Runnable { fn run() } let result = typeof(Runnable);");
        Assert.Equal("interface", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 2 — typeof for type identifiers: struct and enum (L634-636)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// typeof(P) == "struct" — the struct type identifier (not an instance) returns
    /// "struct"; the type and its instance are indistinguishable by typeof
    /// (spec §Values and Types L634-636).
    /// </summary>
    [Fact]
    public void TypeOf_StructTypeIdentifier_ReturnsStruct_PerSpecValuesTypeModel()
    {
        var result = Run("struct P { x } let result = typeof(P);");
        Assert.Equal("struct", result);
    }

    /// <summary>
    /// typeof(Color) == "enum" — the enum type identifier returns "enum";
    /// the type and its instance are indistinguishable by typeof
    /// (spec §Values and Types L634-636).
    /// </summary>
    [Fact]
    public void TypeOf_EnumTypeIdentifier_ReturnsEnum_PerSpecValuesTypeModel()
    {
        var result = Run("enum Color { Red } let result = typeof(Color);");
        Assert.Equal("enum", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 2 — nameof(enum_value) returns fully-qualified member name (option B)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// nameof(Color.Red) == "Color.Red" — the fully-qualified member name,
    /// NOT the declaring type name "Color". This is the sealed option-B behavior
    /// (runtime authoritative): spec §Values and Types L632-637 corrected to
    /// document the qualified-path return value. See backlog bug
    /// nameof-enum-value-returns-qualified-name-not-type-name.md for the
    /// eventual follow-up (changing to the bare member name "Red").
    /// </summary>
    [Fact]
    public void Nameof_EnumValue_ReturnsQualifiedMemberName_PerSpecValuesTypeModel()
    {
        var result = Run("enum Color { Red } let result = nameof(Color.Red);");
        Assert.Equal("Color.Red", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edit 7 + Edit 1 — Range as a first-class value
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// typeof(1..3) == "range" — range has a designated typeof string.
    /// </summary>
    [Fact]
    public void Range_TypeOf_ReturnsRange_PerSpecValuesTypeModel()
    {
        var result = Run("let result = typeof(1..3);");
        Assert.Equal("range", result);
    }

    /// <summary>
    /// A range is always truthy — even 0..0 (an empty range).
    /// (1..3) ? true : false evaluates to true.
    /// </summary>
    [Fact]
    public void Range_IsAlwaysTruthy_PerSpecValuesTypeModel()
    {
        var result = Run("let result = (1..3) ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>
    /// An empty range (0..0) is also truthy.
    /// </summary>
    [Fact]
    public void Range_EmptyRange_IsAlwaysTruthy_PerSpecValuesTypeModel()
    {
        var result = Run("let result = (0..0) ? true : false;");
        Assert.Equal(true, result);
    }

    /// <summary>
    /// Two distinct range constructions are not == (reference identity).
    /// 1..3 == 1..3 is false because they are separate value handles.
    /// </summary>
    [Fact]
    public void Range_TwoDistinctConstructions_AreNotEqual_PerSpecValuesTypeModel()
    {
        var result = Run(@"
let r1 = 1..3;
let r2 = 1..3;
let result = r1 == r2;
");
        Assert.Equal(false, result);
    }
}
