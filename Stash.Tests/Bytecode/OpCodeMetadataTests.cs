using System;
using Stash.Bytecode;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Coverage and consistency invariants for the centralized opcode metadata.
/// These tests guard against the "missing attribute on a new opcode" failure
/// mode that previously surfaced as wrong disassembly output or mis-classified
/// CFG edges only at runtime.
/// </summary>
public class OpCodeMetadataTests
{
    [Fact]
    public void EveryOpCode_HasMetadataAttribute()
    {
        foreach (OpCode op in Enum.GetValues<OpCode>())
        {
            OpCodeAttribute attr = OpCodeMetadata.Get(op);
            Assert.False(string.IsNullOrEmpty(attr.Mnemonic), $"{op}: mnemonic is empty");
            Assert.False(string.IsNullOrEmpty(attr.Summary),  $"{op}: summary is empty");
            Assert.NotNull(attr.Operands);
        }
    }

    [Fact]
    public void OpCodeInfoGetFormat_AgreesWithMetadata()
    {
        // OpCodeInfo is kept as a backwards-compatible facade and must report the
        // exact same format as OpCodeMetadata for every enum member.
        foreach (OpCode op in Enum.GetValues<OpCode>())
        {
            OpCodeFormat fromFacade   = OpCodeInfo.GetFormat(op);
            OpCodeFormat fromMetadata = OpCodeMetadata.GetFormat(op);
            Assert.Equal(fromMetadata, fromFacade);
        }
    }

    [Fact]
    public void Mnemonics_AreUnique()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (OpCode op in Enum.GetValues<OpCode>())
        {
            string mn = OpCodeMetadata.GetMnemonic(op);
            Assert.True(seen.Add(mn), $"Duplicate mnemonic '{mn}' detected.");
        }
    }

    [Fact]
    public void IsDefined_TrueForEveryEnumValue_FalseForKnownGaps()
    {
        foreach (OpCode op in Enum.GetValues<OpCode>())
            Assert.True(OpCodeMetadata.IsDefined((byte)op), $"{op} reported undefined");

        // Byte 0xFF is well above the highest defined opcode and must read as undefined.
        Assert.False(OpCodeMetadata.IsDefined(0xFF));
    }

    [Fact]
    public void Get_OnUndefinedByte_Throws()
    {
        // Casting an out-of-range byte to OpCode and calling Get must fail fast.
        Assert.Throws<InvalidOperationException>(() => OpCodeMetadata.Get((OpCode)0xFF));
    }
}
