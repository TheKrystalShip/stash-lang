using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Options that control how <see cref="Disassembler"/> formats its output.
/// </summary>
public class DisassemblerOptions
{
    /// <summary>When true, use compact format (no hex bytes, no source annotations, no preamble sections). Default: false.</summary>
    public bool Compact { get; set; }

    /// <summary>When true, emit ANSI color codes. Default: false.</summary>
    public bool Color { get; set; }

    /// <summary>Original source text for inline source annotations. Null if unavailable.</summary>
    public string? SourceText { get; set; }
}

/// <summary>
/// Produces human-readable disassembly of <see cref="Chunk"/> bytecode.
/// Inspired by objdump/javap/luac with hex offsets, raw bytes, named references, jump labels, and section headers.
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
        [OpCode.Const]                = "const",
        [OpCode.Null]                 = "null",
        [OpCode.True]                 = "true",
        [OpCode.False]                = "false",
        [OpCode.Pop]                  = "pop",
        [OpCode.Dup]                  = "dup",
        [OpCode.LoadLocal]            = "load.local",
        [OpCode.StoreLocal]           = "store.local",
        [OpCode.LoadGlobal]           = "load.global",
        [OpCode.StoreGlobal]          = "store.global",
        [OpCode.LoadUpvalue]          = "load.upvalue",
        [OpCode.StoreUpvalue]         = "store.upvalue",
        [OpCode.Add]                  = "add",
        [OpCode.Subtract]             = "sub",
        [OpCode.Multiply]             = "mul",
        [OpCode.Divide]               = "div",
        [OpCode.Modulo]               = "mod",
        [OpCode.Power]                = "pow",
        [OpCode.Negate]               = "neg",
        [OpCode.BitAnd]               = "bit.and",
        [OpCode.BitOr]                = "bit.or",
        [OpCode.BitXor]               = "bit.xor",
        [OpCode.BitNot]               = "bit.not",
        [OpCode.ShiftLeft]            = "shl",
        [OpCode.ShiftRight]           = "shr",
        [OpCode.Equal]                = "eq",
        [OpCode.NotEqual]             = "neq",
        [OpCode.LessThan]             = "lt",
        [OpCode.LessEqual]            = "le",
        [OpCode.GreaterThan]          = "gt",
        [OpCode.GreaterEqual]         = "ge",
        [OpCode.Not]                  = "not",
        [OpCode.And]                  = "and",
        [OpCode.Or]                   = "or",
        [OpCode.NullCoalesce]         = "null.coal",
        [OpCode.Jump]                 = "jmp",
        [OpCode.JumpTrue]             = "jmp.true",
        [OpCode.JumpFalse]            = "jmp.false",
        [OpCode.Loop]                 = "loop",
        [OpCode.Call]                 = "call",
        [OpCode.Return]               = "ret",
        [OpCode.Closure]              = "closure",
        [OpCode.Array]                = "array",
        [OpCode.Dict]                 = "dict",
        [OpCode.Range]                = "range",
        [OpCode.Spread]               = "spread",
        [OpCode.GetField]             = "get.field",
        [OpCode.SetField]             = "set.field",
        [OpCode.GetIndex]             = "get.index",
        [OpCode.SetIndex]             = "set.index",
        [OpCode.StructDecl]           = "struct.decl",
        [OpCode.StructInit]           = "struct.init",
        [OpCode.EnumDecl]             = "enum.decl",
        [OpCode.InterfaceDecl]        = "iface.decl",
        [OpCode.Extend]               = "extend",
        [OpCode.Is]                   = "is",
        [OpCode.Interpolate]          = "interpolate",
        [OpCode.Command]              = "command",
        [OpCode.Pipe]                 = "pipe",
        [OpCode.Redirect]             = "redirect",
        [OpCode.Import]               = "import",
        [OpCode.ImportAs]             = "import.as",
        [OpCode.Destructure]          = "destructure",
        [OpCode.Throw]                = "throw",
        [OpCode.TryBegin]             = "try.begin",
        [OpCode.TryEnd]               = "try.end",
        [OpCode.TryExpr]              = "try.expr",
        [OpCode.Await]                = "await",
        [OpCode.PreIncrement]         = "pre.inc",
        [OpCode.PreDecrement]         = "pre.dec",
        [OpCode.PostIncrement]        = "post.inc",
        [OpCode.PostDecrement]        = "post.dec",
        [OpCode.Switch]               = "switch",
        [OpCode.ElevateBegin]         = "elevate.begin",
        [OpCode.ElevateEnd]           = "elevate.end",
        [OpCode.Retry]                = "retry",
        [OpCode.Iterator]             = "iterator",
        [OpCode.Iterate]              = "iterate",
        [OpCode.In]                   = "in",
        [OpCode.ArgMark]              = "arg.mark",
        [OpCode.CallSpread]           = "call.spread",
        [OpCode.CloseUpvalue]         = "close.upvalue",
        [OpCode.InitConstGlobal]      = "init.const",
        [OpCode.CheckNumeric]         = "check.numeric",
        [OpCode.LoadLocal0]           = "load.local.0",
        [OpCode.LoadLocal1]           = "load.local.1",
        [OpCode.LoadLocal2]           = "load.local.2",
        [OpCode.LoadLocal3]           = "load.local.3",
        [OpCode.Call0]                = "call.0",
        [OpCode.Call1]                = "call.1",
        [OpCode.Call2]                = "call.2",
        [OpCode.LL_Add]               = "ll.add",
        [OpCode.LC_Add]               = "lc.add",
        [OpCode.LC_LessThan]          = "lc.lt",
        [OpCode.DupStoreLocalPop]     = "dup.store.pop",
        [OpCode.LL_LessThan]          = "ll.lt",
        [OpCode.LC_Subtract]          = "lc.sub",
        [OpCode.L_Return]             = "ret.local",
        [OpCode.GetFieldIC]           = "get.field.ic",
        [OpCode.LessThanJumpFalse]    = "jmp.lt.false",
        [OpCode.GreaterThanJumpFalse] = "jmp.gt.false",
        [OpCode.LessEqualJumpFalse]   = "jmp.le.false",
        [OpCode.GreaterEqualJumpFalse]= "jmp.ge.false",
        [OpCode.EqualJumpFalse]       = "jmp.eq.false",
        [OpCode.NotEqualJumpFalse]    = "jmp.neq.false",
        [OpCode.IncrLocal]            = "incr.local",
        [OpCode.DecrLocal]            = "decr.local",
    };

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Disassemble a chunk with default options (verbose, no color, no source text).</summary>
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

    // ─── Core Dispatch ────────────────────────────────────────────────────────

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
            EmitCompactHeader(chunk, options, sb);
            EmitCompactCode(chunk, options, sb);
            return;
        }

        EmitFullHeader(chunk, options, sb);
        EmitConstSection(chunk, options, sb);
        EmitGlobalsSection(chunk, options, sb);

        var labels = CollectLabels(chunk);
        string[]? sourceLines = options.SourceText?.Split('\n');

        EmitCodeSection(chunk, options, labels, sourceLines, sb);
    }

    // ─── Header Emission ──────────────────────────────────────────────────────

    private static void EmitFullHeader(Chunk chunk, DisassemblerOptions options, StringBuilder sb)
    {
        string name = chunk.Name ?? "<script>";

        string titleLine = $"; ─── {name} ";
        string titleFill = new string('─', Math.Max(0, 66 - titleLine.Length));
        sb.AppendLine(Col(options, $"{titleLine}{titleFill}", Ansi.BoldMagenta));

        if (chunk.Name == null)
        {
            // Top-level script
            string? sourceFile = chunk.SourceMap.Count > 0
                ? System.IO.Path.GetFileName(chunk.SourceMap[0].Span.File)
                : null;
            if (sourceFile != null)
                sb.AppendLine($"; source: {sourceFile}");
            sb.AppendLine($"; locals: {chunk.LocalCount}   globals: {chunk.GlobalSlotCount}   constants: {chunk.Constants.Length}");
        }
        else
        {
            string arityInfo = chunk.MinArity == chunk.Arity
                ? chunk.Arity.ToString()
                : $"{chunk.MinArity}..{chunk.Arity}";
            sb.AppendLine($"; arity: {arityInfo}   locals: {chunk.LocalCount}   upvalues: {chunk.Upvalues.Length}");

            if (IsLambdaName(chunk.Name) && chunk.SourceMap.Count > 0)
            {
                int line = chunk.SourceMap[0].Span.StartLine;
                sb.AppendLine($"; defined at line {line}");
            }
        }

        sb.AppendLine(Col(options, $"; {new string('─', 64)}", Ansi.BoldMagenta));
        sb.AppendLine();
    }

    private static void EmitCompactHeader(Chunk chunk, DisassemblerOptions options, StringBuilder sb)
    {
        string name = chunk.Name ?? "<script>";
        sb.AppendLine(Col(options, $"== {name} ==", Ansi.BoldMagenta));
    }

    // ─── .const: Section ──────────────────────────────────────────────────────

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

    // ─── .globals: Section ────────────────────────────────────────────────────

    private static void EmitGlobalsSection(Chunk chunk, DisassemblerOptions options, StringBuilder sb)
    {
        if (chunk.GlobalNameTable == null || chunk.GlobalSlotCount == 0)
            return;

        ScanGlobalInfo(chunk, out var constSlots, out var fnSlots, out var assignedSlots);

        sb.AppendLine(Col(options, ".globals:", Ansi.BoldMagenta));
        for (int i = 0; i < chunk.GlobalSlotCount; i++)
        {
            string globalName = chunk.GlobalNameTable != null && i < chunk.GlobalNameTable.Length
                ? chunk.GlobalNameTable[i]
                : i.ToString();

            string annotation = "";
            if (fnSlots.Contains(i))
                annotation = " ; const (fn)";
            else if (constSlots.Contains(i))
                annotation = " ; const";
            else if (!assignedSlots.Contains(i))
                annotation = " ; built-in";

            string entry = $"  ${i} = {globalName,-28}";
            if (annotation.Length > 0)
                entry += Col(options, annotation, Ansi.Green);

            sb.AppendLine(entry);
        }
        sb.AppendLine();
    }

    private static void ScanGlobalInfo(Chunk chunk,
        out HashSet<int> constSlots,
        out HashSet<int> fnSlots,
        out HashSet<int> assignedSlots)
    {
        constSlots    = new HashSet<int>();
        fnSlots       = new HashSet<int>();
        assignedSlots = new HashSet<int>();

        int offset = 0;
        OpCode prevOp = (OpCode)255;

        while (offset < chunk.Code.Length)
        {
            var opCode = (OpCode)chunk.Code[offset];

            switch (opCode)
            {
                case OpCode.InitConstGlobal:
                {
                    ushort slot = ReadU16(chunk.Code, offset + 1);
                    constSlots.Add(slot);
                    assignedSlots.Add(slot);
                    if (prevOp == OpCode.Closure)
                        fnSlots.Add(slot);
                    break;
                }
                case OpCode.StoreGlobal:
                {
                    ushort slot = ReadU16(chunk.Code, offset + 1);
                    assignedSlots.Add(slot);
                    break;
                }
            }

            prevOp = opCode;
            offset += InstructionSize(chunk, offset);
        }
    }

    // ─── .code: Section (Full Mode) ───────────────────────────────────────────

    private static void EmitCodeSection(Chunk chunk, DisassemblerOptions options,
        Dictionary<int, string> labels, string[]? sourceLines, StringBuilder sb)
    {
        sb.AppendLine(Col(options, ".code:", Ansi.BoldMagenta));

        int offset  = 0;
        int prevLine = -1;

        while (offset < chunk.Code.Length)
        {
            if (labels.TryGetValue(offset, out string? labelName))
                sb.AppendLine(Col(options, $"  {labelName}:", Ansi.Yellow));

            int currentLine = chunk.SourceMap.GetLine(offset);
            if (currentLine != -1 && currentLine != prevLine)
            {
                sb.AppendLine(Col(options, BuildSourceAnnotation(currentLine, sourceLines), Ansi.DimGreen));
                prevLine = currentLine;
            }

            offset = EmitInstruction(chunk, options, labels, offset, sb);
        }
    }

    // ─── .code: Section (Compact Mode) ────────────────────────────────────────

    private static void EmitCompactCode(Chunk chunk, DisassemblerOptions options, StringBuilder sb)
    {
        var labels = CollectLabels(chunk);
        int offset = 0;

        while (offset < chunk.Code.Length)
        {
            if (labels.TryGetValue(offset, out string? labelName))
                sb.AppendLine(Col(options, $"{labelName}:", Ansi.Yellow));

            offset = EmitCompactInstruction(chunk, options, labels, offset, sb);
        }
    }

    // ─── Full Instruction Emission ────────────────────────────────────────────

    private static int EmitInstruction(Chunk chunk, DisassemblerOptions options,
        Dictionary<int, string> labels, int offset, StringBuilder sb)
    {
        byte[] code   = chunk.Code;
        var opCode    = (OpCode)code[offset];
        string opName = _opNames.TryGetValue(opCode, out string? n) ? n : opCode.ToString();
        int totalSize = InstructionSize(chunk, offset);

        string hexBytes = BuildHexBytes(code, offset, totalSize);

        string offsetStr = Col(options, $"{offset:x4}", Ansi.Dim);
        string hexStr    = Col(options, hexBytes, Ansi.Dim);
        string opStr     = Col(options, opName.PadRight(16), Ansi.Bold);

        string prefix = $"  {offsetStr}  {hexStr}  {opStr}";

        (string operand, string comment) = FormatOperand(chunk, options, labels, offset, opCode);

        var line = new StringBuilder(prefix);
        if (operand.Length > 0)
            line.Append(Col(options, operand, Ansi.Cyan));
        if (comment.Length > 0)
            line.Append(Col(options, $" ; {comment}", Ansi.Green));

        sb.AppendLine(line.ToString());

        // Closure: emit upvalue descriptors on continuation lines
        if (opCode == OpCode.Closure)
        {
            ushort constIdx = ReadU16(code, offset + 1);
            if (constIdx < chunk.Constants.Length && chunk.Constants[constIdx].AsObj is Chunk fn)
            {
                int uvOffset = offset + 3;
                for (int i = 0; i < fn.Upvalues.Length; i++)
                {
                    byte isLocal = code[uvOffset++];
                    byte index   = code[uvOffset++];
                    string kind  = isLocal != 0 ? "local" : "upvalue";
                    sb.AppendLine(Col(options, $"                              {kind} {index}", Ansi.Dim));
                }
            }
        }

        return offset + totalSize;
    }

    // ─── Compact Instruction Emission ─────────────────────────────────────────

    private static int EmitCompactInstruction(Chunk chunk, DisassemblerOptions options,
        Dictionary<int, string> labels, int offset, StringBuilder sb)
    {
        byte[] code   = chunk.Code;
        var opCode    = (OpCode)code[offset];
        string opName = _opNames.TryGetValue(opCode, out string? n) ? n : opCode.ToString();
        int totalSize = InstructionSize(chunk, offset);

        string offsetStr = Col(options, $"{offset:D4}", Ansi.Dim);
        string opStr     = Col(options, opName.PadRight(16), Ansi.Bold);

        (string operand, string _) = FormatOperand(chunk, options, labels, offset, opCode);

        string line = $"{offsetStr} {opStr}";
        if (operand.Length > 0)
            line += Col(options, operand, Ansi.Cyan);

        sb.AppendLine(line);

        if (opCode == OpCode.Closure)
        {
            ushort constIdx = ReadU16(code, offset + 1);
            if (constIdx < chunk.Constants.Length && chunk.Constants[constIdx].AsObj is Chunk fn)
            {
                int uvOffset = offset + 3;
                for (int i = 0; i < fn.Upvalues.Length; i++)
                {
                    byte isLocal = code[uvOffset++];
                    byte index   = code[uvOffset++];
                    string kind  = isLocal != 0 ? "local" : "upvalue";
                    sb.AppendLine($"                  {kind} {index}");
                }
            }
        }

        return offset + totalSize;
    }

    // ─── Operand Formatting ───────────────────────────────────────────────────

    private static (string operand, string comment) FormatOperand(
        Chunk chunk, DisassemblerOptions options, Dictionary<int, string> labels,
        int offset, OpCode opCode)
    {
        byte[] code = chunk.Code;

        switch (opCode)
        {
            // ── Zero-operand opcodes ──────────────────────────────────────────
            case OpCode.Null:
            case OpCode.True:
            case OpCode.False:
            case OpCode.Pop:
            case OpCode.Dup:
            case OpCode.Add:
            case OpCode.Subtract:
            case OpCode.Multiply:
            case OpCode.Divide:
            case OpCode.Modulo:
            case OpCode.Power:
            case OpCode.Negate:
            case OpCode.BitAnd:
            case OpCode.BitOr:
            case OpCode.BitXor:
            case OpCode.BitNot:
            case OpCode.ShiftLeft:
            case OpCode.ShiftRight:
            case OpCode.Equal:
            case OpCode.NotEqual:
            case OpCode.LessThan:
            case OpCode.LessEqual:
            case OpCode.GreaterThan:
            case OpCode.GreaterEqual:
            case OpCode.Not:
            case OpCode.Return:
            case OpCode.Range:
            case OpCode.Spread:
            case OpCode.GetIndex:
            case OpCode.SetIndex:
            case OpCode.Pipe:
            case OpCode.Throw:
            case OpCode.TryEnd:
            case OpCode.TryExpr:
            case OpCode.Await:
            case OpCode.PreIncrement:
            case OpCode.PreDecrement:
            case OpCode.PostIncrement:
            case OpCode.PostDecrement:
            case OpCode.ElevateBegin:
            case OpCode.ElevateEnd:
            case OpCode.Iterator:
            case OpCode.In:
            case OpCode.ArgMark:
            case OpCode.CallSpread:
            case OpCode.CheckNumeric:
            case OpCode.LoadLocal0:
            case OpCode.LoadLocal1:
            case OpCode.LoadLocal2:
            case OpCode.LoadLocal3:
            case OpCode.Call0:
            case OpCode.Call1:
            case OpCode.Call2:
                return ("", "");

            // ── 1-byte (u8) opcodes ───────────────────────────────────────────
            case OpCode.LoadLocal:
            case OpCode.StoreLocal:
            {
                byte slot = code[offset + 1];
                string name = GetLocalName(chunk, slot);
                return (slot.ToString(), name.Length > 0 ? name : "");
            }

            case OpCode.LoadUpvalue:
            case OpCode.StoreUpvalue:
            {
                byte slot = code[offset + 1];
                string name = GetUpvalueName(chunk, slot);
                return (slot.ToString(), name.Length > 0 ? name : "");
            }

            case OpCode.Call:
            case OpCode.Redirect:
            case OpCode.CloseUpvalue:
                return (code[offset + 1].ToString(), "");

            // ── 2-byte (u16) opcodes ──────────────────────────────────────────
            case OpCode.Const:
            {
                ushort idx = ReadU16(code, offset + 1);
                return (idx < chunk.Constants.Length
                    ? FormatConstant(chunk.Constants[idx])
                    : idx.ToString(), "");
            }

            case OpCode.LoadGlobal:
            case OpCode.StoreGlobal:
            case OpCode.InitConstGlobal:
            {
                ushort slot = ReadU16(code, offset + 1);
                return (FormatGlobalRef(chunk, slot), "");
            }

            case OpCode.GetField:
            case OpCode.SetField:
            {
                ushort idx = ReadU16(code, offset + 1);
                string fieldName = idx < chunk.Constants.Length && chunk.Constants[idx].AsObj is string fn
                    ? $"\"{EscapeString(fn)}\""
                    : idx.ToString();
                return (fieldName, "");
            }

            case OpCode.Is:
            {
                ushort idx = ReadU16(code, offset + 1);
                string typeName = idx < chunk.Constants.Length && chunk.Constants[idx].AsObj is string tn
                    ? tn
                    : idx.ToString();
                return (typeName, "");
            }

            case OpCode.Closure:
            {
                ushort idx = ReadU16(code, offset + 1);
                string fnName = idx < chunk.Constants.Length && chunk.Constants[idx].AsObj is Chunk fn
                    ? $"<fn {fn.Name ?? "<lambda>"}>"
                    : idx.ToString();
                return (fnName, "");
            }

            case OpCode.Jump:
            case OpCode.JumpTrue:
            case OpCode.JumpFalse:
            {
                ushort raw    = ReadU16(code, offset + 1);
                int target    = offset + 3 + (short)raw;
                string label  = GetLabelRef(labels, target, options);
                return (label, $"-> {target:x4}");
            }

            case OpCode.Loop:
            {
                ushort raw   = ReadU16(code, offset + 1);
                int target   = offset + 3 - raw;
                string label = GetLabelRef(labels, target, options);
                return (label, $"-> {target:x4}");
            }

            case OpCode.And:
            case OpCode.Or:
            case OpCode.NullCoalesce:
            case OpCode.Iterate:
            {
                ushort raw = ReadU16(code, offset + 1);
                int target = offset + 3 + (short)raw;
                return ("", $"-> {target:x4}");
            }

            case OpCode.TryBegin:
            {
                ushort raw = ReadU16(code, offset + 1);
                int target = offset + 3 + (short)raw;
                return ("", $"-> {target:x4}");
            }

            case OpCode.Array:
            case OpCode.Dict:
            case OpCode.Interpolate:
            case OpCode.StructInit:
            {
                ushort count = ReadU16(code, offset + 1);
                return (count.ToString(), "");
            }

            case OpCode.StructDecl:
            case OpCode.EnumDecl:
            case OpCode.InterfaceDecl:
            case OpCode.Extend:
            {
                ushort idx = ReadU16(code, offset + 1);
                string typeName = idx < chunk.Constants.Length && chunk.Constants[idx].AsObj is string tn
                    ? tn
                    : idx.ToString();
                return (typeName, "");
            }

            case OpCode.Command:
            case OpCode.Import:
            case OpCode.ImportAs:
            case OpCode.Destructure:
            case OpCode.Switch:
            case OpCode.Retry:
                return (ReadU16(code, offset + 1).ToString(), "");

            // ── Superinstructions ─────────────────────────────────────────────
            case OpCode.LL_Add:
            case OpCode.LL_LessThan:
            {
                byte slot1 = code[offset + 1];
                byte slot2 = code[offset + 2];
                string n1  = GetLocalName(chunk, slot1);
                string n2  = GetLocalName(chunk, slot2);
                string op  = opCode == OpCode.LL_Add ? "+" : "<";
                string s1  = n1.Length > 0 ? $"local[{slot1}]({n1})" : $"local[{slot1}]";
                string s2  = n2.Length > 0 ? $"local[{slot2}]({n2})" : $"local[{slot2}]";
                return ($"local[{slot1}], local[{slot2}]", $"{s1} {op} {s2}");
            }

            case OpCode.LC_Add:
            case OpCode.LC_Subtract:
            case OpCode.LC_LessThan:
            {
                byte slot       = code[offset + 1];
                ushort constIdx = ReadU16(code, offset + 2);
                string localName = GetLocalName(chunk, slot);
                string op = opCode switch
                {
                    OpCode.LC_Add      => "+",
                    OpCode.LC_Subtract => "-",
                    _                  => "<",
                };
                string constVal = constIdx < chunk.Constants.Length
                    ? FormatConstant(chunk.Constants[constIdx])
                    : constIdx.ToString();
                string nm      = localName.Length > 0 ? $"local[{slot}]({localName})" : $"local[{slot}]";
                return ($"local[{slot}], {constVal}", $"{nm} {op} {constVal}");
            }

            case OpCode.DupStoreLocalPop:
            {
                byte slot        = code[offset + 1];
                string localName = GetLocalName(chunk, slot);
                string nm        = localName.Length > 0 ? $"{localName}" : $"local[{slot}]";
                return (slot.ToString(), $"store-and-pop {nm}");
            }

            case OpCode.L_Return:
            {
                byte slot        = code[offset + 1];
                string localName = GetLocalName(chunk, slot);
                string nm        = localName.Length > 0 ? $"{localName}" : $"local[{slot}]";
                return (slot.ToString(), $"return {nm}");
            }

            case OpCode.IncrLocal:
            {
                byte slot        = code[offset + 1];
                string localName = GetLocalName(chunk, slot);
                string nm        = localName.Length > 0 ? localName : $"local[{slot}]";
                return (slot.ToString(), $"{nm}++");
            }

            case OpCode.DecrLocal:
            {
                byte slot        = code[offset + 1];
                string localName = GetLocalName(chunk, slot);
                string nm        = localName.Length > 0 ? localName : $"local[{slot}]";
                return (slot.ToString(), $"{nm}--");
            }

            case OpCode.GetFieldIC:
            {
                ushort nameIdx = ReadU16(code, offset + 1);
                ushort icSlot  = ReadU16(code, offset + 3);
                string fieldName = nameIdx < chunk.Constants.Length && chunk.Constants[nameIdx].AsObj is string fn
                    ? $"\"{EscapeString(fn)}\""
                    : nameIdx.ToString();
                return (fieldName, $"[IC:{icSlot}]");
            }

            case OpCode.LessThanJumpFalse:
            case OpCode.GreaterThanJumpFalse:
            case OpCode.LessEqualJumpFalse:
            case OpCode.GreaterEqualJumpFalse:
            case OpCode.EqualJumpFalse:
            case OpCode.NotEqualJumpFalse:
            {
                ushort raw   = ReadU16(code, offset + 1);
                int target   = offset + 3 + (short)raw;
                string label = GetLabelRef(labels, target, options);
                return (label, $"-> {target:x4}");
            }

            default:
            {
                int operandSize = OpCodeInfo.OperandSize(opCode);
                if (operandSize == 1)
                    return (code[offset + 1].ToString(), "");
                if (operandSize == 2)
                    return (ReadU16(code, offset + 1).ToString(), "");
                return ("", "");
            }
        }
    }

    // ─── Label Collection (Pass 1) ────────────────────────────────────────────

    private static Dictionary<int, string> CollectLabels(Chunk chunk)
    {
        var targets = new Dictionary<int, bool>(); // target offset → is backward?

        int offset = 0;
        while (offset < chunk.Code.Length)
        {
            var opCode = (OpCode)chunk.Code[offset];
            int? target = ComputeJumpTarget(chunk.Code, offset, opCode);

            if (target.HasValue)
            {
                bool isBackward = target.Value < offset;
                if (!targets.ContainsKey(target.Value))
                    targets[target.Value] = isBackward;
                else if (isBackward)
                    targets[target.Value] = true; // backward takes precedence
            }

            offset += InstructionSize(chunk, offset);
        }

        var labels = new Dictionary<int, string>();
        int loopCount    = 0;
        int forwardCount = 0;

        foreach (var (targetOffset, isBackward) in targets.OrderBy(kv => kv.Key))
        {
            if (isBackward)
            {
                labels[targetOffset] = loopCount == 0 ? ".loop_start" : $".loop_start_{loopCount}";
                loopCount++;
            }
            else
            {
                labels[targetOffset] = $".L{forwardCount}";
                forwardCount++;
            }
        }

        return labels;
    }

    private static int? ComputeJumpTarget(byte[] code, int offset, OpCode opCode)
    {
        switch (opCode)
        {
            case OpCode.Jump:
            case OpCode.JumpTrue:
            case OpCode.JumpFalse:
            case OpCode.And:
            case OpCode.Or:
            case OpCode.NullCoalesce:
            case OpCode.Iterate:
            {
                ushort raw = ReadU16(code, offset + 1);
                return offset + 3 + (short)raw;
            }
            case OpCode.Loop:
            {
                ushort raw = ReadU16(code, offset + 1);
                return offset + 3 - raw;
            }
            case OpCode.LessThanJumpFalse:
            case OpCode.GreaterThanJumpFalse:
            case OpCode.LessEqualJumpFalse:
            case OpCode.GreaterEqualJumpFalse:
            case OpCode.EqualJumpFalse:
            case OpCode.NotEqualJumpFalse:
            {
                ushort raw = ReadU16(code, offset + 1);
                return offset + 3 + (short)raw;
            }
            default:
                return null;
        }
    }

    // ─── Instruction Size ──────────────────────────────────────────────────────

    private static int InstructionSize(Chunk chunk, int offset)
    {
        var opCode = (OpCode)chunk.Code[offset];
        int size   = 1 + OpCodeInfo.OperandSize(opCode);

        // Closure: opcode(1) + constIdx(2) + 2 bytes per upvalue descriptor
        if (opCode == OpCode.Closure)
        {
            ushort constIdx = ReadU16(chunk.Code, offset + 1);
            if (constIdx < chunk.Constants.Length && chunk.Constants[constIdx].AsObj is Chunk fn)
                size += fn.Upvalues.Length * 2;
        }

        return size;
    }

    // ─── Formatting Helpers ───────────────────────────────────────────────────

    private static string BuildHexBytes(byte[] code, int offset, int totalSize)
    {
        const int MaxBytesShown = 4;
        const int ColWidth      = 12;

        int bytesShown = Math.Min(totalSize, MaxBytesShown);
        bool truncated = totalSize > MaxBytesShown;

        var sb = new StringBuilder();
        for (int i = 0; i < bytesShown; i++)
        {
            if (i > 0) sb.Append(' ');
            if (truncated && i == bytesShown - 1)
            {
                sb.Append("...");
                break;
            }
            sb.Append(code[offset + i].ToString("x2"));
        }

        while (sb.Length < ColWidth) sb.Append(' ');
        return sb.ToString();
    }

    private static string BuildSourceAnnotation(int line, string[]? sourceLines)
    {
        const int TotalWidth = 68;

        string prefix;
        if (sourceLines != null && line - 1 < sourceLines.Length)
        {
            string lineText = sourceLines[line - 1].Trim();
            if (lineText.Length > 50)
                lineText = lineText[..47] + "...";
            prefix = $"  ; ── line {line}: {lineText} ";
        }
        else
        {
            prefix = $"  ; ── line {line} ";
        }

        int dashCount = Math.Max(4, TotalWidth - prefix.Length);
        return prefix + new string('─', dashCount);
    }

    private static string GetLabelRef(Dictionary<int, string> labels, int target, DisassemblerOptions options)
    {
        return labels.TryGetValue(target, out string? name)
            ? Col(options, name, Ansi.Yellow)
            : $"0x{target:x4}";
    }

    private static string FormatGlobalRef(Chunk chunk, ushort slot)
    {
        return chunk.GlobalNameTable != null && slot < chunk.GlobalNameTable.Length
            ? $"${chunk.GlobalNameTable[slot]}"
            : $"${slot}";
    }

    private static string GetLocalName(Chunk chunk, int slot)
    {
        return chunk.LocalNames != null && slot < chunk.LocalNames.Length
            ? chunk.LocalNames[slot] ?? ""
            : "";
    }

    private static string GetUpvalueName(Chunk chunk, int slot)
    {
        return chunk.UpvalueNames != null && slot < chunk.UpvalueNames.Length
            ? chunk.UpvalueNames[slot] ?? ""
            : "";
    }

    private static string FormatConstant(StashValue value) => value.Tag switch
    {
        StashValueTag.Null  => "null",
        StashValueTag.Bool  => value.AsBool ? "true" : "false",
        StashValueTag.Int   => value.AsInt.ToString(),
        StashValueTag.Float => value.AsFloat.ToString("G"),
        StashValueTag.Obj when value.AsObj is string s => $"\"{EscapeString(s)}\"",
        StashValueTag.Obj when value.AsObj is Chunk c  => $"<fn {c.Name ?? "<lambda>"}>",
        _                   => value.ToString() ?? "?",
    };

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t");

    private static ushort ReadU16(byte[] code, int offset) =>
        (ushort)((code[offset] << 8) | code[offset + 1]);

    private static bool IsLambdaName(string? name) =>
        name == null || name.StartsWith("<lambda", StringComparison.Ordinal);

    private static string Col(DisassemblerOptions options, string text, string ansiCode) =>
        options.Color ? $"{ansiCode}{text}{Ansi.Reset}" : text;
}
