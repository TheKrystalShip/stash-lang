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
            or OpCode.ForPrep or OpCode.ForLoop or OpCode.IterPrep or OpCode.IterLoop => InstrFmt.AsBx,

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
                sb.AppendLine(Col(options, $"  {labelName}:", Ansi.Yellow));

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
                        sb.AppendLine($"      ; upvalue [{u}]: {(isLocal != 0 ? "local" : "upval")} {uvIdx}");
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
            sb.AppendLine($"  ${i} = {gname}");
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
            : Col(options, $"  {idx,4}", Ansi.Dim);

        string rawHex = options.Compact ? "" : Col(options, $" {word:x8}", Ansi.Dim) + " ";
        string mnemStr = Col(options, $"{mnem,-18}", Ansi.Bold);

        sb.AppendLine($"{offsetStr}{rawHex} {mnemStr} {operands}");
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
            OpCode.LoadK       => $"R({a}) = K({bx})  ; {FormatConstant(bx < chunk.Constants.Length ? chunk.Constants[bx] : default)}",
            OpCode.LoadNull    => $"R({a})",
            OpCode.LoadBool    => $"R({a}) = {(b != 0 ? "true" : "false")}{(c != 0 ? " ; skip next" : "")}",
            OpCode.Move        => $"R({a}) = R({b})",

            // Globals
            OpCode.GetGlobal      => $"R({a}) = G[{bx}]  ; {FormatGlobal(chunk, bx)}",
            OpCode.SetGlobal      => $"G[{bx}] = R({a})  ; {FormatGlobal(chunk, bx)}",
            OpCode.InitConstGlobal=> $"G[{bx}] = R({a})  ; {FormatGlobal(chunk, bx)} (const)",

            // Upvalues
            OpCode.GetUpval    => $"R({a}) = UV[{b}]  ; {GetUpvalueName(chunk, b)}",
            OpCode.SetUpval    => $"UV[{b}] = R({a})  ; {GetUpvalueName(chunk, b)}",
            OpCode.CloseUpval  => $"close R({a})",

            // Arithmetic
            OpCode.Add         => $"R({a}) = R({b}) + R({c})",
            OpCode.Sub         => $"R({a}) = R({b}) - R({c})",
            OpCode.Mul         => $"R({a}) = R({b}) * R({c})",
            OpCode.Div         => $"R({a}) = R({b}) / R({c})",
            OpCode.Mod         => $"R({a}) = R({b}) % R({c})",
            OpCode.Pow         => $"R({a}) = R({b}) ** R({c})",
            OpCode.Neg         => $"R({a}) = -R({b})",
            OpCode.AddI        => $"R({a}) = R({a}) + {sbx}",

            // Bitwise
            OpCode.BAnd        => $"R({a}) = R({b}) & R({c})",
            OpCode.BOr         => $"R({a}) = R({b}) | R({c})",
            OpCode.BXor        => $"R({a}) = R({b}) ^ R({c})",
            OpCode.BNot        => $"R({a}) = ~R({b})",
            OpCode.Shl         => $"R({a}) = R({b}) << R({c})",
            OpCode.Shr         => $"R({a}) = R({b}) >> R({c})",

            // Comparisons
            OpCode.Eq          => $"R({a}) = R({b}) == R({c})",
            OpCode.Ne          => $"R({a}) = R({b}) != R({c})",
            OpCode.Lt          => $"R({a}) = R({b}) < R({c})",
            OpCode.Le          => $"R({a}) = R({b}) <= R({c})",
            OpCode.Gt          => $"R({a}) = R({b}) > R({c})",
            OpCode.Ge          => $"R({a}) = R({b}) >= R({c})",

            // Logic
            OpCode.Not         => $"R({a}) = !R({b})",
            OpCode.TestSet     => $"if IsTruthy(R({b})) == {c} then R({a}) = R({b}) else skip",
            OpCode.Test        => $"if IsTruthy(R({a})) != {c} then skip",

            // Jumps
            OpCode.Jmp         => $"{GetLabelRef(labels, idx + 1 + sbx)}  ; offset {sbx:+0;-0}",
            OpCode.JmpFalse    => $"if !R({a}) -> {GetLabelRef(labels, idx + 1 + sbx)}  ; offset {sbx:+0;-0}",
            OpCode.JmpTrue     => $"if R({a}) -> {GetLabelRef(labels, idx + 1 + sbx)}  ; offset {sbx:+0;-0}",
            OpCode.Loop        => $"-> {GetLabelRef(labels, idx + 1 + sbx)}  ; offset {sbx:+0;-0}",

            // Calls
            OpCode.Call        => $"R({a}) = R({a})(R({a}+1)..R({a}+{c}))",
            OpCode.Return      => b != 0 ? $"return R({a})" : "return null",
            OpCode.CallSpread  => $"R({a}) = R({a})(... spread)",

            // Iteration
            OpCode.ForPrep     => $"R({a}) -= R({a}+2) ; -> {GetLabelRef(labels, idx + 1 + sbx)}",
            OpCode.ForLoop     => $"R({a}) += R({a}+2) ; if R({a}) <= R({a}+1) -> {GetLabelRef(labels, idx + 1 + sbx)}",
            OpCode.IterPrep    => $"R({a})..R({a}+2) = iter(R({a}))",
            OpCode.IterLoop    => $"iter step R({a}) ; -> {GetLabelRef(labels, idx + 1 + sbx)}",

            // Tables
            OpCode.GetTable    => $"R({a}) = R({b})[R({c})]",
            OpCode.SetTable    => $"R({a})[R({b})] = R({c})",
            OpCode.GetField    => $"R({a}) = R({b}).K({c})  ; \"{FormatFieldName(chunk, c)}\"",
            OpCode.SetField    => $"R({a}).K({b}) = R({c})  ; \"{FormatFieldName(chunk, b)}\"",
            OpCode.Self        => $"R({a}+1) = R({b}); R({a}) = R({b}).K({c})  ; \"{FormatFieldName(chunk, c)}\"",

            // Collections
            OpCode.NewArray    => $"R({a}) = [{b} elems from R({a}+1)..R({a}+{b})]",
            OpCode.NewDict     => $"R({a}) = dict({b} pairs from R({a}+1)..R({a}+{b * 2}))",
            OpCode.NewRange    => $"R({a}) = range(R({b}), R({c}))",
            OpCode.Spread      => $"spread R({b}) -> R({a})",
            OpCode.Destructure => $"destructure R({a}) per K({bx})",

            // Closures & Types
            OpCode.Closure     => $"R({a}) = closure K({bx})  ; {FormatConstant(bx < chunk.Constants.Length ? chunk.Constants[bx] : default)}",
            OpCode.NewStruct   => $"R({a}) = new K({b})({c} fields from R({a}+1)..)",
            OpCode.TypeOf      => $"R({a}) = typeof(R({b}))",
            OpCode.Is          => $"R({a}) = R({b}) is K({c})/R({c})",

            // Error handling
            OpCode.TryBegin    => $"try catch R({a}) at {GetLabelRef(labels, idx + 1 + sbx)}",
            OpCode.TryEnd      => "try.end",
            OpCode.Throw       => $"throw R({a})",
            OpCode.TryExpr     => $"R({a}) = try? R({b})",

            // Type decls
            OpCode.StructDecl  => $"R({a}) = StructDecl K({bx})",
            OpCode.EnumDecl    => $"R({a}) = EnumDecl K({bx})",
            OpCode.IfaceDecl   => $"R({a}) = IfaceDecl K({bx})",
            OpCode.Extend      => $"Extend K({bx}) with methods from R({a}+1)..",

            // Shell
            OpCode.Command     => $"R({a}) = command({b} parts from R({a}+1)..) flags={c}",
            OpCode.Pipe        => $"R({a}) = R({b}) | R({c})",
            OpCode.Redirect    => $"R({a}) = R({a}) >{(c == 0 ? "" : ">")} R({c}) flags={b}",
            OpCode.Interpolate => $"R({a}) = interpolate({b} parts from R({a}+1)..R({a}+{b}))",

            // Modules
            OpCode.Import      => $"Import path=R({a}) meta=K({bx}) -> R({a}+1)..",
            OpCode.ImportAs    => $"ImportAs path=R({a}) meta=K({bx}) -> R({a}+1)",

            // Misc
            OpCode.In          => $"R({a}) = R({b}) in R({c})",
            OpCode.Switch      => $"switch R({a}) table=K({bx})",
            OpCode.ElevateBegin=> $"R({a}) = elevate(R({b}))",
            OpCode.ElevateEnd  => "elevate.end",
            OpCode.Retry       => $"retry meta=K({bx})",
            OpCode.Await       => $"R({a}) = await R({b})",

            _ => fmt switch
            {
                InstrFmt.ABC  => $"R({a}), R({b}), R({c})",
                InstrFmt.ABx  => $"R({a}), K({bx})",
                InstrFmt.AsBx => $"R({a}), {sbx:+0;-0}",
                InstrFmt.Ax   => "",
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
        }

        var labels = new Dictionary<int, string>();
        int labelCount = 0;
        foreach (int t in targets)
            labels[t] = $"L{labelCount++}";
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
