namespace Stash.Bytecode.Optimization;

/// <summary>
/// Shared opcode classification helpers used by CFG construction and linear lowering.
/// </summary>
internal static class CfgOpcodeInfo
{
    /// <summary>
    /// Returns true if <paramref name="op"/> is followed by exactly one raw companion word
    /// (GetFieldIC or CallBuiltIn).  PipeChain and Closure have variable-length companion
    /// words and must be handled separately by the CFG builder's pre-scan.
    /// </summary>
    public static bool IsCompanionBearer(OpCode op)
        => op == OpCode.GetFieldIC || op == OpCode.CallBuiltIn;

    /// <summary>Returns true if <paramref name="op"/> carries an sBx relative jump offset.</summary>
    public static bool IsJumpWithSBx(OpCode op) => op switch
    {
        OpCode.Jmp or OpCode.JmpFalse or OpCode.JmpTrue or OpCode.Loop
            or OpCode.ForPrep or OpCode.ForLoop or OpCode.ForPrepII or OpCode.ForLoopII
            or OpCode.IterLoop or OpCode.TryBegin => true,
        _ => false,
    };

    /// <summary>
    /// Returns true if <paramref name="op"/> terminates a basic block, meaning the
    /// instruction immediately following it (if any) is a block leader.
    /// </summary>
    public static bool IsBlockTerminator(OpCode op) => op switch
    {
        OpCode.Jmp or OpCode.JmpFalse or OpCode.JmpTrue or OpCode.Loop
            or OpCode.Return or OpCode.Throw or OpCode.Rethrow
            or OpCode.ForPrep or OpCode.ForLoop or OpCode.ForPrepII or OpCode.ForLoopII
            or OpCode.IterLoop or OpCode.TryBegin or OpCode.TryEnd => true,
        _ => false,
    };
}
