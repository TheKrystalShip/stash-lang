using System;
using System.Collections.Generic;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Options that control how <see cref="Disassembler"/> formats its output.
/// </summary>
public class DisassemblerOptions
{
    /// <summary>When true, use compact format (no annotations, no section headers). Default: false.</summary>
    public bool Compact { get; set; }

    /// <summary>When true, emit ANSI color codes. Default: false.</summary>
    public bool Color { get; set; }

    /// <summary>Original source text for inline source annotations. Null if unavailable.</summary>
    public string? SourceText { get; set; }
}

/// <summary>
/// Produces human-readable disassembly of register-based <see cref="Chunk"/> bytecode.
/// Each instruction is a 32-bit word: ABC [op:8][A:8][B:8][C:8], ABx [op:8][A:8][Bx:16],
/// AsBx [op:8][A:8][sBx:16], Ax [op:8][Ax:24].
/// </summary>
public static class Disassembler
{
    // ─── ANSI Color Codes ─────────────────────────────────────────────────────

    private static class Ansi
    {
        public const string Reset       = "\x1b[0m";
        public const string Dim         = "\x1b[2m";
        public const string Bold        = "\x1b[1m";
        public const string Cyan        = "\x1b[36m";
        public const string Yellow      = "\x1b[33m";
        public const string Green       = "\x1b[32m";
        public const string BoldMagenta = "\x1b[1;35m";
        public const string DimGreen    = "\x1b[2;32m";
    }

    // ─── Mnemonic & Format Lookup ────────────────────────────────────────────
    //
    // The mnemonic table, the instruction-format classification, the operand-role
    // flags, and the companion-word kind all live as [OpCode(...)] attributes on
    // the OpCode enum itself (single source of truth). See OpCodeMetadata.cs.
    //
    // The local alias `InstrFmt` keeps the rest of this file readable; it maps
    // directly to OpCodeFormat from OpCodeMetadata.GetFormat.

    private enum InstrFmt { ABC, ABx, AsBx, Ax }

    private static InstrFmt GetFormat(OpCode op) => OpCodeMetadata.GetFormat(op) switch
    {
        OpCodeFormat.ABC  => InstrFmt.ABC,
        OpCodeFormat.ABx  => InstrFmt.ABx,
        OpCodeFormat.AsBx => InstrFmt.AsBx,
        OpCodeFormat.Ax   => InstrFmt.Ax,
        _ => InstrFmt.ABC,
    };

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Disassemble a chunk with default options.</summary>
    public static string Disassemble(Chunk chunk) => Disassemble(chunk, new DisassemblerOptions());

    /// <summary>Disassemble a chunk with full options.</summary>
    public static string Disassemble(Chunk chunk, DisassemblerOptions options)
    {
        var sb = new StringBuilder();
        DisassembleChunk(chunk, options, sb);
        return sb.ToString();
    }

    /// <summary>Disassemble a chunk and all nested function chunks recursively.</summary>
    public static string DisassembleAll(Chunk chunk, DisassemblerOptions? options = null)
    {
        options ??= new DisassemblerOptions();
        var sb = new StringBuilder();
        DisassembleAllRecursive(chunk, options, sb, isFirst: true);
        return sb.ToString();
    }

    // ─── Core Dispatch ───────────────────────────────────────────────────────

    private static void DisassembleAllRecursive(Chunk chunk, DisassemblerOptions options, StringBuilder sb, bool isFirst)
    {
        if (!isFirst)
            sb.AppendLine();
        DisassembleChunk(chunk, options, sb);

        foreach (StashValue constant in chunk.Constants)
        {
            if (constant.AsObj is Chunk nested)
                DisassembleAllRecursive(nested, options, sb, isFirst: false);
        }
    }

    private static void DisassembleChunk(Chunk chunk, DisassemblerOptions options, StringBuilder sb)
    {
        if (options.Compact)
        {
            sb.AppendLine(Col(options, $"== {chunk.Name ?? "<script>"} ==", Ansi.BoldMagenta));
        }
        else
        {
            string name = chunk.Name ?? "<script>";
            string titleLine = $"; ─── {name} ";
            string titleFill = new string('─', Math.Max(0, 66 - titleLine.Length));
            sb.AppendLine(Col(options, $"{titleLine}{titleFill}", Ansi.BoldMagenta));

            int locals = chunk.LocalNames?.Length ?? 0;
            if (chunk.Name == null)
            {
                string? srcFile = chunk.SourceMap.Count > 0
                    ? System.IO.Path.GetFileName(chunk.SourceMap[0].Span.File)
                    : null;
                if (srcFile != null)
                    sb.AppendLine($"; source: {srcFile}");
                sb.AppendLine($"; regs: {chunk.MaxRegs}   locals: {locals}   globals: {chunk.GlobalSlotCount}   constants: {chunk.Constants.Length}");
            }
            else
            {
                string arityInfo = chunk.MinArity == chunk.Arity
                    ? chunk.Arity.ToString()
                    : $"{chunk.MinArity}..{chunk.Arity}";
                sb.AppendLine($"; arity: {arityInfo}   regs: {chunk.MaxRegs}   locals: {locals}   upvalues: {chunk.Upvalues.Length}");
            }
            sb.AppendLine(Col(options, $"; {new string('─', 64)}", Ansi.BoldMagenta));
            sb.AppendLine();

            EmitConstSection(chunk, options, sb);
            EmitGlobalsSection(chunk, options, sb);
            EmitConstGlobalInitsSection(chunk, options, sb);
            // EmitLocalsSection(chunk, options, sb);
        }

        var labels = CollectLabels(chunk);
        string[]? sourceLines = options.SourceText?.Split('\n');

        if (!options.Compact)
            sb.AppendLine(Col(options, ".code:", Ansi.BoldMagenta));

        int prevLine = -1;
        for (int idx = 0; idx < chunk.Code.Length; idx++)
        {
            uint word = chunk.Code[idx];
            var op = Instruction.GetOp(word);

            if (labels.TryGetValue(idx, out string? labelName))
                sb.AppendLine(Col(options, $"{labelName}:", Ansi.Yellow));

            if (!options.Compact)
            {
                int curLine = chunk.SourceMap.GetLine(idx);
                if (curLine != -1 && curLine != prevLine)
                {
                    sb.AppendLine(Col(options, BuildSourceAnnotation(curLine, sourceLines), Ansi.DimGreen));
                    prevLine = curLine;
                }
            }

            EmitInstruction(chunk, options, labels, idx, word, op, sb);

            // Companion-word handling — driven by OpCodeMetadata so any new opcode
            // that declares CompanionWords is handled without touching this loop.
            CompanionWordKind cwKind = OpCodeMetadata.IsDefined((byte)op)
                ? OpCodeMetadata.GetCompanionWords(op)
                : CompanionWordKind.None;
            switch (cwKind)
            {
                case CompanionWordKind.UpvalueDescriptors:
                {
                    ushort protoIdx = Instruction.GetBx(word);
                    int uvCount = 0;
                    if (protoIdx < chunk.Constants.Length && chunk.Constants[protoIdx].AsObj is Chunk fn)
                        uvCount = fn.Upvalues.Length;
                    for (int u = 0; u < uvCount; u++)
                    {
                        idx++;
                        uint uvWord = chunk.Code[idx];
                        byte isLocal = (byte)(uvWord & 0xFF);
                        byte uvIdx   = (byte)((uvWord >> 8) & 0xFF);
                        if (!options.Compact)
                            sb.AppendLine($"                    ; upvalue [{u}]: {(isLocal != 0 ? "local" : "upval")} {uvIdx}");
                    }
                    break;
                }
                case CompanionWordKind.OneIC:
                    idx++;
                    break;
                case CompanionWordKind.PipeStages:
                {
                    byte stageCount = Instruction.GetB(word);
                    for (int s = 0; s < stageCount; s++)
                    {
                        idx++;
                        if (idx < chunk.Code.Length)
                        {
                            uint cwWord = chunk.Code[idx];
                            int partCount = (int)((cwWord >> 8) & 0xFF);
                            byte flags = (byte)(cwWord & 0xFF);
                            if (!options.Compact)
                                sb.AppendLine($"                    ; stage[{s}]: parts={partCount} flags=0x{flags:x2}");
                        }
                    }
                    break;
                }
            }
        }
    }

    // ─── Section Emitters ────────────────────────────────────────────────────

    private static void EmitConstSection(Chunk chunk, DisassemblerOptions options, StringBuilder sb)
    {
        if (chunk.Constants.Length == 0)
            return;
        sb.AppendLine(Col(options, ".const:", Ansi.BoldMagenta));
        for (int i = 0; i < chunk.Constants.Length; i++)
        {
            string idx = Col(options, $"[{i}]", Ansi.Cyan);
            string val = Col(options, FormatConstant(chunk.Constants[i]), Ansi.Cyan);
            sb.AppendLine($"  {idx} {val}");
        }
        sb.AppendLine();
    }

    private static void EmitGlobalsSection(Chunk chunk, DisassemblerOptions options, StringBuilder sb)
    {
        if (chunk.GlobalNameTable == null || chunk.GlobalSlotCount == 0)
            return;
        sb.AppendLine(Col(options, ".globals:", Ansi.BoldMagenta));
        for (int i = 0; i < chunk.GlobalSlotCount; i++)
        {
            string gname = chunk.GlobalNameTable != null && i < chunk.GlobalNameTable.Length
                ? chunk.GlobalNameTable[i]
                : i.ToString();
            sb.AppendLine($"  [g{i}] {gname}");
        }
        sb.AppendLine();
    }

    private static void EmitConstGlobalInitsSection(Chunk chunk, DisassemblerOptions options, StringBuilder sb)
    {
        if (chunk.ConstGlobalInits is not { Length: > 0 })
            return;
        sb.AppendLine(Col(options, ".const_global_inits:", Ansi.BoldMagenta));
        for (int i = 0; i < chunk.ConstGlobalInits.Length; i++)
        {
            var (slot, constIdx) = chunk.ConstGlobalInits[i];
            string gname = chunk.GlobalNameTable != null && slot < chunk.GlobalNameTable.Length
                ? chunk.GlobalNameTable[slot]
                : slot.ToString();
            string constVal = constIdx < chunk.Constants.Length
                ? FormatConstant(chunk.Constants[constIdx])
                : $"k{constIdx}";
            sb.AppendLine($"  [g{slot}] = [{constIdx}]  ; {gname} = {constVal}");
        }
        sb.AppendLine();
    }

    private static void EmitLocalsSection(Chunk chunk, DisassemblerOptions options, StringBuilder sb)
    {
        if (chunk.LocalNames is not { Length: > 0 })
            return;

        sb.AppendLine(Col(options, ".locals:", Ansi.BoldMagenta));

        // Parameters occupy registers 0..Arity-1
        int arity = chunk.Arity;
        bool hasUpvalues = chunk.Upvalues.Length > 0;

        for (int i = 0; i < chunk.LocalNames.Length; i++)
        {
            string name = chunk.LocalNames[i];
            bool isConst = chunk.LocalIsConst != null && i < chunk.LocalIsConst.Length && chunk.LocalIsConst[i];

            // Classify the register role
            string role;
            if (i < arity)
                role = "param";
            else if (name.StartsWith('<') && name.EndsWith('>'))
                role = "internal";   // compiler-managed: <for_counter>, <lock_err>, etc.
            else
                role = isConst ? "const" : "local";

            string regStr  = Col(options, $"[r{i}]", Ansi.Cyan);
            string nameStr = Col(options, name, Ansi.Cyan);
            sb.AppendLine($"  {regStr} {nameStr,-24} ; {role}");
        }
        sb.AppendLine();
    }

    // ─── Instruction Emitter ─────────────────────────────────────────────────

    private static void EmitInstruction(Chunk chunk, DisassemblerOptions options,
        Dictionary<int, string> labels, int idx, uint word, OpCode op, StringBuilder sb)
    {
        string mnem = OpCodeMetadata.IsDefined((byte)op)
            ? OpCodeMetadata.GetMnemonic(op)
            : $"op_{(byte)op:x2}";
        (string operands, string? comment) = FormatInstruction(chunk, labels, idx, word, op);

        string offsetStr = options.Compact
            ? $"{idx,4}:"
            : Col(options, $"  {idx:x4}:", Ansi.Dim);

        string mnemStr = Col(options, $"{mnem,-20}", Ansi.Bold);

        if (comment != null)
        {
            string commentStr = Col(options, $"; {comment}", Ansi.DimGreen);
            sb.AppendLine($"{offsetStr}  {mnemStr}{operands,-24}{commentStr}");
        }
        else
        {
            sb.AppendLine($"{offsetStr}  {mnemStr}{operands}");
        }
    }

    // ─── Operand Formatter ───────────────────────────────────────────────────
    //
    // FormatInstruction is driven by the OpCodeAttribute.Operands template string:
    //
    //   1. Empty template  → ("", null)
    //   2. Bespoke token   → dispatch to BespokeOperandFormatters.Registry[op]
    //   3. Grammar tokens  → evaluate via OperandTemplateRenderer.Render(...)
    //
    // The per-opcode switch that previously lived here has been replaced by the
    // two supporting files (OperandTemplateParser.cs, BespokeOperandFormatters.cs).
    // Adding a new opcode no longer requires touching this method — only the
    // OpCodeAttribute on the enum member and (if needed) a bespoke registry entry.

    private static (string operands, string? comment) FormatInstruction(
        Chunk chunk, Dictionary<int, string> labels, int idx, uint word, OpCode op)
    {
        string template = OpCodeMetadata.IsDefined((byte)op)
            ? OpCodeMetadata.GetOperandTemplate(op)
            : OperandTemplate.Bespoke;

        // Empty operand list (TryEnd, ElevateEnd, LockEnd, …)
        if (template == OperandTemplate.Empty)
            return (string.Empty, null);

        // Bespoke — opcode renders itself
        if (template == OperandTemplate.Bespoke)
        {
            if (BespokeOperandFormatters.Registry.TryGetValue(op, out BespokeFormatter? fmt))
                return fmt(chunk, labels, idx, word);

            // Unknown opcode with no bespoke handler — produce a safe fallback.
            InstrFmt fallbackFmt = GetFormat(op);
            byte   fa  = Instruction.GetA(word);
            byte   fb  = Instruction.GetB(word);
            byte   fc  = Instruction.GetC(word);
            ushort fbx = Instruction.GetBx(word);
            int    fsb = Instruction.GetSBx(word);
            return (fallbackFmt switch
            {
                InstrFmt.ABC  => $"r{fa}, r{fb}, r{fc}",
                InstrFmt.ABx  => $"r{fa}, k{fbx}",
                InstrFmt.AsBx => $"r{fa}, {fsb:+0;-0}",
                InstrFmt.Ax   => string.Empty,
                _             => string.Empty,
            }, null);
        }

        // Template-driven path — parse tokens (cached) and render
        OperandToken[]? tokens = OperandTemplateCache.Get(op);
        if (tokens is null)
        {
            // Template was not pre-parsed (shouldn't happen for defined opcodes).
            return (string.Empty, null);
        }

        return OperandTemplateRenderer.Render(tokens, chunk, labels, idx, word);
    }

    // ─── Label Collection ────────────────────────────────────────────────────

    private static Dictionary<int, string> CollectLabels(Chunk chunk)
    {
        var targets = new HashSet<int>();
        for (int idx = 0; idx < chunk.Code.Length; idx++)
        {
            uint word = chunk.Code[idx];
            var op = Instruction.GetOp(word);
            if (!OpCodeMetadata.IsDefined((byte)op))
                continue;

            // AsBx jump targets — driven by metadata format, not a hardcoded list.
            if (OpCodeMetadata.GetFormat(op) == OpCodeFormat.AsBx)
            {
                int sbx = Instruction.GetSBx(word);
                targets.Add(idx + 1 + sbx);
            }

            // Companion-word skip — driven by metadata.
            switch (OpCodeMetadata.GetCompanionWords(op))
            {
                case CompanionWordKind.UpvalueDescriptors:
                {
                    ushort protoIdx = Instruction.GetBx(word);
                    int uvCount = 0;
                    if (protoIdx < chunk.Constants.Length && chunk.Constants[protoIdx].AsObj is Chunk fn)
                        uvCount = fn.Upvalues.Length;
                    idx += uvCount;
                    break;
                }
                case CompanionWordKind.OneIC:
                    idx++;
                    break;
                case CompanionWordKind.PipeStages:
                {
                    byte stageCount = Instruction.GetB(word);
                    idx += stageCount;
                    break;
                }
            }
        }

        var labels = new Dictionary<int, string>();
        int labelCount = 0;
        foreach (int t in targets)
            labels[t] = $".L{labelCount++}";
        return labels;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────
    //
    // These helpers are internal so that BespokeOperandFormatters and
    // OperandTemplateRenderer (in separate files) can call them without
    // duplicating logic.  The "Public" suffix distinguishes the callable
    // aliases from any future private overloads — within this file the
    // private wrapper name is kept for backward compatibility with callers
    // that already exist in this file.

    private static string GetLabelRef(Dictionary<int, string> labels, int target)
        => GetLabelRefPublic(labels, target);

    internal static string GetLabelRefPublic(Dictionary<int, string> labels, int target)
        => labels.TryGetValue(target, out string? lbl) ? lbl : $"@{target}";

    private static string FormatGlobal(Chunk chunk, ushort slot)
        => FormatGlobalPublic(chunk, slot);

    internal static string FormatGlobalPublic(Chunk chunk, ushort slot)
        => chunk.GlobalNameTable != null && slot < chunk.GlobalNameTable.Length
            ? chunk.GlobalNameTable[slot]
            : slot.ToString();

    private static string GetUpvalueName(Chunk chunk, int slot)
        => GetUpvalueNamePublic(chunk, slot);

    internal static string GetUpvalueNamePublic(Chunk chunk, int slot)
        => chunk.UpvalueNames != null && slot < chunk.UpvalueNames.Length
            ? chunk.UpvalueNames[slot]
            : slot.ToString();

    private static string FormatFieldName(Chunk chunk, byte constIdx)
        => FormatFieldNamePublic(chunk, constIdx);

    internal static string FormatFieldNamePublic(Chunk chunk, byte constIdx)
        => constIdx < chunk.Constants.Length && chunk.Constants[constIdx].AsObj is string s
            ? s
            : constIdx.ToString();

    private static string FormatConstant(StashValue value) => FormatConstantPublic(value);

    internal static string FormatConstantPublic(StashValue value) => value.Tag switch
    {
        StashValueTag.Null  => "null",
        StashValueTag.Bool  => value.AsBool ? "true" : "false",
        StashValueTag.Int   => value.AsInt.ToString(),
        StashValueTag.Float => value.AsFloat.ToString("G"),
        StashValueTag.Obj   => value.AsObj switch
        {
            string s       => $"\"{ EscapeString(s)}\"",
            Chunk fn       => $"<fn:{fn.Name ?? "?"}({fn.Arity}p)>",
            string[] types => types.Length == 0
                ? "string[0] <catch-all>"
                : $"string[] {{{string.Join(", ", types)}}}",
            LockMetadata m => FormatLockMetaConst(m),
            StashLiteralArg la => la.ShouldExpand
                ? $"LiteralArg(\"{EscapeString(la.Text)}\")"
                : $"LiteralArg(\"{EscapeString(la.Text)}\", verbatim)",
            _              => value.AsObj?.GetType().Name ?? "obj",
        },
        _ => "?"
    };

    private static string FormatLockMetaConst(LockMetadata m)
    {
        if (!m.HasWait && !m.HasStale) return "LockMetadata()";
        if (m.HasWait && m.HasStale)   return "LockMetadata(wait,stale)";
        return m.HasWait ? "LockMetadata(wait)" : "LockMetadata(stale)";
    }

    private static string EscapeString(string s)
    {
        if (s.Length > 48)
            s = s[..45] + "...";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private static string BuildSourceAnnotation(int line, string[]? sourceLines)
    {
        if (sourceLines != null && line >= 1 && line <= sourceLines.Length)
        {
            string src = sourceLines[line - 1].Trim();
            if (src.Length > 60) src = src[..57] + "...";
            return $"  ; {line}: {src}";
        }
        return $"  ; line {line}";
    }

    private static string FormatCatchTypes(Chunk chunk, ushort idx)
        => FormatCatchTypesPublic(chunk, idx);

    internal static string FormatCatchTypesPublic(Chunk chunk, ushort idx)
    {
        if (idx < chunk.Constants.Length && chunk.Constants[idx].AsObj is string[] types)
            return types.Length == 0 ? "catch-all" : string.Join(" | ", types);
        return $"k{idx}";
    }

    private static string FormatLockMeta(Chunk chunk, byte idx)
        => FormatLockMetaPublic(chunk, idx);

    internal static string FormatLockMetaPublic(Chunk chunk, byte idx)
    {
        if (idx < chunk.Constants.Length && chunk.Constants[idx].AsObj is LockMetadata meta)
        {
            if (!meta.HasWait && !meta.HasStale) return "no opts";
            if (meta.HasWait && meta.HasStale)   return "opts=[wait,stale]";
            return meta.HasWait ? "opts=[wait]" : "opts=[stale]";
        }
        return $"k{idx}";
    }

    private static string Col(DisassemblerOptions options, string text, string ansiCode)
        => options.Color ? $"{ansiCode}{text}{Ansi.Reset}" : text;

    // ─── Builtin Call Name Resolution ────────────────────────────────────────

    /// <summary>Maximum number of instructions to scan backward when resolving a namespace prefix.</summary>
    private const int MaxBackwardSteps = 8;

    /// <summary>
    /// Resolves the human-readable name for a <c>CallBuiltIn</c> instruction at
    /// <paramref name="instrIdx"/>.  The companion word at <c>instrIdx+1</c> holds the
    /// IC slot index; <c>ICSlots[slot].ConstantIndex</c> gives the method-name constant.
    /// A bounded backward scan of at most <see cref="MaxBackwardSteps"/> real instructions
    /// (skipping companion words) looks for the most recent instruction that wrote to the
    /// receiver register (operand B of the <c>CallBuiltIn</c>) via <c>GetGlobal</c>,
    /// <c>GetUpval</c>, or <c>Move</c> and recovers the source name to use as a namespace prefix.
    /// </summary>
    /// <returns>A string like <c>"io.println(2 args)"</c> or <c>".println(2 args)"</c> on fallback.</returns>
    internal static string ResolveBuiltinCallName(Chunk chunk, int instrIdx)
    {
        uint callWord = chunk.Code[instrIdx];
        byte receiverReg = Instruction.GetB(callWord);
        byte argCount    = Instruction.GetC(callWord);

        // IC slot index is in the companion word immediately following the instruction.
        uint icSlot = instrIdx + 1 < chunk.Code.Length ? chunk.Code[instrIdx + 1] : 0;

        // Resolve method name from IC slot constant index.
        string? methodName = null;
        if (chunk.ICSlots != null && icSlot < (uint)chunk.ICSlots.Length)
        {
            ushort constIdx = chunk.ICSlots[(int)icSlot].ConstantIndex;
            if (constIdx < chunk.Constants.Length && chunk.Constants[constIdx].AsObj is string s)
                methodName = s;
        }

        if (methodName == null)
            return $"({argCount} args)";

        // Build a list of instruction-start offsets up to instrIdx so we can walk
        // backward through real instructions only (skipping companion words).
        var instrOffsets = new List<int>(instrIdx + 1);
        for (int i = 0; i < instrIdx; )
        {
            instrOffsets.Add(i);
            uint w  = chunk.Code[i];
            var  op = Instruction.GetOp(w);
            i++;
            // Skip companion words so we do not misidentify them as instructions.
            if (OpCodeMetadata.IsDefined((byte)op))
            {
                switch (OpCodeMetadata.GetCompanionWords(op))
                {
                    case CompanionWordKind.OneIC:
                        i++;
                        break;
                    case CompanionWordKind.UpvalueDescriptors:
                    {
                        ushort protoIdx = Instruction.GetBx(w);
                        if (protoIdx < chunk.Constants.Length && chunk.Constants[protoIdx].AsObj is Chunk fn)
                            i += fn.Upvalues.Length;
                        break;
                    }
                    case CompanionWordKind.PipeStages:
                        i += Instruction.GetB(w);
                        break;
                }
            }
        }

        // Walk backward through real instructions, bounded by MaxBackwardSteps.
        string? nsName = null;
        int stepsRemaining = MaxBackwardSteps;
        for (int j = instrOffsets.Count - 1; j >= 0 && stepsRemaining > 0; j--, stepsRemaining--)
        {
            uint w  = chunk.Code[instrOffsets[j]];
            var  op = Instruction.GetOp(w);

            // Stop at any jump (we do not track control flow).
            if (OpCodeMetadata.IsDefined((byte)op) && OpCodeMetadata.GetFormat(op) == OpCodeFormat.AsBx)
                break;

            byte destA = Instruction.GetA(w);
            if (destA != receiverReg)
                continue;

            // Found the most recent writer of receiverReg.
            if (op == OpCode.GetGlobal)
            {
                ushort bx = Instruction.GetBx(w);
                nsName = FormatGlobalPublic(chunk, bx);
            }
            else if (op == OpCode.GetUpval)
            {
                byte b = Instruction.GetB(w);
                nsName = GetUpvalueNamePublic(chunk, b);
            }
            else if (op == OpCode.Move)
            {
                byte srcB = Instruction.GetB(w);
                if (chunk.LocalNames != null && srcB < chunk.LocalNames.Length)
                    nsName = chunk.LocalNames[srcB];
            }
            break;
        }

        string callName = nsName != null
            ? $"{nsName}.{methodName}({argCount} args)"
            : $".{methodName}({argCount} args)";

        return callName;
    }
}
