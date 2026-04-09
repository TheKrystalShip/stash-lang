namespace Stash.Bytecode;

using System.Runtime.CompilerServices;

/// <summary>
/// Static helpers for encoding and decoding 32-bit register-based instructions.
/// Encoding: all instructions are uint values.
/// ABC:  [op:8][A:8][B:8][C:8]
/// ABx:  [op:8][A:8][Bx:16]        (Bx unsigned)
/// AsBx: [op:8][A:8][sBx:16]       (sBx stored as sBx + SBXBIAS)
/// Ax:   [op:8][Ax:24]
/// </summary>
public static class Instruction
{
    // Bias for signed sBx field: stored as (sBx + Bias), range -32767..+32768
    public const int SBxBias = 32767;
    public const int SBxMax = 32768;
    public const int SBxMin = -32767;
    public const int BxMax = 65535;   // 16-bit unsigned max
    public const int AxMax = 16777215; // 24-bit unsigned max

    // === Encoding ===

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeABC(OpCode op, byte a, byte b, byte c)
        => (uint)op | ((uint)a << 8) | ((uint)b << 16) | ((uint)c << 24);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeABx(OpCode op, byte a, ushort bx)
        => (uint)op | ((uint)a << 8) | ((uint)bx << 16);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeAsBx(OpCode op, byte a, int sbx)
        => EncodeABx(op, a, (ushort)(sbx + SBxBias));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint EncodeAx(OpCode op, uint ax)
        => (uint)op | (ax << 8);

    // === Decoding ===

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OpCode GetOp(uint instruction)
        => (OpCode)(instruction & 0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetA(uint instruction)
        => (byte)((instruction >> 8) & 0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetB(uint instruction)
        => (byte)((instruction >> 16) & 0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetC(uint instruction)
        => (byte)((instruction >> 24) & 0xFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort GetBx(uint instruction)
        => (ushort)((instruction >> 16) & 0xFFFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetSBx(uint instruction)
        => (int)GetBx(instruction) - SBxBias;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetAx(uint instruction)
        => (instruction >> 8) & 0xFFFFFF;

    // === Patching (for jump targets) ===

    /// <summary>Replace the Bx/sBx field of an instruction, keeping op and A.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint PatchBx(uint instruction, ushort newBx)
        => (instruction & 0x0000FFFF) | ((uint)newBx << 16);

    /// <summary>Replace the sBx field (signed) of an instruction.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint PatchSBx(uint instruction, int newSBx)
        => PatchBx(instruction, (ushort)(newSBx + SBxBias));

    /// <summary>Replace the opcode byte of an instruction, keeping all operand fields.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint PatchOp(uint instruction, OpCode newOp)
        => (instruction & 0xFFFFFF00) | (uint)newOp;
}
