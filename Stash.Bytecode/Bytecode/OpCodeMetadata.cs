using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Stash.Bytecode;

/// <summary>
/// Centralized opcode metadata lookup. The <see cref="OpCode"/> enum is the single
/// source of truth — every member must be decorated with an <see cref="OpCodeAttribute"/>.
/// The static constructor verifies full coverage at process start; any missing
/// attribute fails fast with a descriptive error before any consumer runs.
/// <para>
/// Reflection cost is paid once per process at type initialization. Lookups are
/// O(1) array indexing — no allocations on the disassembly hot path.
/// </para>
/// </summary>
public static class OpCodeMetadata
{
    /// <summary>One slot per byte value of <see cref="OpCode"/>. Null for undefined values.</summary>
    private static readonly OpCodeAttribute?[] _table = BuildTable();

    static OpCodeMetadata()
    {
        // Fail fast at process startup if any enum member lacks an attribute.
        foreach (OpCode op in Enum.GetValues<OpCode>())
        {
            if (_table[(byte)op] is null)
            {
                throw new InvalidOperationException(
                    $"OpCode.{op} ({(byte)op}) is missing [OpCode(...)] attribute. " +
                    "Every enum member must declare metadata in OpCode.cs.");
            }
        }
    }

    /// <summary>Returns the full metadata for an opcode. Throws if the enum value lacks an attribute.</summary>
    public static OpCodeAttribute Get(OpCode op)
    {
        OpCodeAttribute? entry = _table[(byte)op];
        if (entry is null)
        {
            throw new InvalidOperationException(
                $"OpCode.{op} ({(byte)op}) is missing [OpCode(...)] attribute.");
        }
        return entry;
    }

    /// <summary>Returns true if the byte value corresponds to a defined opcode with metadata.</summary>
    public static bool IsDefined(byte value) => value < _table.Length && _table[value] is not null;

    /// <summary>Returns the instruction encoding format.</summary>
    public static OpCodeFormat GetFormat(OpCode op) => Get(op).Format;

    /// <summary>Returns the disassembly mnemonic.</summary>
    public static string GetMnemonic(OpCode op) => Get(op).Mnemonic;

    /// <summary>Returns the operand-shape template string.</summary>
    public static string GetOperandTemplate(OpCode op) => Get(op).Operands;

    /// <summary>Returns the companion-word kind (None/OneIC/UpvalueDescriptors/PipeStages).</summary>
    public static CompanionWordKind GetCompanionWords(OpCode op) => Get(op).CompanionWords;

    /// <summary>Returns true if this opcode can transfer control.</summary>
    public static bool IsBranching(OpCode op) => Get(op).IsBranching;

    /// <summary>Returns true if this opcode terminates a basic block.</summary>
    public static bool IsTerminator(OpCode op) => Get(op).IsTerminator;

    /// <summary>Returns the operand role flags that this opcode writes to.</summary>
    public static OperandRole GetWrites(OpCode op) => Get(op).Writes;

    /// <summary>Returns the operand role flags that this opcode reads from.</summary>
    public static OperandRole GetReads(OpCode op) => Get(op).Reads;

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Enum fields and OpCodeAttribute are preserved by the DynamicDependency below.")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OpCode))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OpCodeAttribute))]
    private static OpCodeAttribute?[] BuildTable()
    {
        var table = new OpCodeAttribute?[256];
        Type enumType = typeof(OpCode);
        foreach (OpCode op in Enum.GetValuesAsUnderlyingType<OpCode>())
        {
            string name = op.ToString();
            FieldInfo? field = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field is null)
                continue;
            OpCodeAttribute? attr = field.GetCustomAttribute<OpCodeAttribute>(inherit: false);
            if (attr is not null)
                table[(byte)op] = attr;
        }
        return table;
    }
}
