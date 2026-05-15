namespace Stash.Bytecode.Optimization;

/// <summary>
/// Shared opcode classification helpers used by CFG construction and linear lowering.
/// <para>
/// All classifications derive from <see cref="OpCodeMetadata"/> — the
/// <see cref="OpCodeAttribute"/> decoration on each enum member is the single source
/// of truth. Adding a new opcode without filling in the required metadata fails fast
/// at process start, so this helper cannot drift out of sync.
/// </para>
/// <para>
/// Undefined opcodes (e.g. raw companion-word payloads encountered while linearly
/// scanning a code array) return <c>false</c> from every predicate — matching the
/// silent-default behaviour of the previous switch-based implementation.
/// </para>
/// </summary>
internal static class CfgOpcodeInfo
{
    /// <summary>
    /// Returns true if <paramref name="op"/> is followed by exactly one raw companion word
    /// (GetFieldIC or CallBuiltIn).  PipeChain and Closure have variable-length companion
    /// words and must be handled separately by the CFG builder's pre-scan.
    /// </summary>
    public static bool IsCompanionBearer(OpCode op)
        => OpCodeMetadata.IsDefined((byte)op)
           && OpCodeMetadata.GetCompanionWords(op) == CompanionWordKind.OneIC;

    /// <summary>
    /// Returns true if <paramref name="op"/> carries an sBx relative jump offset.
    /// </summary>
    public static bool IsJumpWithSBx(OpCode op)
    {
        if (!OpCodeMetadata.IsDefined((byte)op))
            return false;
        return OpCodeMetadata.GetFormat(op) == OpCodeFormat.AsBx
               && OpCodeMetadata.IsBranching(op);
    }

    /// <summary>
    /// Returns true if <paramref name="op"/> terminates a basic block, meaning the
    /// instruction immediately following it (if any) is a block leader.
    /// </summary>
    public static bool IsBlockTerminator(OpCode op)
        => OpCodeMetadata.IsDefined((byte)op) && OpCodeMetadata.IsTerminator(op);
}
