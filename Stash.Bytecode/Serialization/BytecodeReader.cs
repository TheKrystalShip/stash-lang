using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

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
    private const int    MaxStringLength       = 16 * 1024 * 1024; // 16 MB

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
        if (sourceLength > MaxStringLength)
            throw new InvalidDataException(
                $"Embedded source length {sourceLength} exceeds the {MaxStringLength / (1024 * 1024)} MB safety limit.");
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

        // Arity / minArity / maxRegs / globalSlotCount (all u16 LE)
        int arity           = reader.ReadUInt16();
        int minArity        = reader.ReadUInt16();
        int maxRegs         = reader.ReadUInt16();
        int globalSlotCount = reader.ReadUInt16();

        // Chunk flags (bit 0: IsAsync, bit 1: HasRestParam, bit 2: MayHaveCapturedLocals)
        byte chunkFlags            = reader.ReadByte();
        bool isAsync               = (chunkFlags & 0x01) != 0;
        bool hasRestParam          = (chunkFlags & 0x02) != 0;
        bool mayHaveCapturedLocals = (chunkFlags & 0x04) != 0;

        // Code (u32 instruction count + uint[] 4-bytes-per-instruction LE)
        uint codeLength = reader.ReadUInt32();
        if (codeLength > MaxCodeLength)
            throw new InvalidDataException(
                $"Code length {codeLength} exceeds the 16 MB safety limit.");
        uint[] code = new uint[codeLength];
        for (int i = 0; i < (int)codeLength; i++)
            code[i] = reader.ReadUInt32();

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

        // IC slots (u16 count + u16[] ConstantIndex)
        ushort icSlotCount = reader.ReadUInt16();
        ICSlot[]? icSlots = icSlotCount > 0 ? new ICSlot[icSlotCount] : null;
        if (icSlots is not null)
        {
            for (int i = 0; i < icSlotCount; i++)
                icSlots[i].ConstantIndex = reader.ReadUInt16();
        }

        // ConstGlobalInits (u16 count + (u16 slot, u16 constIndex) pairs)
        ushort constGlobalInitCount = reader.ReadUInt16();
        (ushort Slot, ushort ConstIndex)[]? constGlobalInits = constGlobalInitCount > 0
            ? new (ushort, ushort)[constGlobalInitCount]
            : null;
        if (constGlobalInits is not null)
        {
            for (int i = 0; i < constGlobalInitCount; i++)
            {
                ushort slot = reader.ReadUInt16();
                ushort constIndex = reader.ReadUInt16();
                constGlobalInits[i] = (slot, constIndex);
            }
        }

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
            maxRegs,
            upvalues,
            name,
            isAsync,
            hasRestParam,
            mayHaveCapturedLocals,
            localNames,
            localIsConst,
            upvalueNames,
            globalNameTable,
            globalSlotCount,
            icSlots: icSlots,
            constGlobalInits: constGlobalInits);

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
            6 => StashValue.FromObj(ReadCommandMetadata(reader)),
            7 => StashValue.FromObj(ReadStructMetadata(reader)),
            8 => StashValue.FromObj(ReadEnumMetadata(reader)),
            9 => StashValue.FromObj(ReadInterfaceMetadata(reader)),
            10 => StashValue.FromObj(ReadExtendMetadata(reader)),
            11 => StashValue.FromObj(ReadImportMetadata(reader)),
            12 => StashValue.FromObj(ReadImportAsMetadata(reader)),
            13 => StashValue.FromObj(ReadDestructureMetadata(reader)),
            14 => StashValue.FromObj(ReadRetryMetadata(reader)),
            15 => StashValue.FromObj(ReadStructInitMetadata(reader)),
            16 => StashValue.FromByte(reader.ReadByte()),
            _ => throw new InvalidDataException($"Unknown constant tag {tag} in .stashc constant pool.")
        };
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
        if (length > MaxStringLength)
            throw new InvalidDataException(
                $"String constant length {length} exceeds the {MaxStringLength / (1024 * 1024)} MB safety limit.");
        byte[] bytes = reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string[] ReadStringArray(BinaryReader reader)
    {
        ushort count = reader.ReadUInt16();
        string[] values = new string[count];
        for (int i = 0; i < count; i++)
            values[i] = ReadString16(reader);
        return values;
    }

    private static string?[] ReadNullableStringArray(BinaryReader reader)
    {
        ushort count = reader.ReadUInt16();
        string?[] values = new string?[count];
        for (int i = 0; i < count; i++)
            values[i] = ReadNullableString16(reader);
        return values;
    }

    private static CommandMetadata ReadCommandMetadata(BinaryReader reader)
    {
        int partCount = reader.ReadUInt16();
        bool isPassthrough = reader.ReadByte() != 0;
        bool isStrict = reader.ReadByte() != 0;
        return new CommandMetadata(partCount, isPassthrough, isStrict);
    }

    private static StructMetadata ReadStructMetadata(BinaryReader reader)
    {
        string name = ReadString16(reader);
        string[] fields = ReadStringArray(reader);
        string[] methodNames = ReadStringArray(reader);
        string[] interfaceNames = ReadStringArray(reader);
        return new StructMetadata(name, fields, methodNames, interfaceNames);
    }

    private static EnumMetadata ReadEnumMetadata(BinaryReader reader)
    {
        string name = ReadString16(reader);
        string[] members = ReadStringArray(reader);
        return new EnumMetadata(name, members);
    }

    private static InterfaceMetadata ReadInterfaceMetadata(BinaryReader reader)
    {
        string name = ReadString16(reader);
        ushort fieldCount = reader.ReadUInt16();
        InterfaceField[] fields = new InterfaceField[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            string fieldName = ReadString16(reader);
            string? typeHint = ReadNullableString16(reader);
            fields[i] = new InterfaceField(fieldName, typeHint);
        }
        ushort methodCount = reader.ReadUInt16();
        InterfaceMethod[] methods = new InterfaceMethod[methodCount];
        for (int i = 0; i < methodCount; i++)
        {
            string methodName = ReadString16(reader);
            int arity = reader.ReadUInt16();
            string[] paramNames = ReadStringArray(reader);
            string?[] paramTypes = ReadNullableStringArray(reader);
            string? returnType = ReadNullableString16(reader);
            methods[i] = new InterfaceMethod(methodName, arity, new List<string>(paramNames), new List<string?>(paramTypes), returnType);
        }
        return new InterfaceMetadata(name, fields, methods);
    }

    private static ExtendMetadata ReadExtendMetadata(BinaryReader reader)
    {
        string typeName = ReadString16(reader);
        string[] methodNames = ReadStringArray(reader);
        bool isBuiltIn = reader.ReadByte() != 0;
        return new ExtendMetadata(typeName, methodNames, isBuiltIn);
    }

    private static ImportMetadata ReadImportMetadata(BinaryReader reader)
    {
        string[] names = ReadStringArray(reader);
        return new ImportMetadata(names);
    }

    private static ImportAsMetadata ReadImportAsMetadata(BinaryReader reader)
    {
        string aliasName = ReadString16(reader);
        return new ImportAsMetadata(aliasName);
    }

    private static DestructureMetadata ReadDestructureMetadata(BinaryReader reader)
    {
        string kind = ReadString16(reader);
        string[] names = ReadStringArray(reader);
        string? restName = ReadNullableString16(reader);
        bool isConst = reader.ReadByte() != 0;
        return new DestructureMetadata(kind, names, restName, isConst);
    }

    private static RetryMetadata ReadRetryMetadata(BinaryReader reader)
    {
        int optionCount = reader.ReadUInt16();
        bool hasUntilClause = reader.ReadByte() != 0;
        bool hasOnRetryClause = reader.ReadByte() != 0;
        bool onRetryIsReference = reader.ReadByte() != 0;
        return new RetryMetadata(optionCount, hasUntilClause, hasOnRetryClause, onRetryIsReference);
    }

    private static StructInitMetadata ReadStructInitMetadata(BinaryReader reader)
    {
        string typeName = ReadString16(reader);
        bool hasTypeReg = reader.ReadByte() != 0;
        string[] fieldNames = ReadStringArray(reader);
        return new StructInitMetadata(typeName, hasTypeReg, fieldNames);
    }
}
