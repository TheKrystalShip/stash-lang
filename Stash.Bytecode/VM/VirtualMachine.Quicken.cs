using System.Runtime.CompilerServices;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Quickening infrastructure: activation, immediate speculation,
/// and de-specialization helpers for adaptive bytecode specialization.
/// </summary>
public sealed partial class VirtualMachine
{
    // ── Opcode Mapping ──

    private static readonly (OpCode generic, OpCode specialized)[] SpecializableBinaryOps =
    [
        (OpCode.Add, OpCode.AddII),
        (OpCode.Sub, OpCode.SubII),
        (OpCode.Mul, OpCode.MulII),
        (OpCode.Div, OpCode.DivII),
        (OpCode.Mod, OpCode.ModII),
        (OpCode.Lt, OpCode.LtII),
        (OpCode.Le, OpCode.LeII),
        (OpCode.Gt, OpCode.GtII),
        (OpCode.Ge, OpCode.GeII),
        (OpCode.Eq, OpCode.EqII),
        (OpCode.Ne, OpCode.NeII),
    ];

    // ── Chunk Activation ──

    /// <summary>
    /// Activate quickening for a chunk by immediately specializing all eligible opcodes.
    /// Uses speculative specialization: assume integer operands, de-specialize on guard failure.
    /// Called once per chunk (top-level: immediately, named functions: 2nd call).
    /// </summary>
    private static void QuickenChunk(Chunk chunk)
    {
        // Allocate counters for de-specialization tracking only
        byte[] counters = new byte[chunk.Code.Length];
        chunk.QuickenCounters = counters;

        uint[] code = chunk.Code;
        for (int i = 0; i < code.Length; i++)
        {
            OpCode op = Instruction.GetOp(code[i]);

            // Specialize binary arithmetic/comparison ops
            for (int j = 0; j < SpecializableBinaryOps.Length; j++)
            {
                if (op == SpecializableBinaryOps[j].generic)
                {
                    code[i] = Instruction.PatchOp(code[i], SpecializableBinaryOps[j].specialized);
                    break;
                }
            }

            // Specialize ForPrep/ForLoop pairs
            if (op == OpCode.ForPrep)
            {
                code[i] = Instruction.PatchOp(code[i], OpCode.ForPrepII);
                int sBx = Instruction.GetSBx(code[i]);
                int forLoopIp = i + 1 + sBx;
                if (forLoopIp >= 0 && forLoopIp < code.Length
                    && Instruction.GetOp(code[forLoopIp]) == OpCode.ForLoop)
                {
                    code[forLoopIp] = Instruction.PatchOp(code[forLoopIp], OpCode.ForLoopII);
                }
            }

            // Skip companion words for IC-based instructions
            if (op is OpCode.GetFieldIC or OpCode.CallBuiltIn)
                i++;
        }
    }

    // ── De-specialization ──

    /// <summary>
    /// Revert a specialized opcode back to its generic equivalent.
    /// Uses escalating response: 1st de-spec → can be re-specialized next activation,
    /// 2nd de-spec → mark as saturated (permanent generic).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DeSpecialize(Chunk chunk, int ip, OpCode genericOp)
    {
        chunk.Code[ip] = Instruction.PatchOp(chunk.Code[ip], genericOp);

        byte[]? counters = chunk.QuickenCounters;
        if (counters is null) return;

        if ((counters[ip] & 0x80) != 0)
            counters[ip] = 255; // Saturated: permanent generic
        else
            counters[ip] = 0x80; // Flag first de-spec
    }

    /// <summary>
    /// Revert both ForPrepII→ForPrep and ForLoopII→ForLoop when ForPrepII guard fails.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DeSpecializeForLoop(Chunk chunk, int prepIp, int sBx)
    {
        // Revert ForPrepII → ForPrep
        chunk.Code[prepIp] = Instruction.PatchOp(chunk.Code[prepIp], OpCode.ForPrep);

        // Revert ForLoopII → ForLoop
        int forLoopIp = prepIp + 1 + sBx;
        if (forLoopIp >= 0 && forLoopIp < chunk.Code.Length
            && Instruction.GetOp(chunk.Code[forLoopIp]) == OpCode.ForLoopII)
        {
            chunk.Code[forLoopIp] = Instruction.PatchOp(chunk.Code[forLoopIp], OpCode.ForLoop);
        }

        // Apply escalation to ForPrep's counter
        byte[]? counters = chunk.QuickenCounters;
        if (counters is not null)
        {
            if ((counters[prepIp] & 0x80) != 0)
                counters[prepIp] = 255; // Saturated
            else
                counters[prepIp] = 0x80; // Flag first de-spec
        }
    }
}
