namespace Stash.Stdlib.BuiltIns;

using System;
using System.Buffers.Binary;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the <c>buf</c> namespace built-in functions for binary data operations.
/// </summary>
[StashNamespace]
public static partial class BufBuiltIns
{
    // --- Construction ---

    /// <summary>Encodes a string to a byte array using the specified encoding (default: utf-8).</summary>
    /// <param name="s">The string to encode</param>
    /// <param name="encoding">The encoding to use (optional: utf-8, ascii, latin1, utf-16, utf-32)</param>
    /// <exception cref="StashErrorTypes.ValueError">if the encoding name is not recognised</exception>
    /// <returns>A byte array containing the encoded bytes</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue From(string s, string encoding = "utf-8")
    {
        System.Text.Encoding enc = encoding switch
        {
            "utf-8" or "utf8" => System.Text.Encoding.UTF8,
            "ascii" => System.Text.Encoding.ASCII,
            "latin1" => System.Text.Encoding.Latin1,
            "utf-16" or "utf16" => System.Text.Encoding.Unicode,
            "utf-32" or "utf32" => System.Text.Encoding.UTF32,
            _ => throw new ValueError($"buf.from: unsupported encoding '{encoding}'. Supported: utf-8, ascii, latin1, utf-16, utf-32.")
        };
        return StashValue.FromObj(new StashByteArray(enc.GetBytes(s)));
    }

    /// <summary>Decodes a hexadecimal string to a byte array.</summary>
    /// <param name="hex">The hex string to decode</param>
    /// <exception cref="StashErrorTypes.ParseError">if the hex string contains non-hex characters</exception>
    /// <returns>A byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue FromHex(string hex)
    {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        try
        {
            return StashValue.FromObj(new StashByteArray(Convert.FromHexString(hex)));
        }
        catch (FormatException)
        {
            throw new ParseError($"buf.fromHex: invalid hex string '{hex}'.");
        }
    }

    /// <summary>Decodes a base64 string to a byte array.</summary>
    /// <param name="b64">The base64 string to decode</param>
    /// <exception cref="StashErrorTypes.ParseError">if the string is not valid base64</exception>
    /// <returns>A byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue FromBase64(string b64)
    {
        try
        {
            return StashValue.FromObj(new StashByteArray(Convert.FromBase64String(b64)));
        }
        catch (FormatException)
        {
            throw new ParseError($"buf.fromBase64: invalid base64 string.");
        }
    }

    /// <summary>Creates a byte array of the given size, filled with zeros or an optional fill value.</summary>
    /// <param name="size">The number of bytes</param>
    /// <param name="fill">Optional byte value to fill with (default: 0)</param>
    /// <exception cref="StashErrorTypes.ValueError">if size is negative</exception>
    /// <returns>A new byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue Alloc(long size, byte fill = 0)
    {
        if (size < 0) throw new ValueError($"buf.alloc: size must be non-negative, got {size}.");
        byte[] data = new byte[(int)size];
        if (fill != 0)
            Array.Fill(data, fill);
        return StashValue.FromObj(new StashByteArray(data));
    }

    /// <summary>Creates a byte array from individual byte values.</summary>
    /// <param name="values">The byte values (0-255)</param>
    /// <exception cref="StashErrorTypes.ValueError">if any value is outside the byte range [0, 255]</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument is not a byte or integer</exception>
    /// <returns>A new byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue Of(params StashValue[] values)
    {
        byte[] data = new byte[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            StashValue v = values[i];
            if (v.IsByte)
                data[i] = v.AsByte;
            else if (v.IsInt)
            {
                long n = v.AsInt;
                if (n < 0 || n > 255)
                    throw new ValueError($"buf.of: argument {i} has value {n} which is out of byte range [0, 255].");
                data[i] = (byte)n;
            }
            else
                throw new TypeError($"buf.of: argument {i} must be a byte or int (0-255).");
        }
        return StashValue.FromObj(new StashByteArray(data));
    }

    // --- Conversion to String ---

    /// <summary>Decodes a byte array to a string using the specified encoding (default: utf-8).</summary>
    /// <param name="data">The byte array to decode</param>
    /// <param name="encoding">The encoding to use (optional)</param>
    /// <exception cref="StashErrorTypes.ValueError">if the encoding name is not recognised</exception>
    /// <returns>The decoded string</returns>
    [StashFn(Name = "toString")]
    private static string BufToString(byte[] data, string encoding = "utf-8")
    {
        System.Text.Encoding enc = encoding switch
        {
            "utf-8" or "utf8" => System.Text.Encoding.UTF8,
            "ascii" => System.Text.Encoding.ASCII,
            "latin1" => System.Text.Encoding.Latin1,
            "utf-16" or "utf16" => System.Text.Encoding.Unicode,
            "utf-32" or "utf32" => System.Text.Encoding.UTF32,
            _ => throw new ValueError($"buf.toString: unsupported encoding '{encoding}'.")
        };
        return enc.GetString(data);
    }

    /// <summary>Encodes a byte array as a lowercase hexadecimal string.</summary>
    /// <param name="data">The byte array</param>
    /// <returns>The hex string</returns>
    [StashFn]
    private static string ToHex(byte[] data)
    {
        return Convert.ToHexString(data).ToLowerInvariant();
    }

    /// <summary>Encodes a byte array as a base64 string.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="urlSafe">Use URL-safe variant (optional)</param>
    /// <returns>The base64 string</returns>
    [StashFn]
    private static string ToBase64(byte[] data, bool urlSafe = false)
    {
        string encoded = Convert.ToBase64String(data);
        if (urlSafe)
            encoded = encoded.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return encoded;
    }

    // --- Inspection ---

    /// <summary>Returns the number of bytes in a byte array.</summary>
    /// <param name="data">The byte array</param>
    /// <returns>The number of bytes</returns>
    [StashFn]
    private static long Len(byte[] data)
    {
        return (long)data.Length;
    }

    /// <summary>Reads a byte at the given index. Supports negative indexing.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="index">The index to read</param>
    /// <exception cref="StashErrorTypes.IndexError">if the index is out of bounds</exception>
    /// <returns>The byte value</returns>
    [StashFn]
    private static StashValue Get(byte[] data, long index)
    {
        int idx = (int)index;
        if (idx < 0) idx += data.Length;
        if (idx < 0 || idx >= data.Length)
            throw new RuntimeError($"Index {idx} out of bounds for byte[] of length {data.Length}.");
        return StashValue.FromByte(data[idx]);
    }

    /// <summary>Finds the first occurrence of a byte or byte subsequence.</summary>
    /// <param name="data">The byte array to search</param>
    /// <param name="search">The byte or byte[] to find</param>
    /// <param name="from">Starting index (optional)</param>
    /// <exception cref="StashErrorTypes.ValueError">if the search byte value is outside [0, 255]</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the search argument is not a byte, integer, or byte array</exception>
    /// <returns>The index, or -1 if not found</returns>
    [StashFn(Raw = true)]
    // Raw = true: search arg is polymorphic (byte, int, or StashByteArray); cannot express in typed form
    private static StashValue IndexOf(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.indexOf");
        int from = args.Length >= 3 ? (int)SvArgs.Long(args, 2, "buf.indexOf") : 0;
        if (from < 0) from = Math.Max(0, ba.Count + from);
        ReadOnlySpan<byte> haystack = ba.AsSpan();
        StashValue searchVal = args[1];
        if (searchVal.IsByte || searchVal.IsInt)
        {
            long rawVal = searchVal.IsByte ? searchVal.AsByte : searchVal.AsInt;
            if (rawVal < 0 || rawVal > 255) throw new ValueError("buf.indexOf: search byte must be in range [0, 255].");
            byte needle = (byte)rawVal;
            int idx = haystack.Slice(from).IndexOf(needle);
            return StashValue.FromInt(idx >= 0 ? idx + from : -1);
        }
        if (searchVal.IsObj && searchVal.AsObj is StashByteArray searchBa)
        {
            int idx = haystack.Slice(from).IndexOf(searchBa.AsSpan());
            return StashValue.FromInt(idx >= 0 ? idx + from : -1);
        }
        throw new TypeError("Second argument to 'buf.indexOf' must be a byte, int (0-255), or byte[].");
    }

    /// <summary>Checks if a byte array contains a byte or subsequence.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="search">The byte or byte[] to find</param>
    /// <exception cref="StashErrorTypes.ValueError">if the search byte value is outside [0, 255]</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the search argument is not a byte, integer, or byte array</exception>
    /// <returns>true if found</returns>
    [StashFn(Raw = true)]
    // Raw = true: search arg is polymorphic (byte, int, or StashByteArray); cannot express in typed form
    private static StashValue Includes(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.includes");
        ReadOnlySpan<byte> haystack = ba.AsSpan();
        StashValue searchVal = args[1];
        if (searchVal.IsByte || searchVal.IsInt)
        {
            long rawVal = searchVal.IsByte ? searchVal.AsByte : searchVal.AsInt;
            if (rawVal < 0 || rawVal > 255) throw new ValueError("buf.includes: search byte must be in range [0, 255].");
            return StashValue.FromBool(haystack.Contains((byte)rawVal));
        }
        if (searchVal.IsObj && searchVal.AsObj is StashByteArray searchBa)
        {
            return StashValue.FromBool(haystack.IndexOf(searchBa.AsSpan()) >= 0);
        }
        throw new TypeError("Second argument to 'buf.includes' must be a byte, int (0-255), or byte[].");
    }

    /// <summary>Compares two byte arrays for equality using constant-time comparison (safe for crypto).</summary>
    /// <param name="a">First byte array</param>
    /// <param name="b">Second byte array</param>
    /// <returns>true if contents are identical</returns>
    [StashFn(Name = "equals")]
    private static bool BufEquals(byte[] a, byte[] b)
    {
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    // --- Manipulation ---

    /// <summary>Returns a copy of a range of bytes. Supports negative indices.</summary>
    /// <param name="data">The source byte array</param>
    /// <param name="start">Start index (optional, default 0)</param>
    /// <param name="end">End index exclusive (optional, default length)</param>
    /// <returns>A new byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue Slice(byte[] data, long start = 0, long end = long.MinValue)
    {
        int len = data.Length;
        int s = (int)start;
        int e = end == long.MinValue ? len : (int)end;
        if (s < 0) s = Math.Max(0, len + s);
        if (e < 0) e = Math.Max(0, len + e);
        s = Math.Min(s, len);
        e = Math.Min(e, len);
        if (s >= e) return StashValue.FromObj(new StashByteArray(Array.Empty<byte>()));
        return StashValue.FromObj(new StashByteArray(data.AsSpan(s, e - s).ToArray()));
    }

    /// <summary>Concatenates multiple byte arrays into one.</summary>
    /// <param name="arrays">The byte arrays to concatenate</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument is not a byte array</exception>
    /// <returns>A new byte array containing all bytes</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue Concat(params StashValue[] arrays)
    {
        int totalLen = 0;
        for (int i = 0; i < arrays.Length; i++)
        {
            if (arrays[i].IsObj && arrays[i].AsObj is StashByteArray ba)
                totalLen += ba.Count;
            else
                throw new TypeError($"Argument {i + 1} to 'buf.concat' must be a byte[].");
        }
        byte[] result = new byte[totalLen];
        int offset = 0;
        for (int i = 0; i < arrays.Length; i++)
        {
            StashByteArray ba = (StashByteArray)arrays[i].AsObj!;
            ba.AsSpan().CopyTo(result.AsSpan(offset));
            offset += ba.Count;
        }
        return StashValue.FromObj(new StashByteArray(result));
    }

    /// <summary>Copies bytes from source to destination. Returns bytes copied.</summary>
    /// <param name="src">Source byte array</param>
    /// <param name="dest">Destination byte array</param>
    /// <param name="destOffset">Offset in destination (optional, default 0)</param>
    /// <param name="srcStart">Start index in source (optional, default 0)</param>
    /// <param name="srcEnd">End index in source (optional, default length)</param>
    /// <exception cref="StashErrorTypes.IndexError">if the source range is out of bounds</exception>
    /// <exception cref="StashErrorTypes.ValueError">if any offset argument is negative</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The number of bytes copied</returns>
    [StashFn(Raw = true)]
    // Raw = true: mutates the destination StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue Copy(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray src = SvArgs.ByteArray(args, 0, "buf.copy");
        StashByteArray dest = SvArgs.ByteArray(args, 1, "buf.copy");
        int destOffset = args.Length >= 3 ? (int)SvArgs.Long(args, 2, "buf.copy") : 0;
        int srcStart = args.Length >= 4 ? (int)SvArgs.Long(args, 3, "buf.copy") : 0;
        int srcEnd = args.Length >= 5 ? (int)SvArgs.Long(args, 4, "buf.copy") : src.Count;
        if (srcStart < 0 || srcEnd < 0 || destOffset < 0)
            throw new ValueError("buf.copy: offsets must be non-negative.");
        if (srcStart > src.Count || srcEnd > src.Count)
            throw new IndexError("buf.copy: source range out of bounds.");
        int count = Math.Min(srcEnd - srcStart, dest.Count - destOffset);
        if (count <= 0) return StashValue.FromInt(0);
        ReadOnlySpan<byte> srcSpan = src.AsSpan().Slice(srcStart, count);
        byte[] destArr = dest.GetBackingArray(out int _);
        srcSpan.CopyTo(destArr.AsSpan(destOffset));
        return StashValue.FromInt(count);
    }

    /// <summary>Fills a range of a byte array with a value. Mutates in-place, returns the array.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="value">The byte value to fill with</param>
    /// <param name="start">Start index (optional)</param>
    /// <param name="end">End index (optional)</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The same byte array</returns>
    [StashFn(Raw = true, ReturnType = "buffer")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray() and returns the live object; typed byte[] would be a detached copy
    private static StashValue Fill(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.fill");
        byte fillVal = SvArgs.Byte(args, 1, "buf.fill");
        int len = ba.Count;
        int start = args.Length >= 3 ? (int)SvArgs.Long(args, 2, "buf.fill") : 0;
        int end = args.Length >= 4 ? (int)SvArgs.Long(args, 3, "buf.fill") : len;
        if (start < 0) start = Math.Max(0, len + start);
        if (end < 0) end = Math.Max(0, len + end);
        start = Math.Min(start, len);
        end = Math.Min(end, len);
        if (start >= end) return StashValue.FromObj(ba);
        byte[] data = ba.GetBackingArray(out int _);
        Array.Fill(data, fillVal, start, end - start);
        return StashValue.FromObj(ba);
    }

    /// <summary>Returns a reversed copy of a byte array.</summary>
    /// <param name="data">The byte array</param>
    /// <returns>A new reversed byte array</returns>
    [StashFn(ReturnType = "buffer")]
    private static StashValue Reverse(byte[] data)
    {
        byte[] copy = (byte[])data.Clone();
        Array.Reverse(copy);
        return StashValue.FromObj(new StashByteArray(copy));
    }

    // --- Binary Read Functions ---

    /// <summary>Reads an unsigned 8-bit integer at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn]
    private static long ReadUint8(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 1, "buf.readUint8");
        return data[off];
    }

    /// <summary>Reads an unsigned 16-bit integer (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn(Name = "readUint16BE")]
    private static long ReadUint16BE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 2, "buf.readUint16BE");
        return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(off));
    }

    /// <summary>Reads an unsigned 16-bit integer (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn(Name = "readUint16LE")]
    private static long ReadUint16LE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 2, "buf.readUint16LE");
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(off));
    }

    /// <summary>Reads an unsigned 32-bit integer (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn(Name = "readUint32BE")]
    private static long ReadUint32BE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 4, "buf.readUint32BE");
        return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(off));
    }

    /// <summary>Reads an unsigned 32-bit integer (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn(Name = "readUint32LE")]
    private static long ReadUint32LE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 4, "buf.readUint32LE");
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(off));
    }

    /// <summary>Reads a signed 8-bit integer at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn]
    private static long ReadInt8(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 1, "buf.readInt8");
        return (sbyte)data[off];
    }

    /// <summary>Reads a signed 16-bit integer (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn(Name = "readInt16BE")]
    private static long ReadInt16BE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 2, "buf.readInt16BE");
        return BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(off));
    }

    /// <summary>Reads a signed 16-bit integer (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn(Name = "readInt16LE")]
    private static long ReadInt16LE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 2, "buf.readInt16LE");
        return BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(off));
    }

    /// <summary>Reads a signed 32-bit integer (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn(Name = "readInt32BE")]
    private static long ReadInt32BE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 4, "buf.readInt32BE");
        return BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(off));
    }

    /// <summary>Reads a signed 32-bit integer (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn(Name = "readInt32LE")]
    private static long ReadInt32LE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 4, "buf.readInt32LE");
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(off));
    }

    /// <summary>Reads a signed 64-bit integer (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn(Name = "readInt64BE")]
    private static long ReadInt64BE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 8, "buf.readInt64BE");
        return BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(off));
    }

    /// <summary>Reads a signed 64-bit integer (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as int</returns>
    [StashFn(Name = "readInt64LE")]
    private static long ReadInt64LE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 8, "buf.readInt64LE");
        return BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(off));
    }

    /// <summary>Reads a 32-bit IEEE 754 float (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as float</returns>
    [StashFn(Name = "readFloatBE")]
    private static double ReadFloatBE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 4, "buf.readFloatBE");
        return BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(off));
    }

    /// <summary>Reads a 32-bit IEEE 754 float (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as float</returns>
    [StashFn(Name = "readFloatLE")]
    private static double ReadFloatLE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 4, "buf.readFloatLE");
        return BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(off));
    }

    /// <summary>Reads a 64-bit IEEE 754 double (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as float</returns>
    [StashFn(Name = "readDoubleBE")]
    private static double ReadDoubleBE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 8, "buf.readDoubleBE");
        return BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(off));
    }

    /// <summary>Reads a 64-bit IEEE 754 double (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <returns>The value as float</returns>
    [StashFn(Name = "readDoubleLE")]
    private static double ReadDoubleLE(byte[] data, long offset)
    {
        int off = (int)offset;
        CheckReadBounds(data, off, 8, "buf.readDoubleLE");
        return BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(off));
    }

    // --- Binary Write Functions ---

    /// <summary>Writes an unsigned 8-bit integer at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write (0-255)</param>
    /// <exception cref="StashErrorTypes.ValueError">if the value is out of the valid range [0, 255]</exception>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true)]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteUint8(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeUint8");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeUint8");
        long val = SvArgs.Long(args, 2, "buf.writeUint8");
        if (val < 0 || val > 255) throw new ValueError($"buf.writeUint8: value {val} out of range [0, 255].");
        CheckWriteBounds(ba, offset, 1, "buf.writeUint8");
        ba.GetBackingArray(out int _)[offset] = (byte)val;
        return StashValue.Null;
    }

    /// <summary>Writes an unsigned 16-bit integer (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.ValueError">if the value is out of the valid range [0, 65535]</exception>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeUint16BE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteUint16BE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeUint16BE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeUint16BE");
        long val = SvArgs.Long(args, 2, "buf.writeUint16BE");
        if (val < 0 || val > 65535) throw new ValueError($"buf.writeUint16BE: value {val} out of range [0, 65535].");
        CheckWriteBounds(ba, offset, 2, "buf.writeUint16BE");
        BinaryPrimitives.WriteUInt16BigEndian(ba.GetBackingArray(out int _).AsSpan(offset), (ushort)val);
        return StashValue.Null;
    }

    /// <summary>Writes an unsigned 16-bit integer (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.ValueError">if the value is out of the valid range [0, 65535]</exception>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeUint16LE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteUint16LE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeUint16LE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeUint16LE");
        long val = SvArgs.Long(args, 2, "buf.writeUint16LE");
        if (val < 0 || val > 65535) throw new ValueError($"buf.writeUint16LE: value {val} out of range [0, 65535].");
        CheckWriteBounds(ba, offset, 2, "buf.writeUint16LE");
        BinaryPrimitives.WriteUInt16LittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), (ushort)val);
        return StashValue.Null;
    }

    /// <summary>Writes an unsigned 32-bit integer (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.ValueError">if the value is out of the valid range [0, 4294967295]</exception>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeUint32BE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteUint32BE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeUint32BE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeUint32BE");
        long val = SvArgs.Long(args, 2, "buf.writeUint32BE");
        if (val < 0 || val > 4294967295L) throw new ValueError($"buf.writeUint32BE: value {val} out of range [0, 4294967295].");
        CheckWriteBounds(ba, offset, 4, "buf.writeUint32BE");
        BinaryPrimitives.WriteUInt32BigEndian(ba.GetBackingArray(out int _).AsSpan(offset), (uint)val);
        return StashValue.Null;
    }

    /// <summary>Writes an unsigned 32-bit integer (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.ValueError">if the value is out of the valid range [0, 4294967295]</exception>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeUint32LE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteUint32LE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeUint32LE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeUint32LE");
        long val = SvArgs.Long(args, 2, "buf.writeUint32LE");
        if (val < 0 || val > 4294967295L) throw new ValueError($"buf.writeUint32LE: value {val} out of range [0, 4294967295].");
        CheckWriteBounds(ba, offset, 4, "buf.writeUint32LE");
        BinaryPrimitives.WriteUInt32LittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), (uint)val);
        return StashValue.Null;
    }

    /// <summary>Writes a signed 8-bit integer at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write (-128 to 127)</param>
    /// <exception cref="StashErrorTypes.ValueError">if the value is out of the valid range [-128, 127]</exception>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true)]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteInt8(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt8");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeInt8");
        long val = SvArgs.Long(args, 2, "buf.writeInt8");
        if (val < -128 || val > 127) throw new ValueError($"buf.writeInt8: value {val} out of range [-128, 127].");
        CheckWriteBounds(ba, offset, 1, "buf.writeInt8");
        ba.GetBackingArray(out int _)[offset] = (byte)(sbyte)val;
        return StashValue.Null;
    }

    /// <summary>Writes a signed 16-bit integer (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.ValueError">if the value is out of the valid range [-32768, 32767]</exception>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeInt16BE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteInt16BE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt16BE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeInt16BE");
        long val = SvArgs.Long(args, 2, "buf.writeInt16BE");
        if (val < -32768 || val > 32767) throw new ValueError($"buf.writeInt16BE: value {val} out of range [-32768, 32767].");
        CheckWriteBounds(ba, offset, 2, "buf.writeInt16BE");
        BinaryPrimitives.WriteInt16BigEndian(ba.GetBackingArray(out int _).AsSpan(offset), (short)val);
        return StashValue.Null;
    }

    /// <summary>Writes a signed 16-bit integer (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.ValueError">if the value is out of the valid range [-32768, 32767]</exception>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeInt16LE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteInt16LE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt16LE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeInt16LE");
        long val = SvArgs.Long(args, 2, "buf.writeInt16LE");
        if (val < -32768 || val > 32767) throw new ValueError($"buf.writeInt16LE: value {val} out of range [-32768, 32767].");
        CheckWriteBounds(ba, offset, 2, "buf.writeInt16LE");
        BinaryPrimitives.WriteInt16LittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), (short)val);
        return StashValue.Null;
    }

    /// <summary>Writes a signed 32-bit integer (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.ValueError">if the value is out of the valid range [-2147483648, 2147483647]</exception>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeInt32BE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteInt32BE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt32BE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeInt32BE");
        long val = SvArgs.Long(args, 2, "buf.writeInt32BE");
        if (val < -2147483648L || val > 2147483647L) throw new ValueError($"buf.writeInt32BE: value {val} out of range [-2147483648, 2147483647].");
        CheckWriteBounds(ba, offset, 4, "buf.writeInt32BE");
        BinaryPrimitives.WriteInt32BigEndian(ba.GetBackingArray(out int _).AsSpan(offset), (int)val);
        return StashValue.Null;
    }

    /// <summary>Writes a signed 32-bit integer (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.ValueError">if the value is out of the valid range [-2147483648, 2147483647]</exception>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeInt32LE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteInt32LE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt32LE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeInt32LE");
        long val = SvArgs.Long(args, 2, "buf.writeInt32LE");
        if (val < -2147483648L || val > 2147483647L) throw new ValueError($"buf.writeInt32LE: value {val} out of range [-2147483648, 2147483647].");
        CheckWriteBounds(ba, offset, 4, "buf.writeInt32LE");
        BinaryPrimitives.WriteInt32LittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), (int)val);
        return StashValue.Null;
    }

    /// <summary>Writes a signed 64-bit integer (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeInt64BE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteInt64BE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt64BE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeInt64BE");
        long val = SvArgs.Long(args, 2, "buf.writeInt64BE");
        CheckWriteBounds(ba, offset, 8, "buf.writeInt64BE");
        BinaryPrimitives.WriteInt64BigEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
        return StashValue.Null;
    }

    /// <summary>Writes a signed 64-bit integer (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeInt64LE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteInt64LE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt64LE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeInt64LE");
        long val = SvArgs.Long(args, 2, "buf.writeInt64LE");
        CheckWriteBounds(ba, offset, 8, "buf.writeInt64LE");
        BinaryPrimitives.WriteInt64LittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
        return StashValue.Null;
    }

    /// <summary>Writes a 32-bit IEEE 754 float (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeFloatBE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteFloatBE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeFloatBE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeFloatBE");
        float val = (float)SvArgs.Numeric(args, 2, "buf.writeFloatBE");
        CheckWriteBounds(ba, offset, 4, "buf.writeFloatBE");
        BinaryPrimitives.WriteSingleBigEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
        return StashValue.Null;
    }

    /// <summary>Writes a 32-bit IEEE 754 float (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeFloatLE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteFloatLE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeFloatLE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeFloatLE");
        float val = (float)SvArgs.Numeric(args, 2, "buf.writeFloatLE");
        CheckWriteBounds(ba, offset, 4, "buf.writeFloatLE");
        BinaryPrimitives.WriteSingleLittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
        return StashValue.Null;
    }

    /// <summary>Writes a 64-bit IEEE 754 double (big-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeDoubleBE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteDoubleBE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeDoubleBE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeDoubleBE");
        double val = SvArgs.Numeric(args, 2, "buf.writeDoubleBE");
        CheckWriteBounds(ba, offset, 8, "buf.writeDoubleBE");
        BinaryPrimitives.WriteDoubleBigEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
        return StashValue.Null;
    }

    /// <summary>Writes a 64-bit IEEE 754 double (little-endian) at the given offset.</summary>
    /// <param name="data">The byte array</param>
    /// <param name="offset">The byte offset</param>
    /// <param name="value">The value to write</param>
    /// <exception cref="StashErrorTypes.IndexError">if the offset is out of range for the buffer length</exception>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    [StashFn(Raw = true, Name = "writeDoubleLE")]
    // Raw = true: mutates the StashByteArray in place via GetBackingArray(); typed byte[] would be a detached copy
    private static StashValue WriteDoubleLE(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeDoubleLE");
        int offset = (int)SvArgs.Long(args, 1, "buf.writeDoubleLE");
        double val = SvArgs.Numeric(args, 2, "buf.writeDoubleLE");
        CheckWriteBounds(ba, offset, 8, "buf.writeDoubleLE");
        BinaryPrimitives.WriteDoubleLittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
        return StashValue.Null;
    }

    private static void CheckReadBounds(byte[] data, int offset, int size, string func)
    {
        if (offset < 0 || offset + size > data.Length)
            throw new RuntimeError($"{func}: offset {offset} with {size}-byte read exceeds buffer length {data.Length}.");
    }

    private static void CheckReadBounds(StashByteArray ba, int offset, int size, string func)
    {
        if (offset < 0 || offset + size > ba.Count)
            throw new RuntimeError($"{func}: offset {offset} with {size}-byte read exceeds buffer length {ba.Count}.");
    }

    private static void CheckWriteBounds(StashByteArray ba, int offset, int size, string func)
    {
        if (offset < 0 || offset + size > ba.Count)
            throw new RuntimeError($"{func}: offset {offset} with {size}-byte write exceeds buffer length {ba.Count}.");
    }
}
