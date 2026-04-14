namespace Stash.Stdlib.BuiltIns;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>buf</c> namespace built-in functions for binary data operations.
/// </summary>
public static class BufBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("buf");

        // --- Construction ---

        ns.Function("from", [Param("s", "string"), Param("encoding", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "buf.from");
            string encoding = args.Length >= 2 ? SvArgs.String(args, 1, "buf.from") : "utf-8";
            System.Text.Encoding enc = encoding switch
            {
                "utf-8" or "utf8" => System.Text.Encoding.UTF8,
                "ascii" => System.Text.Encoding.ASCII,
                "latin1" => System.Text.Encoding.Latin1,
                "utf-16" or "utf16" => System.Text.Encoding.Unicode,
                "utf-32" or "utf32" => System.Text.Encoding.UTF32,
                _ => throw new RuntimeError($"buf.from: unsupported encoding '{encoding}'. Supported: utf-8, ascii, latin1, utf-16, utf-32.")
            };
            return StashValue.FromObj(new StashByteArray(enc.GetBytes(s)));
        },
            returnType: "byte[]",
            isVariadic: true,
            documentation: "Encodes a string to a byte array using the specified encoding (default: utf-8).\n@param s The string to encode\n@param encoding The encoding to use (optional: utf-8, ascii, latin1, utf-16, utf-32)\n@return A byte array containing the encoded bytes");

        ns.Function("fromHex", [Param("hex", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            string hex = SvArgs.String(args, 0, "buf.fromHex");
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex[2..];
            try
            {
                return StashValue.FromObj(new StashByteArray(Convert.FromHexString(hex)));
            }
            catch (FormatException)
            {
                throw new RuntimeError($"buf.fromHex: invalid hex string '{hex}'.");
            }
        },
            returnType: "byte[]",
            documentation: "Decodes a hexadecimal string to a byte array.\n@param hex The hex string to decode\n@return A byte array");

        ns.Function("fromBase64", [Param("b64", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            string b64 = SvArgs.String(args, 0, "buf.fromBase64");
            try
            {
                return StashValue.FromObj(new StashByteArray(Convert.FromBase64String(b64)));
            }
            catch (FormatException)
            {
                throw new RuntimeError($"buf.fromBase64: invalid base64 string.");
            }
        },
            returnType: "byte[]",
            documentation: "Decodes a base64 string to a byte array.\n@param b64 The base64 string to decode\n@return A byte array");

        ns.Function("alloc", [Param("size", "int"), Param("fill", "byte")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            int size = (int)SvArgs.Long(args, 0, "buf.alloc");
            if (size < 0) throw new RuntimeError($"buf.alloc: size must be non-negative, got {size}.");
            byte[] data = new byte[size];
            if (args.Length >= 2)
            {
                byte fill = SvArgs.Byte(args, 1, "buf.alloc");
                Array.Fill(data, fill);
            }
            return StashValue.FromObj(new StashByteArray(data));
        },
            returnType: "byte[]",
            isVariadic: true,
            documentation: "Creates a byte array of the given size, filled with zeros or an optional fill value.\n@param size The number of bytes\n@param fill Optional byte value to fill with (default: 0)\n@return A new byte array");

        ns.Function("of", [Param("values")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            byte[] data = new byte[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                StashValue v = args[i];
                if (v.IsByte)
                    data[i] = v.AsByte;
                else if (v.IsInt)
                {
                    long n = v.AsInt;
                    if (n < 0 || n > 255)
                        throw new RuntimeError($"buf.of: argument {i} has value {n} which is out of byte range [0, 255].");
                    data[i] = (byte)n;
                }
                else
                    throw new RuntimeError($"buf.of: argument {i} must be a byte or int (0-255).");
            }
            return StashValue.FromObj(new StashByteArray(data));
        },
            returnType: "byte[]",
            isVariadic: true,
            documentation: "Creates a byte array from individual byte values.\n@param values The byte values (0-255)\n@return A new byte array");

        // --- Conversion to String ---

        ns.Function("toString", [Param("data", "byte[]"), Param("encoding", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.toString");
            string encoding = args.Length >= 2 ? SvArgs.String(args, 1, "buf.toString") : "utf-8";
            System.Text.Encoding enc = encoding switch
            {
                "utf-8" or "utf8" => System.Text.Encoding.UTF8,
                "ascii" => System.Text.Encoding.ASCII,
                "latin1" => System.Text.Encoding.Latin1,
                "utf-16" or "utf16" => System.Text.Encoding.Unicode,
                "utf-32" or "utf32" => System.Text.Encoding.UTF32,
                _ => throw new RuntimeError($"buf.toString: unsupported encoding '{encoding}'.")
            };
            return StashValue.FromObj(enc.GetString(ba.AsSpan()));
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Decodes a byte array to a string using the specified encoding (default: utf-8).\n@param data The byte array to decode\n@param encoding The encoding to use (optional)\n@return The decoded string");

        ns.Function("toHex", [Param("data", "byte[]")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.toHex");
            return StashValue.FromObj(Convert.ToHexString(ba.AsSpan()).ToLowerInvariant());
        },
            returnType: "string",
            documentation: "Encodes a byte array as a lowercase hexadecimal string.\n@param data The byte array\n@return The hex string");

        ns.Function("toBase64", [Param("data", "byte[]"), Param("urlSafe", "bool")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.toBase64");
            string encoded = Convert.ToBase64String(ba.AsSpan());
            if (args.Length >= 2 && SvArgs.Bool(args, 1, "buf.toBase64"))
                encoded = encoded.Replace('+', '-').Replace('/', '_').TrimEnd('=');
            return StashValue.FromObj(encoded);
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Encodes a byte array as a base64 string.\n@param data The byte array\n@param urlSafe Use URL-safe variant (optional)\n@return The base64 string");

        // --- Inspection ---

        ns.Function("len", [Param("data", "byte[]")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromInt(SvArgs.ByteArray(args, 0, "buf.len").Count);
        },
            returnType: "int",
            documentation: "Returns the number of bytes in a byte array.\n@param data The byte array\n@return The number of bytes");

        ns.Function("get", [Param("data", "byte[]"), Param("index", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.get");
            int index = (int)SvArgs.Long(args, 1, "buf.get");
            index = ba.ResolveIndex(index);
            ba.CheckBounds(index, "buf.get");
            return ba.Get(index);
        },
            returnType: "byte",
            documentation: "Reads a byte at the given index. Supports negative indexing.\n@param data The byte array\n@param index The index to read\n@return The byte value");

        ns.Function("indexOf", [Param("data", "byte[]"), Param("search"), Param("from", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.indexOf");
            int from = args.Length >= 3 ? (int)SvArgs.Long(args, 2, "buf.indexOf") : 0;
            if (from < 0) from = Math.Max(0, ba.Count + from);
            ReadOnlySpan<byte> haystack = ba.AsSpan();
            StashValue searchVal = args[1];
            if (searchVal.IsByte || searchVal.IsInt)
            {
                long rawVal = searchVal.IsByte ? searchVal.AsByte : searchVal.AsInt;
                if (rawVal < 0 || rawVal > 255) throw new RuntimeError("buf.indexOf: search byte must be in range [0, 255].");
                byte needle = (byte)rawVal;
                int idx = haystack.Slice(from).IndexOf(needle);
                return StashValue.FromInt(idx >= 0 ? idx + from : -1);
            }
            if (searchVal.IsObj && searchVal.AsObj is StashByteArray searchBa)
            {
                int idx = haystack.Slice(from).IndexOf(searchBa.AsSpan());
                return StashValue.FromInt(idx >= 0 ? idx + from : -1);
            }
            throw new RuntimeError("Second argument to 'buf.indexOf' must be a byte, int (0-255), or byte[].");
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Finds the first occurrence of a byte or byte subsequence.\n@param data The byte array to search\n@param search The byte or byte[] to find\n@param from Starting index (optional)\n@return The index, or -1 if not found");

        ns.Function("includes", [Param("data", "byte[]"), Param("search")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.includes");
            ReadOnlySpan<byte> haystack = ba.AsSpan();
            StashValue searchVal = args[1];
            if (searchVal.IsByte || searchVal.IsInt)
            {
                long rawVal = searchVal.IsByte ? searchVal.AsByte : searchVal.AsInt;
                if (rawVal < 0 || rawVal > 255) throw new RuntimeError("buf.includes: search byte must be in range [0, 255].");
                return StashValue.FromBool(haystack.Contains((byte)rawVal));
            }
            if (searchVal.IsObj && searchVal.AsObj is StashByteArray searchBa)
            {
                return StashValue.FromBool(haystack.IndexOf(searchBa.AsSpan()) >= 0);
            }
            throw new RuntimeError("Second argument to 'buf.includes' must be a byte, int (0-255), or byte[].");
        },
            returnType: "bool",
            documentation: "Checks if a byte array contains a byte or subsequence.\n@param data The byte array\n@param search The byte or byte[] to find\n@return true if found");

        ns.Function("equals", [Param("a", "byte[]"), Param("b", "byte[]")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray a = SvArgs.ByteArray(args, 0, "buf.equals");
            StashByteArray b = SvArgs.ByteArray(args, 1, "buf.equals");
            return StashValue.FromBool(System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a.AsSpan(), b.AsSpan()));
        },
            returnType: "bool",
            documentation: "Compares two byte arrays for equality using constant-time comparison (safe for crypto).\n@param a First byte array\n@param b Second byte array\n@return true if contents are identical");

        // --- Manipulation ---

        ns.Function("slice", [Param("data", "byte[]"), Param("start", "int"), Param("end", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.slice");
            int len = ba.Count;
            int start = args.Length >= 2 ? (int)SvArgs.Long(args, 1, "buf.slice") : 0;
            int end = args.Length >= 3 ? (int)SvArgs.Long(args, 2, "buf.slice") : len;
            if (start < 0) start = Math.Max(0, len + start);
            if (end < 0) end = Math.Max(0, len + end);
            start = Math.Min(start, len);
            end = Math.Min(end, len);
            if (start >= end) return StashValue.FromObj(new StashByteArray(Array.Empty<byte>()));
            return StashValue.FromObj(new StashByteArray(ba.AsSpan().Slice(start, end - start).ToArray()));
        },
            returnType: "byte[]",
            isVariadic: true,
            documentation: "Returns a copy of a range of bytes. Supports negative indices.\n@param data The source byte array\n@param start Start index (optional, default 0)\n@param end End index exclusive (optional, default length)\n@return A new byte array");

        ns.Function("concat", [Param("arrays")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            int totalLen = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].IsObj && args[i].AsObj is StashByteArray ba)
                    totalLen += ba.Count;
                else
                    throw new RuntimeError($"Argument {i + 1} to 'buf.concat' must be a byte[].");
            }
            byte[] result = new byte[totalLen];
            int offset = 0;
            for (int i = 0; i < args.Length; i++)
            {
                StashByteArray ba = (StashByteArray)args[i].AsObj!;
                ba.AsSpan().CopyTo(result.AsSpan(offset));
                offset += ba.Count;
            }
            return StashValue.FromObj(new StashByteArray(result));
        },
            returnType: "byte[]",
            isVariadic: true,
            documentation: "Concatenates multiple byte arrays into one.\n@param arrays The byte arrays to concatenate\n@return A new byte array containing all bytes");

        ns.Function("copy", [Param("src", "byte[]"), Param("dest", "byte[]"), Param("destOffset", "int"), Param("srcStart", "int"), Param("srcEnd", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray src = SvArgs.ByteArray(args, 0, "buf.copy");
            StashByteArray dest = SvArgs.ByteArray(args, 1, "buf.copy");
            int destOffset = args.Length >= 3 ? (int)SvArgs.Long(args, 2, "buf.copy") : 0;
            int srcStart = args.Length >= 4 ? (int)SvArgs.Long(args, 3, "buf.copy") : 0;
            int srcEnd = args.Length >= 5 ? (int)SvArgs.Long(args, 4, "buf.copy") : src.Count;
            if (srcStart < 0 || srcEnd < 0 || destOffset < 0)
                throw new RuntimeError("buf.copy: offsets must be non-negative.");
            if (srcStart > src.Count || srcEnd > src.Count)
                throw new RuntimeError("buf.copy: source range out of bounds.");
            int count = Math.Min(srcEnd - srcStart, dest.Count - destOffset);
            if (count <= 0) return StashValue.FromInt(0);
            ReadOnlySpan<byte> srcSpan = src.AsSpan().Slice(srcStart, count);
            byte[] destArr = dest.GetBackingArray(out int _);
            srcSpan.CopyTo(destArr.AsSpan(destOffset));
            return StashValue.FromInt(count);
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Copies bytes from source to destination. Returns bytes copied.\n@param src Source byte array\n@param dest Destination byte array\n@param destOffset Offset in destination (optional, default 0)\n@param srcStart Start index in source (optional, default 0)\n@param srcEnd End index in source (optional, default length)\n@return The number of bytes copied");

        ns.Function("fill", [Param("data", "byte[]"), Param("value"), Param("start", "int"), Param("end", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "byte[]",
            isVariadic: true,
            documentation: "Fills a range of a byte array with a value. Mutates in-place, returns the array.\n@param data The byte array\n@param value The byte value to fill with\n@param start Start index (optional)\n@param end End index (optional)\n@return The same byte array");

        ns.Function("reverse", [Param("data", "byte[]")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.reverse");
            byte[] copy = ba.AsSpan().ToArray();
            Array.Reverse(copy);
            return StashValue.FromObj(new StashByteArray(copy));
        },
            returnType: "byte[]",
            documentation: "Returns a reversed copy of a byte array.\n@param data The byte array\n@return A new reversed byte array");

        // --- Binary Read Functions ---

        ns.Function("readUint8", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readUint8");
            int offset = (int)SvArgs.Long(args, 1, "buf.readUint8");
            CheckReadBounds(ba, offset, 1, "buf.readUint8");
            return StashValue.FromInt(ba.AsSpan()[offset]);
        },
            returnType: "int",
            documentation: "Reads an unsigned 8-bit integer at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readUint16BE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readUint16BE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readUint16BE");
            CheckReadBounds(ba, offset, 2, "buf.readUint16BE");
            return StashValue.FromInt(BinaryPrimitives.ReadUInt16BigEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "int",
            documentation: "Reads an unsigned 16-bit integer (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readUint16LE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readUint16LE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readUint16LE");
            CheckReadBounds(ba, offset, 2, "buf.readUint16LE");
            return StashValue.FromInt(BinaryPrimitives.ReadUInt16LittleEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "int",
            documentation: "Reads an unsigned 16-bit integer (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readUint32BE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readUint32BE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readUint32BE");
            CheckReadBounds(ba, offset, 4, "buf.readUint32BE");
            return StashValue.FromInt(BinaryPrimitives.ReadUInt32BigEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "int",
            documentation: "Reads an unsigned 32-bit integer (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readUint32LE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readUint32LE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readUint32LE");
            CheckReadBounds(ba, offset, 4, "buf.readUint32LE");
            return StashValue.FromInt(BinaryPrimitives.ReadUInt32LittleEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "int",
            documentation: "Reads an unsigned 32-bit integer (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readInt8", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readInt8");
            int offset = (int)SvArgs.Long(args, 1, "buf.readInt8");
            CheckReadBounds(ba, offset, 1, "buf.readInt8");
            return StashValue.FromInt((sbyte)ba.AsSpan()[offset]);
        },
            returnType: "int",
            documentation: "Reads a signed 8-bit integer at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readInt16BE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readInt16BE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readInt16BE");
            CheckReadBounds(ba, offset, 2, "buf.readInt16BE");
            return StashValue.FromInt(BinaryPrimitives.ReadInt16BigEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "int",
            documentation: "Reads a signed 16-bit integer (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readInt16LE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readInt16LE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readInt16LE");
            CheckReadBounds(ba, offset, 2, "buf.readInt16LE");
            return StashValue.FromInt(BinaryPrimitives.ReadInt16LittleEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "int",
            documentation: "Reads a signed 16-bit integer (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readInt32BE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readInt32BE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readInt32BE");
            CheckReadBounds(ba, offset, 4, "buf.readInt32BE");
            return StashValue.FromInt(BinaryPrimitives.ReadInt32BigEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "int",
            documentation: "Reads a signed 32-bit integer (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readInt32LE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readInt32LE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readInt32LE");
            CheckReadBounds(ba, offset, 4, "buf.readInt32LE");
            return StashValue.FromInt(BinaryPrimitives.ReadInt32LittleEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "int",
            documentation: "Reads a signed 32-bit integer (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readInt64BE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readInt64BE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readInt64BE");
            CheckReadBounds(ba, offset, 8, "buf.readInt64BE");
            return StashValue.FromInt(BinaryPrimitives.ReadInt64BigEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "int",
            documentation: "Reads a signed 64-bit integer (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readInt64LE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readInt64LE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readInt64LE");
            CheckReadBounds(ba, offset, 8, "buf.readInt64LE");
            return StashValue.FromInt(BinaryPrimitives.ReadInt64LittleEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "int",
            documentation: "Reads a signed 64-bit integer (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as int");

        ns.Function("readFloatBE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readFloatBE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readFloatBE");
            CheckReadBounds(ba, offset, 4, "buf.readFloatBE");
            return StashValue.FromFloat(BinaryPrimitives.ReadSingleBigEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "float",
            documentation: "Reads a 32-bit IEEE 754 float (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as float");

        ns.Function("readFloatLE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readFloatLE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readFloatLE");
            CheckReadBounds(ba, offset, 4, "buf.readFloatLE");
            return StashValue.FromFloat(BinaryPrimitives.ReadSingleLittleEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "float",
            documentation: "Reads a 32-bit IEEE 754 float (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as float");

        ns.Function("readDoubleBE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readDoubleBE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readDoubleBE");
            CheckReadBounds(ba, offset, 8, "buf.readDoubleBE");
            return StashValue.FromFloat(BinaryPrimitives.ReadDoubleBigEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "float",
            documentation: "Reads a 64-bit IEEE 754 double (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as float");

        ns.Function("readDoubleLE", [Param("data", "byte[]"), Param("offset", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.readDoubleLE");
            int offset = (int)SvArgs.Long(args, 1, "buf.readDoubleLE");
            CheckReadBounds(ba, offset, 8, "buf.readDoubleLE");
            return StashValue.FromFloat(BinaryPrimitives.ReadDoubleLittleEndian(ba.AsSpan().Slice(offset)));
        },
            returnType: "float",
            documentation: "Reads a 64-bit IEEE 754 double (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@return The value as float");

        // --- Binary Write Functions ---

        ns.Function("writeUint8", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeUint8");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeUint8");
            long val = SvArgs.Long(args, 2, "buf.writeUint8");
            if (val < 0 || val > 255) throw new RuntimeError($"buf.writeUint8: value {val} out of range [0, 255].");
            CheckWriteBounds(ba, offset, 1, "buf.writeUint8");
            ba.GetBackingArray(out int _)[offset] = (byte)val;
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes an unsigned 8-bit integer at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write (0-255)");

        ns.Function("writeUint16BE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeUint16BE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeUint16BE");
            long val = SvArgs.Long(args, 2, "buf.writeUint16BE");
            if (val < 0 || val > 65535) throw new RuntimeError($"buf.writeUint16BE: value {val} out of range [0, 65535].");
            CheckWriteBounds(ba, offset, 2, "buf.writeUint16BE");
            BinaryPrimitives.WriteUInt16BigEndian(ba.GetBackingArray(out int _).AsSpan(offset), (ushort)val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes an unsigned 16-bit integer (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeUint16LE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeUint16LE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeUint16LE");
            long val = SvArgs.Long(args, 2, "buf.writeUint16LE");
            if (val < 0 || val > 65535) throw new RuntimeError($"buf.writeUint16LE: value {val} out of range [0, 65535].");
            CheckWriteBounds(ba, offset, 2, "buf.writeUint16LE");
            BinaryPrimitives.WriteUInt16LittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), (ushort)val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes an unsigned 16-bit integer (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeUint32BE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeUint32BE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeUint32BE");
            long val = SvArgs.Long(args, 2, "buf.writeUint32BE");
            if (val < 0 || val > 4294967295L) throw new RuntimeError($"buf.writeUint32BE: value {val} out of range [0, 4294967295].");
            CheckWriteBounds(ba, offset, 4, "buf.writeUint32BE");
            BinaryPrimitives.WriteUInt32BigEndian(ba.GetBackingArray(out int _).AsSpan(offset), (uint)val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes an unsigned 32-bit integer (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeUint32LE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeUint32LE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeUint32LE");
            long val = SvArgs.Long(args, 2, "buf.writeUint32LE");
            if (val < 0 || val > 4294967295L) throw new RuntimeError($"buf.writeUint32LE: value {val} out of range [0, 4294967295].");
            CheckWriteBounds(ba, offset, 4, "buf.writeUint32LE");
            BinaryPrimitives.WriteUInt32LittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), (uint)val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes an unsigned 32-bit integer (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeInt8", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt8");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeInt8");
            long val = SvArgs.Long(args, 2, "buf.writeInt8");
            if (val < -128 || val > 127) throw new RuntimeError($"buf.writeInt8: value {val} out of range [-128, 127].");
            CheckWriteBounds(ba, offset, 1, "buf.writeInt8");
            ba.GetBackingArray(out int _)[offset] = (byte)(sbyte)val;
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a signed 8-bit integer at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write (-128 to 127)");

        ns.Function("writeInt16BE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt16BE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeInt16BE");
            long val = SvArgs.Long(args, 2, "buf.writeInt16BE");
            if (val < -32768 || val > 32767) throw new RuntimeError($"buf.writeInt16BE: value {val} out of range [-32768, 32767].");
            CheckWriteBounds(ba, offset, 2, "buf.writeInt16BE");
            BinaryPrimitives.WriteInt16BigEndian(ba.GetBackingArray(out int _).AsSpan(offset), (short)val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a signed 16-bit integer (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeInt16LE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt16LE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeInt16LE");
            long val = SvArgs.Long(args, 2, "buf.writeInt16LE");
            if (val < -32768 || val > 32767) throw new RuntimeError($"buf.writeInt16LE: value {val} out of range [-32768, 32767].");
            CheckWriteBounds(ba, offset, 2, "buf.writeInt16LE");
            BinaryPrimitives.WriteInt16LittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), (short)val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a signed 16-bit integer (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeInt32BE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt32BE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeInt32BE");
            long val = SvArgs.Long(args, 2, "buf.writeInt32BE");
            if (val < -2147483648L || val > 2147483647L) throw new RuntimeError($"buf.writeInt32BE: value {val} out of range [-2147483648, 2147483647].");
            CheckWriteBounds(ba, offset, 4, "buf.writeInt32BE");
            BinaryPrimitives.WriteInt32BigEndian(ba.GetBackingArray(out int _).AsSpan(offset), (int)val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a signed 32-bit integer (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeInt32LE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt32LE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeInt32LE");
            long val = SvArgs.Long(args, 2, "buf.writeInt32LE");
            if (val < -2147483648L || val > 2147483647L) throw new RuntimeError($"buf.writeInt32LE: value {val} out of range [-2147483648, 2147483647].");
            CheckWriteBounds(ba, offset, 4, "buf.writeInt32LE");
            BinaryPrimitives.WriteInt32LittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), (int)val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a signed 32-bit integer (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeInt64BE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt64BE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeInt64BE");
            long val = SvArgs.Long(args, 2, "buf.writeInt64BE");
            CheckWriteBounds(ba, offset, 8, "buf.writeInt64BE");
            BinaryPrimitives.WriteInt64BigEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a signed 64-bit integer (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeInt64LE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "int")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeInt64LE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeInt64LE");
            long val = SvArgs.Long(args, 2, "buf.writeInt64LE");
            CheckWriteBounds(ba, offset, 8, "buf.writeInt64LE");
            BinaryPrimitives.WriteInt64LittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a signed 64-bit integer (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeFloatBE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "float")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeFloatBE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeFloatBE");
            float val = (float)SvArgs.Numeric(args, 2, "buf.writeFloatBE");
            CheckWriteBounds(ba, offset, 4, "buf.writeFloatBE");
            BinaryPrimitives.WriteSingleBigEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a 32-bit IEEE 754 float (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeFloatLE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "float")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeFloatLE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeFloatLE");
            float val = (float)SvArgs.Numeric(args, 2, "buf.writeFloatLE");
            CheckWriteBounds(ba, offset, 4, "buf.writeFloatLE");
            BinaryPrimitives.WriteSingleLittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a 32-bit IEEE 754 float (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeDoubleBE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "float")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeDoubleBE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeDoubleBE");
            double val = SvArgs.Numeric(args, 2, "buf.writeDoubleBE");
            CheckWriteBounds(ba, offset, 8, "buf.writeDoubleBE");
            BinaryPrimitives.WriteDoubleBigEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a 64-bit IEEE 754 double (big-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        ns.Function("writeDoubleLE", [Param("data", "byte[]"), Param("offset", "int"), Param("value", "float")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            StashByteArray ba = SvArgs.ByteArray(args, 0, "buf.writeDoubleLE");
            int offset = (int)SvArgs.Long(args, 1, "buf.writeDoubleLE");
            double val = SvArgs.Numeric(args, 2, "buf.writeDoubleLE");
            CheckWriteBounds(ba, offset, 8, "buf.writeDoubleLE");
            BinaryPrimitives.WriteDoubleLittleEndian(ba.GetBackingArray(out int _).AsSpan(offset), val);
            return StashValue.Null;
        },
            returnType: "null",
            documentation: "Writes a 64-bit IEEE 754 double (little-endian) at the given offset.\n@param data The byte array\n@param offset The byte offset\n@param value The value to write");

        return ns.Build();
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
