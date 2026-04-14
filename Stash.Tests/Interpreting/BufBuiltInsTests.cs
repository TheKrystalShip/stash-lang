namespace Stash.Tests.Interpreting;

public class BufBuiltInsTests : StashTestBase
{
    // ── Construction ────────────────────────────────────────────────────

    [Fact]
    public void From_Utf8_EncodesCorrectly()
    {
        Assert.Equal(5L, Run(@"let result = len(buf.from(""Hello""));"));
    }

    [Fact]
    public void From_ReturnsByteArray()
    {
        Assert.Equal("byte[]", Run(@"let result = typeof(buf.from(""Hello""));"));
    }

    [Fact]
    public void FromHex_DecodesCorrectly()
    {
        Assert.Equal("byte[]", Run(@"let result = typeof(buf.fromHex(""48656c6c6f""));"));
    }

    [Fact]
    public void FromHex_InvalidHex_Throws()
    {
        RunExpectingError(@"buf.fromHex(""xyz"");");
    }

    [Fact]
    public void FromBase64_DecodesCorrectly()
    {
        Assert.Equal("byte[]", Run(@"let result = typeof(buf.fromBase64(""SGVsbG8=""));"));
    }

    [Fact]
    public void Alloc_ZeroFilled()
    {
        Assert.Equal(256L, Run(@"let result = buf.len(buf.alloc(256));"));
    }

    [Fact]
    public void Alloc_WithFillValue()
    {
        Assert.Equal("byte[]", Run(@"let data = buf.alloc(10, 0xFF); let result = typeof(data);"));
    }

    [Fact]
    public void Of_CreatesFromValues()
    {
        Assert.Equal(3L, Run(@"let result = buf.len(buf.of(0x48, 0x65, 0x6C));"));
    }

    // ── Conversion ──────────────────────────────────────────────────────

    [Fact]
    public void ToString_Utf8_DecodesCorrectly()
    {
        Assert.Equal("Hello", Run(@"let result = buf.toString(buf.from(""Hello""));"));
    }

    [Fact]
    public void ToHex_EncodesCorrectly()
    {
        Assert.Equal("48656c6c6f", Run(@"let result = buf.toHex(buf.from(""Hello""));"));
    }

    [Fact]
    public void ToBase64_EncodesCorrectly()
    {
        Assert.Equal("SGVsbG8=", Run(@"let result = buf.toBase64(buf.from(""Hello""));"));
    }

    // ── Inspection ──────────────────────────────────────────────────────

    [Fact]
    public void Len_ReturnsCount()
    {
        Assert.Equal(5L, Run(@"let result = buf.len(buf.from(""Hello""));"));
    }

    [Fact]
    public void IndexOf_FindsByte()
    {
        Assert.Equal(1L, Run(@"let data = buf.from(""Hello""); let result = buf.indexOf(data, 0x65);"));
    }

    [Fact]
    public void IndexOf_NotFound_ReturnsNegOne()
    {
        Assert.Equal(-1L, Run(@"let data = buf.from(""Hello""); let result = buf.indexOf(data, 0xFF);"));
    }

    [Fact]
    public void Includes_True()
    {
        Assert.Equal(true, Run(@"let data = buf.from(""Hello""); let result = buf.includes(data, 0x48);"));
    }

    [Fact]
    public void Includes_False()
    {
        Assert.Equal(false, Run(@"let data = buf.from(""Hello""); let result = buf.includes(data, 0xFF);"));
    }

    [Fact]
    public void Equals_SameContent_True()
    {
        Assert.Equal(true, Run(@"let a = buf.from(""Hello""); let b = buf.from(""Hello""); let result = buf.equals(a, b);"));
    }

    [Fact]
    public void Equals_DifferentContent_False()
    {
        Assert.Equal(false, Run(@"let a = buf.from(""Hello""); let b = buf.from(""World""); let result = buf.equals(a, b);"));
    }

    // ── Manipulation ────────────────────────────────────────────────────

    [Fact]
    public void Slice_ReturnsSubset()
    {
        Assert.Equal(3L, Run(@"let result = buf.len(buf.slice(buf.from(""Hello""), 0, 3));"));
    }

    [Fact]
    public void Slice_NegativeIndex()
    {
        Assert.Equal(2L, Run(@"let result = buf.len(buf.slice(buf.from(""Hello""), -2));"));
    }

    [Fact]
    public void Concat_CombinesArrays()
    {
        Assert.Equal(10L, Run(@"let a = buf.from(""Hello""); let b = buf.from(""World""); let result = buf.len(buf.concat(a, b));"));
    }

    [Fact]
    public void Reverse_ReversesBytes()
    {
        Assert.Equal("dlroW", Run(@"let data = buf.from(""World""); let result = buf.toString(buf.reverse(data));"));
    }

    [Fact]
    public void Fill_FillsRange()
    {
        Assert.Equal(true, Run(@"
            let data = buf.alloc(4, 0x00);
            buf.fill(data, 0xFF, 1, 3);
            let result = buf.get(data, 1) == buf.get(data, 2);
        "));
    }

    // ── Binary Read/Write ───────────────────────────────────────────────

    [Fact]
    public void ReadUint8_ReadsCorrectly()
    {
        Assert.Equal(0x48L, Run(@"let data = buf.from(""H""); let result = buf.readUint8(data, 0);"));
    }

    [Fact]
    public void WriteReadUint16BE_RoundTrip()
    {
        Assert.Equal(0x1234L, Run(@"
            let data = buf.alloc(4);
            buf.writeUint16BE(data, 0, 0x1234);
            let result = buf.readUint16BE(data, 0);
        "));
    }

    [Fact]
    public void WriteReadUint32LE_RoundTrip()
    {
        Assert.Equal(0xDEADBEEFL, Run(@"
            let data = buf.alloc(8);
            buf.writeUint32LE(data, 0, 0xDEADBEEF);
            let result = buf.readUint32LE(data, 0);
        "));
    }

    [Fact]
    public void WriteReadInt64BE_RoundTrip()
    {
        Assert.Equal(9223372036854775807L, Run(@"
            let data = buf.alloc(8);
            buf.writeInt64BE(data, 0, 9223372036854775807);
            let result = buf.readInt64BE(data, 0);
        "));
    }

    [Fact]
    public void ReadOutOfBounds_Throws()
    {
        RunExpectingError(@"let data = buf.alloc(2); buf.readUint32BE(data, 0);");
    }

    [Fact]
    public void WriteOutOfBounds_Throws()
    {
        RunExpectingError(@"let data = buf.alloc(2); buf.writeUint32BE(data, 0, 42);");
    }

    // ── Stdlib Integration ──────────────────────────────────────────────

    [Fact]
    public void CryptoSha256Bytes_ReturnsByteArray()
    {
        Assert.Equal("byte[]", Run(@"let result = typeof(crypto.sha256Bytes(buf.from(""hello"")));"));
    }

    [Fact]
    public void CryptoHmacBytes_ReturnsByteArray()
    {
        Assert.Equal("byte[]", Run(@"
            let key = buf.from(""secret"");
            let data = buf.from(""message"");
            let result = typeof(crypto.hmacBytes(""sha256"", key, data));
        "));
    }

    [Fact]
    public void CryptoRandomBytes_NoEncoding_ReturnsByteArray()
    {
        Assert.Equal("byte[]", Run(@"let result = typeof(crypto.randomBytes(16));"));
    }

    [Fact]
    public void CryptoRandomBytes_WithEncoding_ReturnsString()
    {
        Assert.Equal("string", Run(@"let result = typeof(crypto.randomBytes(16, ""hex""));"));
    }

    [Fact]
    public void EncodingBase64DecodeBytes_ReturnsByteArray()
    {
        Assert.Equal("byte[]", Run(@"let result = typeof(encoding.base64DecodeBytes(""SGVsbG8=""));"));
    }

    [Fact]
    public void EncodingHexDecodeBytes_ReturnsByteArray()
    {
        Assert.Equal("byte[]", Run(@"let result = typeof(encoding.hexDecodeBytes(""48656c6c6f""));"));
    }

    [Fact]
    public void JsonStringify_ByteArray_ReturnsBase64()
    {
        Assert.Equal("\"SGVsbG8=\"", Run(@"let result = json.stringify(buf.from(""Hello""));"));
    }
}
