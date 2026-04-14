namespace Stash.Tests.Interpreting;

public class ByteTypeTests : StashTestBase
{
    // ── Byte Primitive ──────────────────────────────────────────────────

    [Fact]
    public void Byte_TypeAnnotation_CreatesByte()
    {
        Assert.Equal("byte", Run(@"let b: byte = 42; let result = typeof(b);"));
    }

    [Fact]
    public void Byte_HexAnnotation_CreatesByte()
    {
        Assert.Equal("byte", Run(@"let b: byte = 0xFF; let result = typeof(b);"));
    }

    [Fact]
    public void Byte_IsByte_ReturnsTrue()
    {
        Assert.Equal(true, Run(@"let b: byte = 10; let result = b is byte;"));
    }

    [Fact]
    public void Byte_IsInt_ReturnsFalse()
    {
        Assert.Equal(false, Run(@"let b: byte = 10; let result = b is int;"));
    }

    [Fact]
    public void Byte_Equality_SameValue_True()
    {
        Assert.Equal(true, Run(@"let a: byte = 42; let b: byte = 42; let result = a == b;"));
    }

    [Fact]
    public void Byte_Equality_DifferentValue_False()
    {
        Assert.Equal(false, Run(@"let a: byte = 42; let b: byte = 43; let result = a == b;"));
    }

    [Fact]
    public void Byte_Equality_WithInt_AlwaysFalse()
    {
        Assert.Equal(false, Run(@"let b: byte = 42; let result = b == 42;"));
    }

    [Fact]
    public void Byte_Truthiness_ZeroIsFalsy()
    {
        Assert.Equal(false, Run(@"let b: byte = 0; let result = !!b;"));
    }

    [Fact]
    public void Byte_Truthiness_NonZeroIsTruthy()
    {
        Assert.Equal(true, Run(@"let b: byte = 1; let result = !!b;"));
    }

    [Fact]
    public void Byte_ArithmeticAdd_PromotesToInt()
    {
        Assert.Equal("int", Run(@"let b: byte = 10; let result = typeof(b + 5);"));
    }

    [Fact]
    public void Byte_ArithmeticAdd_CorrectValue()
    {
        Assert.Equal(15L, Run(@"let b: byte = 10; let result = b + 5;"));
    }

    [Fact]
    public void Byte_ArithmeticSub_PromotesToInt()
    {
        Assert.Equal(5L, Run(@"let b: byte = 10; let result = b - 5;"));
    }

    [Fact]
    public void Byte_ArithmeticMul_PromotesToInt()
    {
        Assert.Equal(20L, Run(@"let b: byte = 10; let result = b * 2;"));
    }

    [Fact]
    public void Byte_BitwiseAnd_PromotesToInt()
    {
        Assert.Equal(0L, Run(@"let b: byte = 0xF0; let result = b & 0x0F;"));
    }

    [Fact]
    public void Byte_Comparison_LessThan()
    {
        Assert.Equal(true, Run(@"let a: byte = 10; let b: byte = 20; let result = a < b;"));
    }

    [Fact]
    public void Byte_Comparison_WithInt()
    {
        Assert.Equal(true, Run(@"let b: byte = 10; let result = b < 20;"));
    }

    [Fact]
    public void Byte_OutOfRange_Throws()
    {
        RunExpectingError(@"let b: byte = 256;");
    }

    [Fact]
    public void Byte_Negative_Throws()
    {
        RunExpectingError(@"let b: byte = -1;");
    }

    // ── conv.toByte ────────────────────────────────────────────────────

    [Fact]
    public void ConvToByte_FromInt_Valid()
    {
        Assert.Equal("byte", Run(@"let result = typeof(conv.toByte(255));"));
    }

    [Fact]
    public void ConvToByte_FromInt_OutOfRange_Throws()
    {
        RunExpectingError(@"conv.toByte(256);");
    }

    [Fact]
    public void ConvToByte_FromString_Valid()
    {
        Assert.Equal("byte", Run(@"let result = typeof(conv.toByte(""200""));"));
    }

    [Fact]
    public void ConvToByte_FromHexString()
    {
        Assert.Equal("byte", Run(@"let result = typeof(conv.toByte(""FF"", 16));"));
    }

    [Fact]
    public void ConvToByte_FromFloat_Truncates()
    {
        Assert.Equal("byte", Run(@"let result = typeof(conv.toByte(42.9));"));
    }

    [Fact]
    public void ConvToInt_FromByte_Widens()
    {
        Assert.Equal(42L, Run(@"let b: byte = 42; let result = conv.toInt(b);"));
    }

    [Fact]
    public void ConvToFloat_FromByte_Widens()
    {
        Assert.Equal(42.0, Run(@"let b: byte = 42; let result = conv.toFloat(b);"));
    }

    // ── Byte Array ─────────────────────────────────────────────────────

    [Fact]
    public void ByteArray_Declaration_CreatesFromInts()
    {
        Assert.Equal("byte[]", Run(@"let data: byte[] = [0x48, 0x65, 0x6C]; let result = typeof(data);"));
    }

    [Fact]
    public void ByteArray_Typeof_ReturnsByteArray()
    {
        Assert.Equal("byte[]", Run(@"let data: byte[] = [1, 2, 3]; let result = typeof(data);"));
    }

    [Fact]
    public void ByteArray_IsArray_ReturnsTrue()
    {
        Assert.Equal(true, Run(@"let data: byte[] = [1, 2]; let result = data is array;"));
    }

    [Fact]
    public void ByteArray_IsByteArray_ReturnsTrue()
    {
        Assert.Equal(true, Run(@"let data: byte[] = [1, 2]; let result = data is byte[];"));
    }

    [Fact]
    public void ByteArray_IsIntArray_ReturnsFalse()
    {
        Assert.Equal(false, Run(@"let data: byte[] = [1, 2]; let result = data is int[];"));
    }

    [Fact]
    public void ByteArray_ArrLen_Works()
    {
        Assert.Equal(3L, Run(@"let data: byte[] = [1, 2, 3]; let result = len(data);"));
    }

    [Fact]
    public void ByteArray_Push_ValidByte()
    {
        Assert.Equal(4L, Run(@"let data: byte[] = [1, 2, 3]; arr.push(data, 4); let result = len(data);"));
    }

    [Fact]
    public void ByteArray_Push_OutOfRange_Throws()
    {
        RunExpectingError(@"let data: byte[] = [1, 2]; arr.push(data, 300);");
    }

    [Fact]
    public void ByteArray_IndexRead_ReturnsByte()
    {
        Assert.Equal("byte", Run(@"let data: byte[] = [0x48]; let result = typeof(data[0]);"));
    }

    [Fact]
    public void ByteArray_OutOfRangeElement_Throws()
    {
        RunExpectingError(@"let data: byte[] = [256];");
    }

    [Fact]
    public void ByteArray_BufIncludes_FindsByte()
    {
        Assert.Equal(true, Run(@"let data: byte[] = [0x48, 0x65]; let result = buf.includes(data, 0x48);"));
    }

    [Fact]
    public void ByteArray_ArrSlice_PreservesType()
    {
        Assert.Equal("byte[]", Run(@"let data: byte[] = [1, 2, 3, 4]; let result = typeof(arr.slice(data, 0, 2));"));
    }

    [Fact]
    public void ByteArray_ArrNew_PreAllocatesZeros()
    {
        Assert.Equal(10L, Run(@"let data = arr.new(""byte"", 10); let result = len(data);"));
    }
}
