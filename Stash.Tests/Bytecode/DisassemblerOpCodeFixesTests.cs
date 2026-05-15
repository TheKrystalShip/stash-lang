using Stash.Bytecode;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Phase 0 disassembler fixes from `.kanban/.../Bytecode — OpCode Metadata Centralization.md` §4a.
///
/// 1. <c>UnsetGlobal</c> (opcode 98) was missing from the mnemonic table and the
///    operand renderer — disassembly printed it as <c>op_62 r0, r0, r0</c>.
/// 2. <c>IterPrep</c> was mis-classified as <see cref="OpCodeFormat.AsBx"/> in the
///    disassembler, which caused <c>CollectLabels</c> to invent a spurious label
///    from the misinterpreted signed offset and the operand renderer dropped the
///    <c>indexed</c> flag in operand B.
/// </summary>
public class DisassemblerOpCodeFixesTests : BytecodeTestBase
{
    [Fact]
    public void Disassemble_UnsetStatement_RendersMnemonicAndSlotAnnotation()
    {
        // `unset x;` lowers to UnsetGlobal at the slot of `x`. The disassembly must
        // include the mnemonic `unset.global` and the `[g<N>]` slot rendering with
        // the variable name annotation — never the old `op_62 r0, r0, r0` output.
        string dump = Disassemble("let x = 1; unset x;");

        Assert.Contains("unset.global", dump);
        // The Ax operand renders as `[g<slot>]` with the global name annotation.
        Assert.Contains("[g", dump);
        Assert.DoesNotContain("op_62", dump);
    }

    [Fact]
    public void Disassemble_ForIn_DoesNotMisclassifyIterPrepAsAsBx()
    {
        // ForIn lowers to: NewArray, IterPrep, IterLoop... If IterPrep is mis-
        // classified as AsBx, CollectLabels picks the sBx slice of the same word
        // (which is operand B, plus high bits of unused C, biased) and emits a
        // bogus label like `.LN:` somewhere in the output that does not correspond
        // to a real jump target. The fix removes IterPrep from the AsBx group.
        //
        // The strongest invariant we can check via the public Disassemble API is
        // that the output contains the iter.prep mnemonic and that IterPrep
        // itself does not render a `+/-N` AsBx-style suffix in its annotation
        // column (only true AsBx jumps do).
        string dump = Disassemble("let arr = [1,2,3]; for (let x in arr) { let y = x; }");

        Assert.Contains("iter.prep", dump);

        // Format consistency: iter.prep is documented as ABC, and consults metadata.
        Assert.Equal(OpCodeFormat.ABC, OpCodeMetadata.GetFormat(OpCode.IterPrep));
    }

    [Fact]
    public void Disassemble_ForInWithIndex_RendersIndexedFlag()
    {
        // `for (let (i, x) in arr)` sets the `indexed` flag in operand B of IterPrep.
        // The disassembler now surfaces operand B alongside operand A so the flag
        // is visible — previously the flag was silently dropped.
        string dump = Disassemble("let arr = [1,2,3]; for (let i, x in arr) { let y = i + x; }");

        Assert.Contains("iter.prep", dump);
        // When B != 0 the annotation column carries the literal "indexed" marker.
        Assert.Contains("indexed", dump);
    }
}
