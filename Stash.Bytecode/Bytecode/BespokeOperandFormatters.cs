using System.Collections.Generic;

namespace Stash.Bytecode;

/// <summary>
/// Named renderer delegate for opcodes whose operand layout cannot be expressed
/// by the <see cref="OperandTemplateParser"/> DSL.  Each entry produces the same
/// <c>(operands, comment?)</c> pair as the original per-opcode switch arm in
/// <see cref="Disassembler"/>.
/// </summary>
internal delegate (string operands, string? comment) BespokeFormatter(
    Chunk chunk,
    Dictionary<int, string> labels,
    int idx,
    uint word);

/// <summary>
/// Registry of bespoke operand formatters for opcodes that declare
/// <see cref="OperandTemplate.Bespoke"/>.  All formatters reproduce the legacy
/// output byte-for-byte — this is a pure refactor.
/// </summary>
internal static class BespokeOperandFormatters
{
    public static readonly Dictionary<OpCode, BespokeFormatter> Registry = BuildRegistry();

    private static Dictionary<OpCode, BespokeFormatter> BuildRegistry()
    {
        var r = new Dictionary<OpCode, BespokeFormatter>();

        // ── Loads ─────────────────────────────────────────────────────────────

        r[OpCode.LoadBool] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, {(b != 0 ? "true" : "false")}", c != 0 ? "skip next" : null);
        };

        // ── Globals ───────────────────────────────────────────────────────────

        r[OpCode.GetGlobal] = (chunk, labels, idx, word) =>
        {
            byte   a  = Instruction.GetA(word);
            ushort bx = Instruction.GetBx(word);
            return ($"r{a}, [g{bx}]", Disassembler.FormatGlobalPublic(chunk, bx));
        };

        r[OpCode.SetGlobal] = (chunk, labels, idx, word) =>
        {
            byte   a  = Instruction.GetA(word);
            ushort bx = Instruction.GetBx(word);
            return ($"[g{bx}], r{a}", Disassembler.FormatGlobalPublic(chunk, bx));
        };

        r[OpCode.InitConstGlobal] = (chunk, labels, idx, word) =>
        {
            byte   a  = Instruction.GetA(word);
            ushort bx = Instruction.GetBx(word);
            return ($"[g{bx}], r{a}", $"{Disassembler.FormatGlobalPublic(chunk, bx)} (const)");
        };

        // ── Upvalues ──────────────────────────────────────────────────────────

        r[OpCode.GetUpval] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            return ($"r{a}, [uv{b}]", Disassembler.GetUpvalueNamePublic(chunk, b));
        };

        r[OpCode.SetUpval] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            return ($"[uv{b}], r{a}", Disassembler.GetUpvalueNamePublic(chunk, b));
        };

        // ── Arithmetic ────────────────────────────────────────────────────────

        r[OpCode.AddI] = (chunk, labels, idx, word) =>
        {
            byte a   = Instruction.GetA(word);
            int  sbx = Instruction.GetSBx(word);
            return ($"r{a}, {sbx}", null);
        };

        // ── Logic ─────────────────────────────────────────────────────────────

        r[OpCode.TestSet] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, r{b}, {c}", null);
        };

        r[OpCode.Test] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, {c}", null);
        };

        // ── Control flow / Jumps ──────────────────────────────────────────────

        r[OpCode.Jmp] = (chunk, labels, idx, word) =>
        {
            int sbx = Instruction.GetSBx(word);
            return (Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx), $"{sbx:+0;-0}");
        };

        r[OpCode.JmpFalse] = (chunk, labels, idx, word) =>
        {
            byte a   = Instruction.GetA(word);
            int  sbx = Instruction.GetSBx(word);
            return ($"r{a}, {Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx)}", $"{sbx:+0;-0}");
        };

        r[OpCode.JmpTrue] = (chunk, labels, idx, word) =>
        {
            byte a   = Instruction.GetA(word);
            int  sbx = Instruction.GetSBx(word);
            return ($"r{a}, {Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx)}", $"{sbx:+0;-0}");
        };

        r[OpCode.Loop] = (chunk, labels, idx, word) =>
        {
            int sbx = Instruction.GetSBx(word);
            return (Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx), $"{sbx:+0;-0}");
        };

        r[OpCode.Call] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, {c}", null);
        };

        r[OpCode.Return] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            return (b != 0 ? $"r{a}" : "null", null);
        };

        // ── Iteration ─────────────────────────────────────────────────────────

        r[OpCode.ForPrep] = (chunk, labels, idx, word) =>
        {
            byte a   = Instruction.GetA(word);
            int  sbx = Instruction.GetSBx(word);
            return ($"r{a}, {Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx)}", null);
        };

        r[OpCode.ForLoop] = (chunk, labels, idx, word) =>
        {
            byte a   = Instruction.GetA(word);
            int  sbx = Instruction.GetSBx(word);
            return ($"r{a}, {Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx)}", null);
        };

        r[OpCode.ForPrepII] = (chunk, labels, idx, word) =>
        {
            byte a   = Instruction.GetA(word);
            int  sbx = Instruction.GetSBx(word);
            return ($"r{a}, {Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx)}", null);
        };

        r[OpCode.ForLoopII] = (chunk, labels, idx, word) =>
        {
            byte a   = Instruction.GetA(word);
            int  sbx = Instruction.GetSBx(word);
            return ($"r{a}, {Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx)}", null);
        };

        r[OpCode.IterPrep] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            return ($"r{a}, {b}", b != 0 ? "indexed" : null);
        };

        r[OpCode.IterLoop] = (chunk, labels, idx, word) =>
        {
            byte a   = Instruction.GetA(word);
            int  sbx = Instruction.GetSBx(word);
            return ($"r{a}, {Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx)}", null);
        };

        // ── Tables & Fields ───────────────────────────────────────────────────

        r[OpCode.GetField] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, r{b}, k{c}", $".{Disassembler.FormatFieldNamePublic(chunk, c)}");
        };

        r[OpCode.GetFieldIC] = (chunk, labels, idx, word) =>
        {
            byte a   = Instruction.GetA(word);
            byte b   = Instruction.GetB(word);
            byte c   = Instruction.GetC(word);
            uint icWord = (uint)(idx + 1 < chunk.Code.Length ? chunk.Code[idx + 1] : 0);
            return ($"r{a}, r{b}, k{c}", $".{Disassembler.FormatFieldNamePublic(chunk, c)} [ic:{icWord}]");
        };

        r[OpCode.SetField] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, r{c}, k{b}", $".{Disassembler.FormatFieldNamePublic(chunk, b)}");
        };

        r[OpCode.Self] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, r{b}, k{c}", $".{Disassembler.FormatFieldNamePublic(chunk, c)}");
        };

        // ── Collections ───────────────────────────────────────────────────────

        r[OpCode.NewArray] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            return ($"r{a}, {b}", null);
        };

        r[OpCode.NewDict] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            return ($"r{a}, {b}", null);
        };

        // ── Closures & Types ──────────────────────────────────────────────────

        r[OpCode.NewStruct] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, k{b}, {c}", null);
        };

        // ── Error handling ────────────────────────────────────────────────────

        r[OpCode.TryBegin] = (chunk, labels, idx, word) =>
        {
            byte a   = Instruction.GetA(word);
            int  sbx = Instruction.GetSBx(word);
            return ($"r{a}, {Disassembler.GetLabelRefPublic(labels, idx + 1 + sbx)}", null);
        };

        r[OpCode.CatchMatch] = (chunk, labels, idx, word) =>
        {
            byte   a  = Instruction.GetA(word);
            ushort bx = Instruction.GetBx(word);
            return ($"r{a}, k{bx}", Disassembler.FormatCatchTypesPublic(chunk, bx));
        };

        // ── Built-in call ─────────────────────────────────────────────────────

        r[OpCode.CallBuiltIn] = (chunk, labels, idx, word) =>
        {
            byte a      = Instruction.GetA(word);
            byte b      = Instruction.GetB(word);
            byte c      = Instruction.GetC(word);
            uint icWord = (uint)(idx + 1 < chunk.Code.Length ? chunk.Code[idx + 1] : 0);
            return ($"r{a}, r{b}, {c}", $"({c} args) [ic:{icWord}]");
        };

        // ── Shell ─────────────────────────────────────────────────────────────

        r[OpCode.Command] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, {b}, {c}", null);
        };

        r[OpCode.PipeChain] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, {b} stages, r{c}", $"parts from r{c}");
        };

        r[OpCode.StreamingPipeline] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, {b} stages, r{c}", $"streaming parts from r{c}");
        };

        r[OpCode.Redirect] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, r{c}, {b}", null);
        };

        // ── Strings ───────────────────────────────────────────────────────────

        r[OpCode.Interpolate] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            return ($"r{a}, {b}", null);
        };

        // ── Misc ──────────────────────────────────────────────────────────────

        r[OpCode.Retry] = (chunk, labels, idx, word) =>
        {
            ushort bx = Instruction.GetBx(word);
            return ($"k{bx}", null);
        };

        r[OpCode.Timeout] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            return ($"r{a}, r{(byte)(a + 1)}", null);
        };

        // ── Lock ──────────────────────────────────────────────────────────────

        r[OpCode.LockBegin] = (chunk, labels, idx, word) =>
        {
            byte a = Instruction.GetA(word);
            byte b = Instruction.GetB(word);
            byte c = Instruction.GetC(word);
            return ($"r{a}, r{b}, k{c}", Disassembler.FormatLockMetaPublic(chunk, c));
        };

        // ── Global bindings (Ax format) ───────────────────────────────────────

        r[OpCode.UnsetGlobal] = (chunk, labels, idx, word) =>
        {
            uint ax = Instruction.GetAx(word);
            return ($"[g{ax}]", Disassembler.FormatGlobalPublic(chunk, (ushort)ax));
        };

        // ── Specialized iteration ─────────────────────────────────────────────

        // (ForPrepII, ForLoopII already registered above with ForPrep/ForLoop pattern)

        return r;
    }
}
