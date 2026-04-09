using System;
using System.Collections.Generic;
using System.Text;
using Stash.Runtime;

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

    // ─── Opcode Display Name Map ──────────────────────────────────────────────

    private static readonly Dictionary<OpCode, string> _opNames = new()
    {
        [OpCode.LoadK]          = "load.k",
        [OpCode.LoadNull]       = "load.null",
        [OpCode.LoadBool]       = "load.bool",
        [OpCode.Move]           = "move",
        [OpCode.GetGlobal]      = "get.global",
        [OpCode.SetGlobal]      = "set.global",
        [OpCode.InitConstGlobal]= "init.const.global",
        [OpCode.GetUpval]       = "get.upval",
        [OpCode.SetUpval]       = "set.upval",
        [OpCode.CloseUpval]     = "close.upval",
        [OpCode.Add]            = "add",
        [OpCode.Sub]            = "sub",
        [OpCode.Mul]            = "mul",
        [OpCode.Div]            = "div",
        [OpCode.Mod]            = "mod",
        [OpCode.Pow]            = "pow",
        [OpCode.Neg]            = "neg",
        [OpCode.AddI]           = "addi",
        [OpCode.BAnd]           = "band",
        [OpCode.BOr]            = "bor",
        [OpCode.BXor]           = "bxor",
        [OpCode.BNot]           = "bnot",
        [OpCode.Shl]            = "shl",
        [OpCode.Shr]            = "shr",
        [OpCode.Eq]             = "eq",
        [OpCode.Ne]             = "ne",
        [OpCode.Lt]             = "lt",
        [OpCode.Le]             = "le",
        [OpCode.Gt]             = "gt",
        [OpCode.Ge]             = "ge",
        [OpCode.Not]            = "not",
        [OpCode.TestSet]        = "test.set",
        [OpCode.Test]           = "test",
        [OpCode.Jmp]            = "jmp",
        [OpCode.JmpFalse]       = "jmp.false",
        [OpCode.JmpTrue]        = "jmp.true",
        [OpCode.Loop]           = "loop",
        [OpCode.Call]           = "call",
        [OpCode.Return]         = "return",
        [OpCode.ForPrep]        = "for.prep",
        [OpCode.ForLoop]        = "for.loop",
        [OpCode.IterPrep]       = "iter.prep",
        [OpCode.IterLoop]       = "iter.loop",
        [OpCode.GetTable]       = "get.table",
        [OpCode.SetTable]       = "set.table",
        [OpCode.GetField]       = "get.field",
        [OpCode.SetField]       = "set.field",
        [OpCode.Self]           = "self",
        [OpCode.NewArray]       = "new.array",
        [OpCode.NewDict]        = "new.dict",
        [OpCode.NewRange]       = "new.range",
        [OpCode.Spread]         = "spread",
        [OpCode.Closure]        = "closure",
        [OpCode.NewStruct]      = "new.struct",
        [OpCode.TypeOf]         = "typeof",
        [OpCode.Is]             = "is",
        [OpCode.TryBegin]       = "try.begin",
        [OpCode.TryEnd]         = "try.end",
        [OpCode.Throw]          = "throw",
        [OpCode.TryExpr]        = "try.expr",
        [OpCode.StructDecl]     = "struct.decl",
        [OpCode.EnumDecl]       = "enum.decl",
        [OpCode.IfaceDecl]      = "iface.decl",
        [OpCode.Extend]         = "extend",
        [OpCode.Command]        = "command",
        [OpCode.Pipe]           = "pipe",
        [OpCode.Redirect]       = "redirect",
        [OpCode.Import]         = "import",
        [OpCode.ImportAs]       = "import.as",
        [OpCode.Interpolate]    = "interpolate",
        [OpCode.In]             = "in",
        [OpCode.Switch]         = "switch",
        [OpCode.Destructure]    = "destructure",
        [OpCode.ElevateBegin]   = "elevate.begin",
        [OpCode.ElevateEnd]     = "elevate.end",
        [OpCode.Retry]          = "retry",
        [OpCode.Await]          = "await",
        [OpCode.CallSpread]     = "call.spread",
        [OpCode.CheckNumeric]   = "check.numeric",
        [OpCode.GetFieldIC]     = "get.field.ic",
        [OpCode.CallBuiltIn]    = "call.builtin",
        [OpCode.ForPrepII]      = "for.prepII",
        [OpCode.ForLoopII]      = "for.loopII",
    };

    // ─── Instruction Format Classification ───────────────────────────────────

    private enum InstrFmt { ABC, ABx, AsBx, Ax }

    private static InstrFmt GetFormat(OpCode op) => op switch
    {
        // ABx
        OpCode.LoadK or OpCode.GetGlobal or OpCode.SetGlobal or OpCode.InitConstGlobal
            or OpCode.Closure or OpCode.StructDecl or OpCode.EnumDecl or OpCode.IfaceDecl
            or OpCode.Extend or OpCode.Import or OpCode.ImportAs or OpCode.Switch
            or OpCode.Destructure or OpCode.Retry or OpCode.TryBegin => InstrFmt.ABx,

        // AsBx (signed offset)
        OpCode.AddI or OpCode.Jmp or OpCode.JmpFalse or OpCode.JmpTrue or OpCode.Loop
            or OpCode.ForPrep or OpCode.ForLoop or OpCode.ForPrepII or OpCode.ForLoopII
            or OpCode.IterPrep or OpCode.IterLoop => InstrFmt.AsBx,

        // Ax
        OpCode.TryEnd or OpCode.ElevateEnd => InstrFmt.Ax,

        // ABC (everything else)
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

            // Closure: skip inline upvalue descriptor words
            if (op == OpCode.Closure)
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
            }

            // GetFieldIC: skip companion word (IC slot index)
            if (op == OpCode.GetFieldIC)
            {
                idx++;
            }

            // CallBuiltIn: skip companion word (IC slot index)
            if (op == OpCode.CallBuiltIn)
            {
                idx++;
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

    // ─── Instruction Emitter ─────────────────────────────────────────────────

    private static void EmitInstruction(Chunk chunk, DisassemblerOptions options,
        Dictionary<int, string> labels, int idx, uint word, OpCode op, StringBuilder sb)
    {
        string mnem = _opNames.TryGetValue(op, out string? n) ? n : $"op_{(byte)op:x2}";
        string operands = FormatInstruction(chunk, labels, idx, word, op);

        string offsetStr = options.Compact
            ? $"{idx,4}:"
            : Col(options, $"  {idx:x4}:", Ansi.Dim);

        string mnemStr = Col(options, $"{mnem,-16}", Ansi.Bold);

        sb.AppendLine($"{offsetStr}  {mnemStr}{operands}");
    }

    // ─── Operand Formatter ───────────────────────────────────────────────────

    private static string FormatInstruction(Chunk chunk, Dictionary<int, string> labels, int idx, uint word, OpCode op)
    {
        InstrFmt fmt = GetFormat(op);
        byte a = Instruction.GetA(word);
        byte b = Instruction.GetB(word);
        byte c = Instruction.GetC(word);
        ushort bx = Instruction.GetBx(word);
        int sbx = Instruction.GetSBx(word);

        return op switch
        {
            // Loads
            OpCode.LoadK       => $"r{a}, k{bx}                  ; {FormatConstant(bx < chunk.Constants.Length ? chunk.Constants[bx] : default)}",
            OpCode.LoadNull    => $"r{a}",
            OpCode.LoadBool    => $"r{a}, {(b != 0 ? "true" : "false")}{(c != 0 ? "           ; skip next" : "")}",
            OpCode.Move        => $"r{a}, r{b}",

            // Globals
            OpCode.GetGlobal      => $"r{a}, [g{bx}]              ; {FormatGlobal(chunk, bx)}",
            OpCode.SetGlobal      => $"[g{bx}], r{a}              ; {FormatGlobal(chunk, bx)}",
            OpCode.InitConstGlobal=> $"[g{bx}], r{a}              ; {FormatGlobal(chunk, bx)} (const)",

            // Upvalues
            OpCode.GetUpval    => $"r{a}, [uv{b}]              ; {GetUpvalueName(chunk, b)}",
            OpCode.SetUpval    => $"[uv{b}], r{a}              ; {GetUpvalueName(chunk, b)}",
            OpCode.CloseUpval  => $"r{a}",

            // Arithmetic
            OpCode.Add         => $"r{a}, r{b}, r{c}",
            OpCode.Sub         => $"r{a}, r{b}, r{c}",
            OpCode.Mul         => $"r{a}, r{b}, r{c}",
            OpCode.Div         => $"r{a}, r{b}, r{c}",
            OpCode.Mod         => $"r{a}, r{b}, r{c}",
            OpCode.Pow         => $"r{a}, r{b}, r{c}",
            OpCode.Neg         => $"r{a}, r{b}",
            OpCode.AddI        => $"r{a}, {sbx}",

            // Bitwise
            OpCode.BAnd        => $"r{a}, r{b}, r{c}",
            OpCode.BOr         => $"r{a}, r{b}, r{c}",
            OpCode.BXor        => $"r{a}, r{b}, r{c}",
            OpCode.BNot        => $"r{a}, r{b}",
            OpCode.Shl         => $"r{a}, r{b}, r{c}",
            OpCode.Shr         => $"r{a}, r{b}, r{c}",

            // Comparisons
            OpCode.Eq          => $"r{a}, r{b}, r{c}",
            OpCode.Ne          => $"r{a}, r{b}, r{c}",
            OpCode.Lt          => $"r{a}, r{b}, r{c}",
            OpCode.Le          => $"r{a}, r{b}, r{c}",
            OpCode.Gt          => $"r{a}, r{b}, r{c}",
            OpCode.Ge          => $"r{a}, r{b}, r{c}",

            // Logic
            OpCode.Not         => $"r{a}, r{b}",
            OpCode.TestSet     => $"r{a}, r{b}, {c}",
            OpCode.Test        => $"r{a}, {c}",

            // Jumps
            OpCode.Jmp         => $"{GetLabelRef(labels, idx + 1 + sbx)}              ; {sbx:+0;-0}",
            OpCode.JmpFalse    => $"r{a}, {GetLabelRef(labels, idx + 1 + sbx)}          ; {sbx:+0;-0}",
            OpCode.JmpTrue     => $"r{a}, {GetLabelRef(labels, idx + 1 + sbx)}          ; {sbx:+0;-0}",
            OpCode.Loop        => $"{GetLabelRef(labels, idx + 1 + sbx)}              ; {sbx:+0;-0}",

            // Calls
            OpCode.Call        => $"r{a}, {c}",
            OpCode.Return      => b != 0 ? $"r{a}" : "null",
            OpCode.CallSpread  => $"r{a}",

            // Iteration
            OpCode.ForPrep     => $"r{a}, {GetLabelRef(labels, idx + 1 + sbx)}",
            OpCode.ForLoop     => $"r{a}, {GetLabelRef(labels, idx + 1 + sbx)}",
            OpCode.ForPrepII   => $"r{a}, {GetLabelRef(labels, idx + 1 + sbx)}",
            OpCode.ForLoopII   => $"r{a}, {GetLabelRef(labels, idx + 1 + sbx)}",
            OpCode.IterPrep    => $"r{a}",
            OpCode.IterLoop    => $"r{a}, {GetLabelRef(labels, idx + 1 + sbx)}",

            // Tables
            OpCode.GetTable    => $"r{a}, r{b}, r{c}",
            OpCode.SetTable    => $"r{a}, r{b}, r{c}",
            OpCode.GetField    => $"r{a}, r{b}, k{c}           ; .{FormatFieldName(chunk, c)}",
            OpCode.GetFieldIC  => $"r{a}, r{b}, k{c}           ; .{FormatFieldName(chunk, c)} [ic:{(idx + 1 < chunk.Code.Length ? chunk.Code[idx + 1] : 0)}]",
            OpCode.CallBuiltIn => $"r{a}, r{b}, {c}            ; ({c} args) [ic:{(idx + 1 < chunk.Code.Length ? chunk.Code[idx + 1] : 0)}]",
            OpCode.SetField    => $"r{a}, r{c}, k{b}           ; .{FormatFieldName(chunk, b)}",
            OpCode.Self        => $"r{a}, r{b}, k{c}           ; .{FormatFieldName(chunk, c)}",

            // Collections
            OpCode.NewArray    => $"r{a}, {b}",
            OpCode.NewDict     => $"r{a}, {b}",
            OpCode.NewRange    => $"r{a}, r{b}, r{c}",
            OpCode.Spread      => $"r{a}, r{b}",
            OpCode.Destructure => $"r{a}, k{bx}",

            // Closures & Types
            OpCode.Closure     => $"r{a}, k{bx}               ; {FormatConstant(bx < chunk.Constants.Length ? chunk.Constants[bx] : default)}",
            OpCode.NewStruct   => $"r{a}, k{b}, {c}",
            OpCode.TypeOf      => $"r{a}, r{b}",
            OpCode.Is          => $"r{a}, r{b}, k{c}",

            // Error handling
            OpCode.TryBegin    => $"r{a}, {GetLabelRef(labels, idx + 1 + sbx)}",
            OpCode.TryEnd      => "",
            OpCode.Throw       => $"r{a}",
            OpCode.TryExpr     => $"r{a}, r{b}",

            // Type decls
            OpCode.StructDecl  => $"r{a}, k{bx}",
            OpCode.EnumDecl    => $"r{a}, k{bx}",
            OpCode.IfaceDecl   => $"r{a}, k{bx}",
            OpCode.Extend      => $"r{a}, k{bx}",

            // Shell
            OpCode.Command     => $"r{a}, {b}, {c}",
            OpCode.Pipe        => $"r{a}, r{b}, r{c}",
            OpCode.Redirect    => $"r{a}, r{c}, {b}",
            OpCode.Interpolate => $"r{a}, {b}",

            // Modules
            OpCode.Import      => $"r{a}, k{bx}",
            OpCode.ImportAs    => $"r{a}, k{bx}",

            // Misc
            OpCode.In          => $"r{a}, r{b}, r{c}",
            OpCode.Switch      => $"r{a}, k{bx}",
            OpCode.ElevateBegin=> $"r{a}, r{b}",
            OpCode.ElevateEnd  => "",
            OpCode.Retry       => $"k{bx}",
            OpCode.Await       => $"r{a}, r{b}",

            _ => fmt switch
            {
                InstrFmt.ABC  => $"r{a}, r{b}, r{c}",
                InstrFmt.ABx  => $"r{a}, k{bx}",
                InstrFmt.AsBx => $"r{a}, {sbx:+0;-0}",
                InstrFmt.Ax   => "",
                _ => ""
            }
        };
    }

    // ─── Label Collection ────────────────────────────────────────────────────

    private static Dictionary<int, string> CollectLabels(Chunk chunk)
    {
        var targets = new HashSet<int>();
        for (int idx = 0; idx < chunk.Code.Length; idx++)
        {
            uint word = chunk.Code[idx];
            var op = Instruction.GetOp(word);
            var fmt = GetFormat(op);
            if (fmt == InstrFmt.AsBx)
            {
                int sbx = Instruction.GetSBx(word);
                targets.Add(idx + 1 + sbx);
            }
            if (op == OpCode.Closure)
            {
                ushort protoIdx = Instruction.GetBx(word);
                int uvCount = 0;
                if (protoIdx < chunk.Constants.Length && chunk.Constants[protoIdx].AsObj is Chunk fn)
                    uvCount = fn.Upvalues.Length;
                idx += uvCount; // skip upvalue descriptors
            }
            if (op == OpCode.GetFieldIC)
                idx++; // skip companion word
            if (op == OpCode.CallBuiltIn)
                idx++; // skip companion word
        }

        var labels = new Dictionary<int, string>();
        int labelCount = 0;
        foreach (int t in targets)
            labels[t] = $".L{labelCount++}";
        return labels;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string GetLabelRef(Dictionary<int, string> labels, int target)
        => labels.TryGetValue(target, out string? lbl) ? lbl : $"@{target}";

    private static string FormatGlobal(Chunk chunk, ushort slot)
        => chunk.GlobalNameTable != null && slot < chunk.GlobalNameTable.Length
            ? chunk.GlobalNameTable[slot]
            : slot.ToString();

    private static string GetUpvalueName(Chunk chunk, int slot)
        => chunk.UpvalueNames != null && slot < chunk.UpvalueNames.Length
            ? chunk.UpvalueNames[slot]
            : slot.ToString();

    private static string FormatFieldName(Chunk chunk, byte constIdx)
        => constIdx < chunk.Constants.Length && chunk.Constants[constIdx].AsObj is string s
            ? s
            : constIdx.ToString();

    private static string FormatConstant(StashValue value) => value.Tag switch
    {
        StashValueTag.Null  => "null",
        StashValueTag.Bool  => value.AsBool ? "true" : "false",
        StashValueTag.Int   => value.AsInt.ToString(),
        StashValueTag.Float => value.AsFloat.ToString("G"),
        StashValueTag.Obj   => value.AsObj switch
        {
            string s   => $"\"{EscapeString(s)}\"",
            Chunk fn   => $"<fn:{fn.Name ?? "?"}({fn.Arity}p)>",
            _          => value.AsObj?.GetType().Name ?? "obj",
        },
        _ => "?"
    };

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

    private static string Col(DisassemblerOptions options, string text, string ansiCode)
        => options.Color ? $"{ansiCode}{text}{Ansi.Reset}" : text;
}
