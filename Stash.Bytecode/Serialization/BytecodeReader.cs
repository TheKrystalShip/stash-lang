using System;
using System.IO;
using System.Text;
using Stash.Common;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Deserializes a .stashc binary file into a <see cref="Chunk"/>.
/// </summary>
public static class BytecodeReader
{
    private const ushort FormatVersion         = 1;
    private const byte   FlagHasDebugInfo      = 0x01;
    private const byte   FlagHasEmbeddedSource = 0x04;
    private const ushort NullLength            = 0xFFFF;
    private const int    MaxCodeLength         = 16 * 1024 * 1024; // 16 MB

    /// <summary>Read and validate a .stashc stream, returning the top-level chunk.</summary>
    public static Chunk Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        byte flags = ReadAndValidateHeader(reader);
        bool hasDebugInfo = (flags & FlagHasDebugInfo) != 0;
        return ReadChunk(reader, hasDebugInfo);
    }

    /// <summary>Convenience: read from file path.</summary>
    public static Chunk Read(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Read(stream);
    }

    /// <summary>Check if a file starts with the STBC magic bytes.</summary>
    public static bool IsBytecodeFile(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            byte[] buf = new byte[4];
            int bytesRead = stream.Read(buf, 0, 4);
            return bytesRead == 4 &&
                   buf[0] == 0x53 && buf[1] == 0x54 && buf[2] == 0x42 && buf[3] == 0x43;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if a stream starts with STBC magic bytes without consuming them
    /// (requires a seekable stream).
    /// </summary>
    public static bool IsBytecodeStream(Stream stream)
    {
        long savedPosition = stream.Position;
        try
        {
            byte[] buf = new byte[4];
            int bytesRead = stream.Read(buf, 0, 4);
            return bytesRead == 4 &&
                   buf[0] == 0x53 && buf[1] == 0x54 && buf[2] == 0x42 && buf[3] == 0x43;
        }
        finally
        {
            stream.Position = savedPosition;
        }
    }

    /// <summary>Read the embedded source text from a .stashc file, or null if not embedded.</summary>
    public static string? ReadEmbeddedSource(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        byte flags = ReadAndValidateHeader(reader);
        bool hasDebugInfo      = (flags & FlagHasDebugInfo) != 0;
        bool hasEmbeddedSource = (flags & FlagHasEmbeddedSource) != 0;

        if (!hasEmbeddedSource)
            return null;

        // Skip past the serialized chunk to reach the embedded source section.
        ReadChunk(reader, hasDebugInfo);

        uint sourceLength = reader.ReadUInt32();
        byte[] sourceBytes = reader.ReadBytes((int)sourceLength);
        return Encoding.UTF8.GetString(sourceBytes);
    }

    // -------------------------------------------------------------------------
    // Private — header
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads and validates the 32-byte .stashc file header.
    /// Returns the flags byte.
    /// </summary>
    private static byte ReadAndValidateHeader(BinaryReader reader)
    {
        // Magic: "STBC" (0x53 0x54 0x42 0x43)
        byte b0 = reader.ReadByte();
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        byte b3 = reader.ReadByte();
        if (b0 != 0x53 || b1 != 0x54 || b2 != 0x42 || b3 != 0x43)
            throw new InvalidDataException("Not a valid .stashc file: magic bytes mismatch.");

        // Format version (u16 LE)
        ushort version = reader.ReadUInt16();
        if (version != FormatVersion)
            throw new InvalidDataException(
                $"Unsupported .stashc format version {version} (expected {FormatVersion}).");

        // Flags byte
        byte flags = reader.ReadByte();

        // Reserved byte
        _ = reader.ReadByte();

        // Compiler version hash — informational, not validated
        _ = reader.ReadUInt32();

        // OpCode table version hash — must match the current build
        uint storedHash   = reader.ReadUInt32();
        uint expectedHash = BytecodeWriter.ComputeOpCodeTableHash();
        if (storedHash != expectedHash)
            throw new InvalidDataException(
                "Bytecode was compiled with an incompatible version of Stash (OpCode table mismatch).");

        // Source SHA-256 prefix (16 bytes) — stored for cache invalidation, not validated here
        _ = reader.ReadBytes(16);

        return flags;
    }

    // -------------------------------------------------------------------------
    // Private — chunk deserialization
    // -------------------------------------------------------------------------

    private static Chunk ReadChunk(BinaryReader reader, bool hasDebugInfo)
    {
        // Name (u16 length + UTF-8, 0xFFFF = null)
        string? name = ReadNullableString16(reader);

        // Arity / minArity / localCount / globalSlotCount (all u16 LE)
        int arity           = reader.ReadUInt16();
        int minArity        = reader.ReadUInt16();
        int localCount      = reader.ReadUInt16();
        int globalSlotCount = reader.ReadUInt16();

        // Chunk flags (bit 0: IsAsync, bit 1: HasRestParam, bit 2: MayHaveCapturedLocals)
        byte chunkFlags            = reader.ReadByte();
        bool isAsync               = (chunkFlags & 0x01) != 0;
        bool hasRestParam          = (chunkFlags & 0x02) != 0;
        bool mayHaveCapturedLocals = (chunkFlags & 0x04) != 0;

        // Code (u32 length + raw bytes)
        uint codeLength = reader.ReadUInt32();
        if (codeLength > MaxCodeLength)
            throw new InvalidDataException(
                $"Code blob length {codeLength} exceeds the 16 MB safety limit.");
        byte[] code = reader.ReadBytes((int)codeLength);

        // Constants (u16 count + tagged values)
        int constantCount = reader.ReadUInt16();
        StashValue[] constants = new StashValue[constantCount];
        for (int i = 0; i < constantCount; i++)
            constants[i] = ReadConstant(reader, hasDebugInfo);

        // Upvalues (u8 count + (u8 index, u8 isLocal) pairs)
        int upvalueCount = reader.ReadByte();
        var upvalues = new UpvalueDescriptor[upvalueCount];
        for (int i = 0; i < upvalueCount; i++)
        {
            byte index   = reader.ReadByte();
            bool isLocal = reader.ReadByte() != 0;
            upvalues[i]  = new UpvalueDescriptor(index, isLocal);
        }

        // GlobalNameTable (u16 count + u16-length-prefixed strings)
        int globalNameCount = reader.ReadUInt16();
        string[]? globalNameTable = globalNameCount > 0 ? new string[globalNameCount] : null;
        for (int i = 0; i < globalNameCount; i++)
            globalNameTable![i] = ReadString16(reader);

        // Debug info (only present when the debug flag is set in the file header)
        SourceMapEntry[] sourceMapEntries = Array.Empty<SourceMapEntry>();
        string[]? localNames   = null;
        bool[]?   localIsConst = null;
        string[]? upvalueNames = null;

        if (hasDebugInfo)
        {
            // SourceMap raw entries — fileIdx resolved after reading the SourceFiles table
            uint sourceMapCount = reader.ReadUInt32();
            uint[]   rawOffsets = new uint[sourceMapCount];
            ushort[] rawFileIdx = new ushort[sourceMapCount];
            ushort[] rawSLine   = new ushort[sourceMapCount];
            ushort[] rawSCol    = new ushort[sourceMapCount];
            ushort[] rawELine   = new ushort[sourceMapCount];
            ushort[] rawECol    = new ushort[sourceMapCount];

            for (int i = 0; i < (int)sourceMapCount; i++)
            {
                rawOffsets[i] = reader.ReadUInt32();
                rawFileIdx[i] = reader.ReadUInt16();
                rawSLine[i]   = reader.ReadUInt16();
                rawSCol[i]    = reader.ReadUInt16();
                rawELine[i]   = reader.ReadUInt16();
                rawECol[i]    = reader.ReadUInt16();
            }

            // SourceFiles table (u16 count + u16-length-prefixed strings)
            int fileCount     = reader.ReadUInt16();
            string[] srcFiles = new string[fileCount];
            for (int i = 0; i < fileCount; i++)
                srcFiles[i] = ReadString16(reader);

            // Resolve fileIdx → SourceMapEntry
            sourceMapEntries = new SourceMapEntry[(int)sourceMapCount];
            for (int i = 0; i < (int)sourceMapCount; i++)
            {
                string file = rawFileIdx[i] < srcFiles.Length ? srcFiles[rawFileIdx[i]] : string.Empty;
                sourceMapEntries[i] = new SourceMapEntry(
                    (int)rawOffsets[i],
                    new SourceSpan(file, rawSLine[i], rawSCol[i], rawELine[i], rawECol[i]));
            }

            // LocalNames (u16 count + nullable u16-length-prefixed strings)
            int localNameCount = reader.ReadUInt16();
            localNames = new string[localNameCount];
            for (int i = 0; i < localNameCount; i++)
                localNames[i] = ReadNullableString16(reader) ?? string.Empty;

            // LocalIsConst (u16 count + 1 byte per local)
            int localConstCount = reader.ReadUInt16();
            localIsConst = new bool[localConstCount];
            for (int i = 0; i < localConstCount; i++)
                localIsConst[i] = reader.ReadByte() != 0;

            // UpvalueNames (u8 count + nullable u16-length-prefixed strings)
            int upvalueNameCount = reader.ReadByte();
            upvalueNames = new string[upvalueNameCount];
            for (int i = 0; i < upvalueNameCount; i++)
                upvalueNames[i] = ReadNullableString16(reader) ?? string.Empty;
        }

        var chunk = new Chunk(
            code,
            constants,
            new SourceMap(sourceMapEntries),
            arity,
            minArity,
            localCount,
            upvalues,
            name,
            isAsync,
            hasRestParam,
            mayHaveCapturedLocals,
            localNames,
            localIsConst,
            upvalueNames,
            globalNameTable,
            globalSlotCount);

        // Reconstruct IC slots by counting GetFieldIC opcodes in the code stream.
        // GetFieldIC (opcode 98) is 5 bytes: 1 opcode byte + u16 name_idx + u16 ic_slot_idx.
        int icSlotCount = CountGetFieldICOpcodes(code, constants);
        if (icSlotCount > 0)
            chunk.ICSlots = new ICSlot[icSlotCount];

        return chunk;
    }

    private static StashValue ReadConstant(BinaryReader reader, bool hasDebugInfo)
    {
        byte tag = reader.ReadByte();
        return tag switch
        {
            0 => StashValue.Null,
            1 => StashValue.FromBool(reader.ReadByte() != 0),
            2 => StashValue.FromInt(reader.ReadInt64()),
            3 => StashValue.FromFloat(BitConverter.Int64BitsToDouble(reader.ReadInt64())),
            4 => StashValue.FromObj(ReadString32(reader)),
            5 => StashValue.FromObj(ReadChunk(reader, hasDebugInfo)),
            _ => throw new InvalidDataException($"Unknown constant tag {tag} in .stashc constant pool.")
        };
    }

    /// <summary>
    /// Walks the bytecode array and counts <see cref="OpCode.GetFieldIC"/> instructions,
    /// correctly skipping the variable-length inline upvalue descriptors that follow
    /// <see cref="OpCode.Closure"/> instructions.
    /// </summary>
    private static int CountGetFieldICOpcodes(byte[] code, StashValue[] constants)
    {
        const byte GetFieldICByte = 98; // OpCode.GetFieldIC: 1 opcode + u16 + u16 = 5 bytes
        const byte ClosureByte    = 41; // OpCode.Closure: 1 opcode + u16 + N*2 upvalue bytes

        int count = 0;
        int i = 0;
        while (i < code.Length)
        {
            byte op = code[i];
            i++; // consume opcode byte

            if (op == GetFieldICByte)
            {
                count++;
                i += 4; // skip u16 name_idx + u16 ic_slot_idx
            }
            else if (op == ClosureByte)
            {
                // Standard 2-byte constant pool index, then N*2 bytes of inline upvalue descriptors.
                ushort constIdx = (ushort)((code[i] << 8) | code[i + 1]);
                i += 2;
                int uvCount = constIdx < constants.Length && constants[constIdx].AsObj is Chunk fn
                    ? fn.Upvalues.Length
                    : 0;
                i += uvCount * 2; // 1 byte isLocal + 1 byte index per descriptor
            }
            else
            {
                i += OpCodeInfo.OperandSize((OpCode)op);
            }
        }
        return count;
    }

    // -------------------------------------------------------------------------
    // Private — string reading helpers
    // -------------------------------------------------------------------------

    /// <summary>Reads a u16-length-prefixed UTF-8 string. Throws on the 0xFFFF null sentinel.</summary>
    private static string ReadString16(BinaryReader reader)
    {
        ushort length = reader.ReadUInt16();
        if (length == NullLength)
            throw new InvalidDataException("Unexpected null sentinel in a non-nullable string field.");
        byte[] bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Reads a u16-length-prefixed UTF-8 string, returning null when the length is 0xFFFF.
    /// </summary>
    private static string? ReadNullableString16(BinaryReader reader)
    {
        ushort length = reader.ReadUInt16();
        if (length == NullLength)
            return null;
        byte[] bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Reads a u32-length-prefixed UTF-8 string (used for constant pool string values).</summary>
    private static string ReadString32(BinaryReader reader)
    {
        uint length = reader.ReadUInt32();
        byte[] bytes = reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }
}
