using System.Collections.Generic;
using System.Linq;
using Stash.Bytecode;
using Stash.Bytecode.Optimization;
using Stash.Stdlib;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Contract tests for <see cref="StdlibRegistry.LiveMemberNames"/> and its interaction
/// with the CSE-ineligibility check in <see cref="LocalValueNumberingPass"/>.
///
/// These tests pin two invariants established in the stdlib-namespace-members Decision Log
/// (2026-05-23, P3):
///
///   1. <strong>Allowlist contract</strong>: <c>LiveMemberNames</c> contains exactly the
///      identifiers listed in the fixture below.  Any future PR that adds a Live-stability
///      member MUST update both <c>StdlibRegistry</c> AND this fixture, forcing explicit
///      review of the CSE deopt blast-radius increase on user-code struct fields that share
///      the same name.
///
///   2. <strong>Trade-off acknowledgement</strong>: Because the CSE-ineligibility check is
///      a pure name match (no namespace-receiver discrimination), a user struct field whose
///      name collides with a Live member is CSE-ineligible today.  This is the known,
///      accepted cost of the current implementation.  The test below pins the behavior so
///      that a future tightening (option 1 in the review finding) produces a deliberate,
///      visible test update rather than a silent regression.
/// </summary>
public class LiveMemberContractTests : BytecodeTestBase
{
    // ─── Fixture ────────────────────────────────────────────────────────────────
    //
    // UPDATE THIS LIST when a new Live-stability member is added to the stdlib.
    // Names are unqualified (just the member name, not "ns.name").
    // Current members: env.cwd → "cwd", log.level → "level".
    //
    private static readonly IReadOnlySet<string> AllowedLiveNames =
        new HashSet<string> { "cwd", "level" };

    // ─── Test 1: Allowlist contract ─────────────────────────────────────────────

    [Fact]
    public void LiveMemberNames_MatchesAllowlist_ExactSet()
    {
        // If this test fails it means a new Live-stability member was added (or one was
        // removed) without updating the AllowedLiveNames fixture above.  Update the
        // fixture AND review the CSE deopt impact on user code before merging.
        var actual = StdlibRegistry.LiveMemberNames;

        // Names in the registry that are NOT in the allowlist (unexpected additions):
        IEnumerable<string> unexpected = actual.Except(AllowedLiveNames).OrderBy(n => n);
        Assert.Empty(unexpected);

        // Names in the allowlist that are NOT in the registry (accidental removals):
        IEnumerable<string> missing = AllowedLiveNames.Except(actual).OrderBy(n => n);
        Assert.Empty(missing);
    }

    // ─── Test 2: Trade-off — user struct field named "level" is CSE-ineligible ──
    //
    // KNOWN LIMITATION: A struct field whose name matches a Live stdlib member name
    // (currently "level" or "cwd") is denied CSE today.  This is correct (no wrong
    // behaviour, no data corruption) but is an over-conservative pessimisation.
    //
    // The test asserts the CURRENT behaviour: the second GetField / GetFieldIC access
    // to `s.level` is NOT rewritten to a Move by LVN.  When option 1 (namespace-receiver
    // discrimination) is implemented, this test should be updated to assert the opposite
    // — two accesses DO collapse — proving the tightening is deliberate and complete.

    [Fact]
    public void LiveMemberNames_UserStructFieldNamed_Level_IsNotCsed()
    {
        // Emit two GetField instructions for field "level" on the same receiver register.
        // The name "level" is in LiveMemberNames, so LVN must assign fresh opaque VNs
        // and NOT rewrite the second access to Move.
        var builder = new ChunkBuilder();
        builder.MaxRegs = 4;

        // r0 = some object (simulated as LoadNull for builder-level testing purposes)
        builder.EmitA(OpCode.LoadNull, 0);

        int levelIdx = builder.AddConstant("level");

        // r1 = r0.level — first access
        builder.EmitABC(OpCode.GetField, 1, 0, (byte)levelIdx);

        // r2 = r0.level — second access; should remain GetField (not Move)
        builder.EmitABC(OpCode.GetField, 2, 0, (byte)levelIdx);

        builder.EmitABC(OpCode.Return, 0, 1, 0);

        var pipeline = new PassPipeline();
        pipeline.Add(new LocalValueNumberingPass());
        pipeline.Run(builder);

        // The second GetField must NOT have been rewritten to Move.
        // Index 0 = LoadNull, 1 = first GetField, 2 = second GetField.
        uint secondAccess = builder.RawCode[2];
        Assert.NotEqual(OpCode.Move, Instruction.GetOp(secondAccess));
        Assert.Equal(OpCode.GetField, Instruction.GetOp(secondAccess));
    }

    // ─── Test 3: Positive control — non-Live field IS CSE'd ─────────────────────
    //
    // A field named "value" (not in LiveMemberNames) should collapse: the second
    // GetField becomes a Move.  This is the baseline proving the LVN pass is active
    // and that only Live-named fields are treated specially.

    [Fact]
    public void NonLiveMemberField_Value_IsCsed_SecondAccessBecomesMove()
    {
        var builder = new ChunkBuilder();
        builder.MaxRegs = 4;

        // r0 = object (LoadNull as placeholder)
        builder.EmitA(OpCode.LoadNull, 0);

        int valueIdx = builder.AddConstant("value");

        // r1 = r0.value — first access
        builder.EmitABC(OpCode.GetField, 1, 0, (byte)valueIdx);

        // r2 = r0.value — second access; should become Move(r2, r1)
        builder.EmitABC(OpCode.GetField, 2, 0, (byte)valueIdx);

        builder.EmitABC(OpCode.Return, 0, 1, 0);

        var pipeline = new PassPipeline();
        pipeline.Add(new LocalValueNumberingPass());
        pipeline.Run(builder);

        // The second GetField MUST have been rewritten to Move.
        uint secondAccess = builder.RawCode[2];
        Assert.Equal(OpCode.Move, Instruction.GetOp(secondAccess));
        Assert.Equal(2, (int)Instruction.GetA(secondAccess));  // dest = r2
        Assert.Equal(1, (int)Instruction.GetB(secondAccess));  // src = r1
    }
}
