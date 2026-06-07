using Stash.Runtime;
using Stash.Runtime.Protocols;
using Stash.Runtime.Types;
using Stash.Tests.Interpreting;

namespace Stash.Tests.Conformance.Equality;

/// <summary>
/// Conformance tests for the P1 chokepoint contract of §Equality.
///
/// <para>
/// This class proves two invariants that are observable from language-level behavior:
/// </para>
///
/// <list type="number">
///   <item>
///     <b>Chokepoint coverage cross-check (OperatorEquals routing).</b>
///     The <c>==</c> operator routes through <see cref="StashEquality.OperatorEquals"/> —
///     observable because the literal form and the variable form of any cross-type numeric
///     <c>==</c> must return the same boolean (DE1 regression guard). A divergence between
///     the two forms indicates the constant folder and the runtime chokepoint have split.
///   </item>
///   <item>
///     <b><see cref="IVMEquatable.VMEquals"/> precedence preserved.</b>
///     User-defined struct/enum <c>==</c> overloads dispatch to the user method and are
///     not overridden by the chokepoint. The chokepoint must not regress user-defined
///     equality.
///   </item>
/// </list>
///
/// <para>
/// Both invariants must hold after every migration phase (P2–P6). This class is the
/// permanent regression guard for the P1 chokepoint installation.
/// </para>
/// </summary>
[Trait("Category", "Conformance")]
public sealed class OperatorChokepointConformanceTests : StashTestBase
{
    // ── Chokepoint coverage cross-check (literal-vs-variable pair pattern) ────

    /// <summary>
    /// <c>1 == 1.0</c> via literal (constant-folder path) and via variable (runtime chokepoint path)
    /// must return the same <c>true</c>. A divergence is a regression of the chokepoint installation.
    /// </summary>
    [Fact]
    public void OperatorChokepoint_IntEqFloat_LiteralAndVariableFormAgree_PerSpecEquality()
    {
        var literalResult   = (bool?)Run("let result = 1 == 1.0;");
        var variableResult  = (bool?)Run("let i = 1; let f = 1.0; let result = i == f;");

        Assert.True(
            literalResult == variableResult,
            $"Constant folder diverged from runtime chokepoint path — chokepoint regression.\n" +
            $"  Literal form result:  {literalResult}\n" +
            $"  Variable form result: {variableResult}\n" +
            $"  Both must be true (DE1 / D2 numeric-coercion rule).");

        Assert.True(literalResult,
            "1 == 1.0 must be true (DE1 — int/float numeric-coercion rule, operator routes through StashEquality.OperatorEquals).");
    }

    /// <summary>
    /// <c>NaN != NaN</c> via variable (runtime chokepoint). NaN is not self-equal under
    /// <see cref="StashEquality.OperatorEquals"/> (IEEE 754, DE1).
    /// </summary>
    [Fact]
    public void OperatorChokepoint_NaN_NotSelfEqual_PerSpecEquality()
    {
        var result = (bool?)Run(
            "let big = conv.toFloat(\"1e308\") * 10.0; let nan = big - big; let result = nan == nan;");
        Assert.False(result,
            "NaN == NaN must be false via the operator chokepoint (DE1 — IEEE 754, OperatorEquals passes nanSelfEqual:false).");
    }

    /// <summary>
    /// <c>+0.0 == -0.0</c> is <c>true</c> via variable (runtime chokepoint). ±0 are unified
    /// under <see cref="StashEquality.OperatorEquals"/> (IEEE 754, DE1).
    /// </summary>
    [Fact]
    public void OperatorChokepoint_PositiveZeroEqNegativeZero_IsTrue_PerSpecEquality()
    {
        var literalResult  = (bool?)Run("let result = 0.0 == -0.0;");
        var variableResult = (bool?)Run("let pz = 0.0; let nz = -0.0; let result = pz == nz;");

        Assert.True(
            literalResult == variableResult,
            $"Literal/variable divergence on ±0 comparison — chokepoint regression.\n" +
            $"  Literal: {literalResult}, Variable: {variableResult}");

        Assert.True(literalResult,
            "0.0 == -0.0 must be true (DE1 — IEEE 754 ±0 unification, OperatorEquals).");
    }

    // ── IVMEquatable.VMEquals precedence preserved ────────────────────────────

    /// <summary>
    /// A user enum's <c>==</c> still dispatches to <see cref="IVMEquatable.VMEquals"/>.
    /// The chokepoint must not override user-defined equality: <c>S.A == S.A</c> is <c>true</c>
    /// and <c>S.A == S.B</c> is <c>false</c> per the enum's value-equality definition.
    /// </summary>
    [Fact]
    public void IVMEquatable_EnumSameValue_EqIsTrue_PreservesVMEquals_PerSpecEquality()
    {
        var result = (bool?)Run("enum S { A, B } let result = S.A == S.A;");
        Assert.True(result,
            "S.A == S.A must be true — enum == dispatches to IVMEquatable.VMEquals which checks type+member equality. " +
            "The chokepoint must not regress user-defined equality.");
    }

    /// <summary>
    /// A user enum's <c>==</c> returns <c>false</c> when values differ — proves
    /// <see cref="IVMEquatable.VMEquals"/> is invoked (not the chokepoint's numeric path).
    /// </summary>
    [Fact]
    public void IVMEquatable_EnumDifferentValue_EqIsFalse_PreservesVMEquals_PerSpecEquality()
    {
        var result = (bool?)Run("enum S { A, B } let result = S.A == S.B;");
        Assert.False(result,
            "S.A == S.B must be false — IVMEquatable.VMEquals compares type+member equality; different members are not equal.");
    }

    /// <summary>
    /// A user enum's <c>==</c> returns <c>false</c> across different enum types —
    /// proves <see cref="IVMEquatable.VMEquals"/> checks the type name, not just member name.
    /// </summary>
    [Fact]
    public void IVMEquatable_EnumDifferentType_EqIsFalse_PreservesVMEquals_PerSpecEquality()
    {
        var result = (bool?)Run("enum S { A } enum T { A } let result = S.A == T.A;");
        Assert.False(result,
            "S.A == T.A must be false (different enum types) — IVMEquatable.VMEquals checks type name equality.");
    }

    // ── Chokepoint direct API cross-check (C# level) ─────────────────────────

    /// <summary>
    /// Directly verify that <see cref="StashEquality.OperatorEquals"/> honors DE1 at the
    /// C# API level: int 1 and float 1.0 are equal, NaN is not self-equal, ±0 is unified.
    /// This is the binding-level proof that the chokepoint module exists and is correct.
    /// </summary>
    [Fact]
    public void StashEqualityOperatorEquals_DirectApi_HonorsDE1_PerSpecEquality()
    {
        var int1   = StashValue.FromInt(1L);
        var float1 = StashValue.FromFloat(1.0);
        var pos0   = StashValue.FromFloat(0.0);
        var neg0   = StashValue.FromFloat(-0.0);
        var nan    = StashValue.FromFloat(double.NaN);

        Assert.True(StashEquality.OperatorEquals(int1, float1),
            "StashEquality.OperatorEquals(1, 1.0) must be true (int/float numeric coercion).");
        Assert.False(StashEquality.OperatorEquals(nan, nan),
            "StashEquality.OperatorEquals(NaN, NaN) must be false (IEEE 754 — nanSelfEqual:false).");
        Assert.True(StashEquality.OperatorEquals(pos0, neg0),
            "StashEquality.OperatorEquals(+0.0, -0.0) must be true (IEEE 754 ±0 unification).");
    }

    /// <summary>
    /// Directly verify that <see cref="StashEquality.SameValueZeroEquals"/> has NaN self-equal
    /// at the C# API level — the key delta from <see cref="StashEquality.OperatorEquals"/>.
    /// </summary>
    [Fact]
    public void StashEqualitySameValueZero_DirectApi_NanSelfEqual_PerSpecEquality()
    {
        var nan = StashValue.FromFloat(double.NaN);
        Assert.True(StashEquality.SameValueZeroEquals(nan, nan),
            "StashEquality.SameValueZeroEquals(NaN, NaN) must be true (nanSelfEqual:true for collections).");
    }

    /// <summary>
    /// Directly verify that <see cref="StashEquality.StrictEquals"/> is tag-strict:
    /// int 1 and float 1.0 are NOT equal under StrictEquals even though they are under OperatorEquals.
    /// </summary>
    [Fact]
    public void StashEqualityStrictEquals_DirectApi_TagStrict_IntNeqFloat_PerSpecEquality()
    {
        var int1   = StashValue.FromInt(1L);
        var float1 = StashValue.FromFloat(1.0);
        Assert.False(StashEquality.StrictEquals(int1, float1),
            "StashEquality.StrictEquals(1, 1.0) must be false (DE3 — tag-strict; different typeof).");
    }
}
