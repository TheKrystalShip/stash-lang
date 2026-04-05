using System;
using System.Text;
using Stash.Common;

namespace Stash.Bytecode;

/// <summary>
/// Produces human-readable disassembly of <see cref="Chunk"/> bytecode for debugging.
/// </summary>
public static class Disassembler
{
    /// <summary>
    /// Disassemble an entire chunk into a multi-line string.
    /// </summary>
    public static string Disassemble(Chunk chunk)
    {
        var sb = new StringBuilder();
        string name = chunk.Name ?? "<script>";
        sb.AppendLine($"== {name} ==");

        int previousLine = -1;
        int offset = 0;

        while (offset < chunk.Code.Length)
        {
            offset = DisassembleInstruction(chunk, offset, previousLine, sb, out int currentLine);
            previousLine = currentLine;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Disassemble a single instruction at the given offset.
    /// Returns the offset of the next instruction.
    /// </summary>
    public static int DisassembleInstruction(Chunk chunk, int offset, int previousLine, StringBuilder sb, out int currentLine)
    {
        // Offset (4-digit decimal)
        sb.Append($"{offset:D4} ");

        // Line number or continuation marker
        currentLine = chunk.SourceMap.GetLine(offset);
        if (currentLine == previousLine || currentLine == -1)
            sb.Append("   | ");
        else
            sb.Append($"{currentLine,4} ");

        byte raw = chunk.Code[offset];
        var opCode = (OpCode)raw;
        int operandSize = OpCodeInfo.OperandSize(opCode);
        string opName = opCode.ToString();

        switch (operandSize)
        {
            case 0:
                sb.AppendLine(opName);
                return offset + 1;

            case 1:
            {
                byte operand = chunk.Code[offset + 1];
                sb.Append($"{opName,-16} {operand,4}");
                sb.AppendLine();
                return offset + 2;
            }

            case 2:
            {
                ushort operand = (ushort)((chunk.Code[offset + 1] << 8) | chunk.Code[offset + 2]);

                sb.Append($"{opName,-16} {operand,4}");

                switch (opCode)
                {
                    case OpCode.Const:
                        if (operand < chunk.Constants.Length)
                        {
                            StashValue value = chunk.Constants[operand];
                            sb.Append($"    ; {FormatConstant(value)}");
                        }
                        break;

                    case OpCode.LoadGlobal:
                    case OpCode.StoreGlobal:
                    case OpCode.GetField:
                    case OpCode.SetField:
                    case OpCode.Is:
                        if (operand < chunk.Constants.Length && chunk.Constants[operand].AsObj is string s)
                            sb.Append($"    ; {s}");
                        break;

                    case OpCode.Jump:
                    case OpCode.JumpTrue:
                    case OpCode.JumpFalse:
                    case OpCode.And:
                    case OpCode.Or:
                    case OpCode.NullCoalesce:
                    case OpCode.Iterate:
                    {
                        short signedOffset = (short)operand;
                        int target = offset + 3 + signedOffset;
                        sb.Append($"    ; -> {target:D4}");
                        break;
                    }

                    case OpCode.Loop:
                    {
                        int target = offset + 3 - operand;
                        sb.Append($"    ; -> {target:D4}");
                        break;
                    }

                    case OpCode.Closure:
                        if (operand < chunk.Constants.Length && chunk.Constants[operand].AsObj is Chunk fn)
                        {
                            string fnName = fn.Name ?? "<lambda>";
                            sb.Append($"    ; {fnName}");
                        }
                        break;
                }

                sb.AppendLine();

                // For Closure, also print upvalue descriptors that follow inline
                if (opCode == OpCode.Closure && operand < chunk.Constants.Length && chunk.Constants[operand].AsObj is Chunk closureFn)
                {
                    int nextOffset = offset + 3;
                    for (int i = 0; i < closureFn.Upvalues.Length; i++)
                    {
                        byte isLocal = chunk.Code[nextOffset++];
                        byte index = chunk.Code[nextOffset++];
                        string kind = isLocal != 0 ? "local" : "upvalue";
                        sb.AppendLine($"     |                  {kind} {index}");
                    }
                    return nextOffset;
                }

                return offset + 3;
            }

            default:
                sb.AppendLine($"Unknown opcode {raw}");
                return offset + 1;
        }
    }

    private static string FormatConstant(StashValue value) => value.Tag switch
    {
        StashValueTag.Null => "null",
        StashValueTag.Bool => value.AsBool ? "true" : "false",
        StashValueTag.Int => value.AsInt.ToString(),
        StashValueTag.Float => value.AsFloat.ToString("G"),
        StashValueTag.Obj when value.AsObj is string s => $"\"{EscapeString(s)}\"",
        StashValueTag.Obj when value.AsObj is Chunk c => $"<fn {c.Name ?? "<lambda>"}>",
        _ => value.ToString(),
    };

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}
