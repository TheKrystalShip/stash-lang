using System;

namespace Stash.Bytecode;

/// <summary>
/// Decorates each <see cref="OpCode"/> enum member with the metadata its consumers
/// (Disassembler, OpCodeInfo, CfgOpcodeInfo, OpcodeOperands, BytecodeVerifier) need.
/// Co-locating the metadata with the enum makes the enum the single source of truth
/// and forces every new opcode to declare its full metadata at the call site
/// (the <c>required</c> properties are enforced by the C# compiler).
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class OpCodeAttribute : Attribute
{
    /// <summary>Disassembly mnemonic, e.g. "load.k", "get.field.ic".</summary>
    public required string Mnemonic { get; init; }

    /// <summary>Instruction encoding format.</summary>
    public required OpCodeFormat Format { get; init; }

    /// <summary>
    /// Operand-shape template used by the disassembler's template-driven formatter.
    /// Use <see cref="OperandTemplate.Bespoke"/> for opcodes that need a custom renderer in
    /// <see cref="BespokeOperandFormatters"/>.
    /// </summary>
    public required string Operands { get; init; }

    /// <summary>One-line description.</summary>
    public required string Summary { get; init; }

    /// <summary>Which operand positions this opcode writes to. Default: RegA.</summary>
    public OperandRole Writes { get; init; } = OperandRole.RegA;

    /// <summary>Which operand positions this opcode reads from. Default: None.</summary>
    public OperandRole Reads { get; init; } = OperandRole.None;

    /// <summary>True if this opcode can transfer control (jumps, returns, throws, loop heads).</summary>
    public bool IsBranching { get; init; }

    /// <summary>
    /// True if this opcode terminates a basic block (Jmp/Return/Throw/Rethrow + for-loops + TryBegin/TryEnd).
    /// Mirrors the semantics of <c>CfgOpcodeInfo.IsBlockTerminator</c>.
    /// </summary>
    public bool IsTerminator { get; init; }

    /// <summary>How many companion words follow this opcode.</summary>
    public CompanionWordKind CompanionWords { get; init; } = CompanionWordKind.None;
}

/// <summary>
/// Operand positions an opcode may read from or write to. Used by the optimizer to
/// classify register reads/writes without per-opcode switches in <c>OpcodeOperands</c>.
/// </summary>
[Flags]
public enum OperandRole : byte
{
    None = 0,
    RegA = 1 << 0,
    RegB = 1 << 1,
    RegC = 1 << 2,
    ConstBx = 1 << 3,
    ConstC = 1 << 4,
    JumpSBx = 1 << 5,
    UpvalB = 1 << 6,
    GlobalBx = 1 << 7,
}

/// <summary>
/// How many companion words follow an opcode in the instruction stream.
/// Companion words are part of the bytecode but do not carry an opcode in their low byte.
/// </summary>
public enum CompanionWordKind : byte
{
    /// <summary>No companion words.</summary>
    None,
    /// <summary>Exactly one companion word storing an inline-cache slot index (GetFieldIC, CallBuiltIn).</summary>
    OneIC,
    /// <summary>One companion word per upvalue descriptor in the target function prototype (Closure).</summary>
    UpvalueDescriptors,
    /// <summary>One companion word per pipeline stage; count comes from operand B (PipeChain, StreamingPipeline).</summary>
    PipeStages,
}

/// <summary>Sentinel values used in <see cref="OpCodeAttribute.Operands"/> templates.</summary>
public static class OperandTemplate
{
    /// <summary>The opcode has no operands rendered after the mnemonic.</summary>
    public const string Empty = "";

    /// <summary>The opcode renders via a named handler in <see cref="BespokeOperandFormatters"/>.</summary>
    public const string Bespoke = "<bespoke>";
}
