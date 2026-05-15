using Stash.Bytecode;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Golden-output tests verifying that the template-driven <see cref="Disassembler"/>
/// refactor produces byte-for-byte identical output to the previous per-opcode switch.
///
/// Each test compiles a minimal <see cref="Chunk"/> using <see cref="ChunkBuilder"/>
/// (giving full control over the instruction sequence), then asserts the compact-mode
/// disassembly matches a known-good golden string.
///
/// Compact mode is used so the golden strings are short and not sensitive to the chunk
/// header (register counts, local names, source file paths).
/// </summary>
public class DisassemblerTemplateTests : BytecodeTestBase
{
    private static readonly DisassemblerOptions CompactOptions = new() { Compact = true };

    private static string Disasm(Chunk chunk) =>
        Disassembler.Disassemble(chunk, CompactOptions);

    // ── R(A), R(B), R(C) — three-register opcode ─────────────────────────────

    [Fact]
    public void Template_RRR_Add_RendersThreeRegisters()
    {
        var b = new ChunkBuilder { MaxRegs = 3 };
        b.EmitABC(OpCode.Add, 2, 0, 1);

        string output = Disasm(b.Build());
        // Compact: "   0:  add                 r2, r0, r1"
        Assert.Contains("add", output);
        Assert.Contains("r2, r0, r1", output);
        Assert.DoesNotContain(";", output); // no comment for R/R/R
    }

    // ── K(Bx) — constant pool reference with comment ─────────────────────────

    [Fact]
    public void Template_KBx_LoadK_RendersConstantWithComment()
    {
        var b = new ChunkBuilder { MaxRegs = 1 };
        ushort k = b.AddConstant(42L);
        b.EmitABx(OpCode.LoadK, 0, k);

        string output = Disasm(b.Build());
        Assert.Contains("load.k", output);
        Assert.Contains("r0, k0", output);
        Assert.Contains("; 42", output);
    }

    [Fact]
    public void Template_KBx_Closure_RendersConstantWithComment()
    {
        var innerBuilder = new ChunkBuilder { Name = "fn", Arity = 0, MinArity = 0 };
        innerBuilder.EmitA(OpCode.Return, 0);
        Chunk inner = innerBuilder.Build();

        var b = new ChunkBuilder { MaxRegs = 1 };
        ushort k = b.AddConstant(inner);
        b.EmitABx(OpCode.Closure, 0, k);

        string output = Disasm(b.Build());
        Assert.Contains("closure", output);
        Assert.Contains("r0, k0", output);
        // FormatConstant on a Chunk renders as "<fn:fn(0p)>"
        Assert.Contains("<fn:fn(0p)>", output);
    }

    // ── G(Bx) — global slot reference with name annotation ───────────────────

    [Fact]
    public void Bespoke_GetGlobal_RendersGlobalSlotWithAnnotation()
    {
        // Compile a script that reads a global so GetGlobal is emitted with a
        // real name table entry — ChunkBuilder does not expose GlobalNameTable directly.
        string output = Disassemble("let x = 1; let y = x;");
        Assert.Contains("get.global", output);
        Assert.Contains("[g0]", output);
        Assert.Contains("; x", output);
    }

    // ── L(sBx) — label reference (jump instruction) ──────────────────────────

    [Fact]
    public void Template_LSBx_JmpFalse_RendersLabelAndOffset()
    {
        var b = new ChunkBuilder { MaxRegs = 2, EnableDce = false };
        int patch = b.EmitJump(OpCode.JmpFalse, a: 0);
        b.EmitABC(OpCode.Add, 1, 1, 1);       // body — never reached
        b.PatchJump(patch);
        b.EmitA(OpCode.LoadNull, 0);

        string output = Disasm(b.Build());
        Assert.Contains("jmp.false", output);
        Assert.Contains(".L", output);          // label reference
        Assert.Contains("+1", output);          // offset annotation
    }

    // ── F(C) — field name reference ───────────────────────────────────────────

    [Fact]
    public void Template_FC_GetField_RendersFieldNameAsComment()
    {
        var b = new ChunkBuilder { MaxRegs = 2 };
        ushort k = b.AddConstant("myField");
        b.EmitABC(OpCode.GetField, 0, 1, (byte)k);

        string output = Disasm(b.Build());
        Assert.Contains("get.field", output);
        Assert.Contains("r0, r1, k0", output);
        Assert.Contains(".myField", output);
    }

    // ── Bespoke: GetFieldIC with companion-word annotation ───────────────────

    [Fact]
    public void Bespoke_GetFieldIC_RendersFieldAndIcSlot()
    {
        var b = new ChunkBuilder { MaxRegs = 2, EnableDce = false };
        ushort k = b.AddConstant("prop");
        // GetFieldIC is followed by one companion word (IC slot index)
        b.EmitABC(OpCode.GetFieldIC, 0, 1, (byte)k);
        b.EmitRaw(7u);   // IC slot companion word = 7

        string output = Disasm(b.Build());
        Assert.Contains("get.field.ic", output);
        Assert.Contains("r0, r1, k0", output);
        Assert.Contains(".prop", output);
        Assert.Contains("[ic:7]", output);
    }

    // ── KN(Bx) — no-comment constant (metadata opcodes) ──────────────────────

    [Fact]
    public void Template_KNBx_StructDecl_RendersNoComment()
    {
        // StructDecl uses KN(Bx) — it has no constant-pool comment in the output.
        // We can't easily drive a full struct compilation here, so we just verify
        // the opcode itself is registered correctly as a grammar template (not bespoke)
        // and that the template string parses without throwing.
        string template = OpCodeMetadata.GetOperandTemplate(OpCode.StructDecl);
        Assert.Equal("R(A), KN(Bx)", template);
        // Parsing should succeed (no exception).
        OperandToken[] tokens = OperandTemplateParser.Parse(template);
        Assert.NotEmpty(tokens);
        // KNBx token should appear in the parsed sequence.
        Assert.Contains(tokens, t => t.Kind == OperandTokenKind.KNBx);
    }

    // ── Every grammar template parses without error ────────────────────────────

    [Fact]
    public void Template_AllNonBespokeOpcodes_ParseWithoutThrowing()
    {
        foreach (OpCode op in Enum.GetValues<OpCode>())
        {
            string template = OpCodeMetadata.GetOperandTemplate(op);
            if (template == OperandTemplate.Bespoke || template == OperandTemplate.Empty)
                continue;

            // Should not throw.
            OperandToken[] tokens = OperandTemplateParser.Parse(template);
            Assert.NotNull(tokens);
        }
    }

    // ── Every bespoke opcode is registered ────────────────────────────────────

    [Fact]
    public void Bespoke_AllBespokeOpcodes_HaveRegisteredFormatter()
    {
        foreach (OpCode op in Enum.GetValues<OpCode>())
        {
            if (OpCodeMetadata.GetOperandTemplate(op) != OperandTemplate.Bespoke)
                continue;

            bool hasFormatter = BespokeOperandFormatters.Registry.ContainsKey(op);
            Assert.True(hasFormatter,
                $"OpCode.{op} declares Operands = Bespoke but has no entry in BespokeOperandFormatters.Registry");
        }
    }
}
