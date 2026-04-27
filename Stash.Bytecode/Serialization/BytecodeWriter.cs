using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Stdlib;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Serializes a <see cref="Chunk"/> to the binary .stashc format.
/// </summary>
public static class BytecodeWriter
{
    /// <summary>Magic bytes for .stashc files: "STBC" (0x53 0x54 0x42 0x43).</summary>
    public const uint MagicBytes = 0x53544243;

    /// <summary>Current .stashc format version.</summary>
    public const ushort FormatVersion = 1;

    /// <summary>
    /// File-level flags stored in the header byte.
    /// </summary>
    [Flags]
    public enum FileFlags : byte
    {
        None              = 0,
        HasDebugInfo      = 1 << 0,
        Optimized         = 1 << 1,
        HasEmbeddedSource = 1 << 2,
        HasStdlibManifest = 1 << 3,
    }

    /// <summary>
    /// Write a compiled chunk to a stream in .stashc binary format.
    /// </summary>
    /// <param name="stream">Output stream.</param>
    /// <param name="chunk">The top-level chunk to serialize.</param>
    /// <param name="includeDebugInfo">Include source maps, local names, upvalue names, etc.</param>
    /// <param name="optimized">Whether the chunk was compiled with optimizations.</param>
    /// <param name="sourceText">Original source text for SHA-256 hash computation and optional embedding.</param>
    /// <param name="embedSource">Embed the full source text in the file.</param>
    public static void Write(Stream stream, Chunk chunk, bool includeDebugInfo = true, bool optimized = true, string? sourceText = null, bool embedSource = false)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteHeader(writer, includeDebugInfo, optimized, embedSource, sourceText, chunk.StdlibManifest is not null);
        WriteChunk(writer, chunk, includeDebugInfo);
        if (chunk.StdlibManifest is { } manifest)
        {
            WriteStdlibManifest(writer, manifest);
        }
        if (embedSource)
        {
            WriteEmbeddedSource(writer, sourceText);
        }
    }

    /// <summary>
    /// Write a compiled chunk to a file in .stashc binary format.
    /// </summary>
    /// <param name="filePath">Destination file path.</param>
    /// <param name="chunk">The top-level chunk to serialize.</param>
    /// <param name="includeDebugInfo">Include source maps, local names, upvalue names, etc.</param>
    /// <param name="optimized">Whether the chunk was compiled with optimizations.</param>
    /// <param name="sourceText">Original source text for SHA-256 hash computation and optional embedding.</param>
    /// <param name="embedSource">Embed the full source text in the file.</param>
    public static void Write(string filePath, Chunk chunk, bool includeDebugInfo = true, bool optimized = true, string? sourceText = null, bool embedSource = false)
    {
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        Write(stream, chunk, includeDebugInfo, optimized, sourceText, embedSource);
    }

    /// <summary>
    /// Compute a u32 hash of all OpCode enum names to detect format incompatibility at load time.
    /// Uses FNV-1a over the concatenated enum member names.
    /// </summary>
    public static uint ComputeOpCodeTableHash()
    {
        const uint FnvPrime = 16777619u;
        const uint FnvOffset = 2166136261u;

        uint hash = FnvOffset;
        foreach (OpCode opCode in Enum.GetValues<OpCode>())
        {
            string name = opCode.ToString();
            foreach (char c in name)
            {
                hash ^= (byte)c;
                hash *= FnvPrime;
            }
            // Also fold in the numeric value so renames AND renumberings both invalidate
            hash ^= (uint)(byte)opCode;
            hash *= FnvPrime;
        }
        return hash;
    }

    // --- Header ---

    private static void WriteHeader(BinaryWriter writer, bool includeDebugInfo, bool optimized, bool embedSource, string? sourceText, bool hasStdlibManifest = false)
    {
        // 0x00: Magic "STBC" — write as individual bytes to guarantee byte order
        writer.Write((byte)0x53);
        writer.Write((byte)0x54);
        writer.Write((byte)0x42);
        writer.Write((byte)0x43);

        // 0x04: Format version (u16 LE)
        writer.Write(FormatVersion);

        // 0x06: Flags
        FileFlags flags = FileFlags.None;
        if (includeDebugInfo)    flags |= FileFlags.HasDebugInfo;
        if (optimized)           flags |= FileFlags.Optimized;
        if (embedSource)         flags |= FileFlags.HasEmbeddedSource;
        if (hasStdlibManifest)   flags |= FileFlags.HasStdlibManifest;
        writer.Write((byte)flags);

        // 0x07: Reserved padding
        writer.Write((byte)0);

        // 0x08: Stash compiler version hash (u32 LE) — use assemby version hash
        uint compilerHash = ComputeCompilerVersionHash();
        writer.Write(compilerHash);

        // 0x0C: OpCode table version (u32 LE)
        writer.Write(ComputeOpCodeTableHash());

        // 0x10: Source SHA-256 prefix (16 bytes)
        byte[] sha256Prefix = ComputeSourceSha256Prefix(sourceText);
        writer.Write(sha256Prefix); // exactly 16 bytes
    }

    private static uint ComputeCompilerVersionHash()
    {
        // Use a stable hash of the assembly version to detect binary incompatibility
        var version = typeof(BytecodeWriter).Assembly.GetName().Version;
        if (version is null)
        {
            return 0u;
        }
        uint hash = (uint)version.Major;
        hash = (hash << 8) | (uint)version.Minor;
        hash = (hash << 8) | (uint)version.Build;
        hash = (hash << 8) | (uint)(version.Revision & 0xFF);
        return hash;
    }

    private static byte[] ComputeSourceSha256Prefix(string? sourceText)
    {
        byte[] result = new byte[16];
        if (sourceText is null)
        {
            return result; // all zeros
        }
        byte[] bytes = Encoding.UTF8.GetBytes(sourceText);
        byte[] hash = SHA256.HashData(bytes);
        // Take first 16 bytes of the 32-byte SHA-256
        Array.Copy(hash, result, 16);
        return result;
    }

    // --- Chunk ---

    private static void WriteChunk(BinaryWriter writer, Chunk chunk, bool includeDebugInfo)
    {
        // Name: u16 length + UTF-8, or 0xFFFF for null
        WriteNullableString(writer, chunk.Name);

        // Arity, MinArity, MaxRegs, GlobalSlotCount
        writer.Write((ushort)chunk.Arity);
        writer.Write((ushort)chunk.MinArity);
        writer.Write((ushort)chunk.MaxRegs);
        writer.Write((ushort)chunk.GlobalSlotCount);

        // Flags: IsAsync, HasRestParam, MayHaveCapturedLocals
        byte chunkFlags = 0;
        if (chunk.IsAsync)               chunkFlags |= 1;
        if (chunk.HasRestParam)          chunkFlags |= 2;
        if (chunk.MayHaveCapturedLocals) chunkFlags |= 4;
        writer.Write(chunkFlags);

        // Code: u32 instruction count + uint[] (each instruction is 4 bytes LE)
        writer.Write((uint)chunk.Code.Length);
        foreach (uint word in chunk.Code)
            writer.Write(word);

        // Constants: u16 count + [tagged values]
        WriteConstants(writer, chunk.Constants, includeDebugInfo);

        // Upvalues: u8 count + [(u8 index, u8 isLocal)]
        WriteUpvalues(writer, chunk.Upvalues);

        // GlobalNameTable: u16 count + [length-prefixed strings]
        WriteGlobalNameTable(writer, chunk.GlobalNameTable);

        // IC slot count + ConstantIndex array (u16 count + u16[])
        ushort icSlotCount = (ushort)(chunk.ICSlots?.Length ?? 0);
        writer.Write(icSlotCount);
        if (chunk.ICSlots is { } icSlots)
        {
            for (int i = 0; i < icSlots.Length; i++)
                writer.Write(icSlots[i].ConstantIndex);
        }

        // ConstGlobalInits: u16 count + [(u16 slot, u16 constIndex)] pairs
        ushort constGlobalInitCount = (ushort)(chunk.ConstGlobalInits?.Length ?? 0);
        writer.Write(constGlobalInitCount);
        if (chunk.ConstGlobalInits is { } constInits)
        {
            for (int i = 0; i < constInits.Length; i++)
            {
                writer.Write(constInits[i].Slot);
                writer.Write(constInits[i].ConstIndex);
            }
        }

        // Debug info (only if flag was set in header)
        if (includeDebugInfo)
        {
            WriteDebugInfo(writer, chunk);
        }
    }

    private static void WriteConstants(BinaryWriter writer, StashValue[] constants, bool includeDebugInfo)
    {
        writer.Write((ushort)constants.Length);
        foreach (StashValue value in constants)
        {
            WriteConstant(writer, value, includeDebugInfo);
        }
    }

    private static void WriteConstant(BinaryWriter writer, StashValue value, bool includeDebugInfo)
    {
        switch (value.Tag)
        {
            case StashValueTag.Null:
                writer.Write((byte)0);
                break;

            case StashValueTag.Bool:
                writer.Write((byte)1);
                writer.Write((byte)(value.AsBool ? 1 : 0));
                break;

            case StashValueTag.Int:
                writer.Write((byte)2);
                writer.Write(value.AsInt);
                break;

            case StashValueTag.Float:
                writer.Write((byte)3);
                writer.Write(BitConverter.DoubleToInt64Bits(value.AsFloat));
                break;

            case StashValueTag.Byte:
                writer.Write((byte)16);
                writer.Write(value.AsByte);
                break;

            case StashValueTag.Obj:
                object? obj = value.AsObj;
                if (obj is string str)
                {
                    writer.Write((byte)4);
                    byte[] strBytes = Encoding.UTF8.GetBytes(str);
                    writer.Write((uint)strBytes.Length);
                    writer.Write(strBytes);
                }
                else if (obj is Chunk nestedChunk)
                {
                    writer.Write((byte)5);
                    // Nested chunks inherit the debug info flag from the top-level header.
                    WriteChunk(writer, nestedChunk, includeDebugInfo);
                }
                else if (obj is CommandMetadata cmd)
                {
                    writer.Write((byte)6);
                    writer.Write((ushort)cmd.PartCount);
                    writer.Write((byte)(cmd.IsPassthrough ? 1 : 0));
                    writer.Write((byte)(cmd.IsStrict ? 1 : 0));
                }
                else if (obj is StructMetadata structMeta)
                {
                    writer.Write((byte)7);
                    WriteLengthPrefixedString(writer, structMeta.Name);
                    WriteStringArray(writer, structMeta.Fields);
                    WriteStringArray(writer, structMeta.MethodNames);
                    WriteStringArray(writer, structMeta.InterfaceNames);
                }
                else if (obj is EnumMetadata enumMeta)
                {
                    writer.Write((byte)8);
                    WriteLengthPrefixedString(writer, enumMeta.Name);
                    WriteStringArray(writer, enumMeta.Members);
                }
                else if (obj is InterfaceMetadata ifaceMeta)
                {
                    writer.Write((byte)9);
                    WriteLengthPrefixedString(writer, ifaceMeta.Name);
                    writer.Write((ushort)ifaceMeta.Fields.Length);
                    foreach (InterfaceField field in ifaceMeta.Fields)
                    {
                        WriteLengthPrefixedString(writer, field.Name);
                        WriteNullableString(writer, field.TypeHint);
                    }
                    writer.Write((ushort)ifaceMeta.Methods.Length);
                    foreach (InterfaceMethod method in ifaceMeta.Methods)
                    {
                        WriteLengthPrefixedString(writer, method.Name);
                        writer.Write((ushort)method.Arity);
                        WriteStringArray(writer, [.. method.ParameterNames]);
                        WriteNullableStringArray(writer, method.ParameterTypes);
                        WriteNullableString(writer, method.ReturnType);
                    }
                }
                else if (obj is ExtendMetadata extendMeta)
                {
                    writer.Write((byte)10);
                    WriteLengthPrefixedString(writer, extendMeta.TypeName);
                    WriteStringArray(writer, extendMeta.MethodNames);
                    writer.Write((byte)(extendMeta.IsBuiltIn ? 1 : 0));
                }
                else if (obj is ImportMetadata importMeta)
                {
                    writer.Write((byte)11);
                    WriteStringArray(writer, importMeta.Names);
                }
                else if (obj is ImportAsMetadata importAsMeta)
                {
                    writer.Write((byte)12);
                    WriteLengthPrefixedString(writer, importAsMeta.AliasName);
                }
                else if (obj is DestructureMetadata destructureMeta)
                {
                    writer.Write((byte)13);
                    WriteLengthPrefixedString(writer, destructureMeta.Kind);
                    WriteStringArray(writer, destructureMeta.Names);
                    WriteNullableString(writer, destructureMeta.RestName);
                    writer.Write((byte)(destructureMeta.IsConst ? 1 : 0));
                }
                else if (obj is RetryMetadata retryMeta)
                {
                    writer.Write((byte)14);
                    writer.Write((ushort)retryMeta.OptionCount);
                    writer.Write((byte)(retryMeta.HasUntilClause ? 1 : 0));
                    writer.Write((byte)(retryMeta.HasOnRetryClause ? 1 : 0));
                    writer.Write((byte)(retryMeta.OnRetryIsReference ? 1 : 0));
                }
                else if (obj is StructInitMetadata structInitMeta)
                {
                    writer.Write((byte)15);
                    WriteLengthPrefixedString(writer, structInitMeta.TypeName);
                    writer.Write((byte)(structInitMeta.HasTypeReg ? 1 : 0));
                    WriteStringArray(writer, structInitMeta.FieldNames);
                }
                else if (obj is LockMetadata lockMeta)
                {
                    writer.Write((byte)17);
                    writer.Write((int)lockMeta.OptionCount);
                    writer.Write((byte)(lockMeta.HasWait ? 1 : 0));
                    writer.Write((byte)(lockMeta.HasStale ? 1 : 0));
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unexpected object type in constant pool: {obj?.GetType().Name ?? "null"}");
                }
                break;

            default:
                throw new InvalidOperationException($"Unknown StashValueTag: {value.Tag}");
        }
    }

    private static void WriteUpvalues(BinaryWriter writer, UpvalueDescriptor[] upvalues)
    {
        writer.Write((byte)upvalues.Length);
        foreach (UpvalueDescriptor upvalue in upvalues)
        {
            writer.Write(upvalue.Index);
            writer.Write((byte)(upvalue.IsLocal ? 1 : 0));
        }
    }

    private static void WriteGlobalNameTable(BinaryWriter writer, string[]? table)
    {
        if (table is null)
        {
            writer.Write((ushort)0);
            return;
        }
        writer.Write((ushort)table.Length);
        foreach (string name in table)
        {
            WriteLengthPrefixedString(writer, name);
        }
    }

    // --- Debug Info ---

    private static void WriteDebugInfo(BinaryWriter writer, Chunk chunk)
    {
        WriteSourceMap(writer, chunk.SourceMap);
        WriteLocalNames(writer, chunk.LocalNames);
        WriteLocalIsConst(writer, chunk.LocalIsConst);
        WriteUpvalueNames(writer, chunk.UpvalueNames);
    }

    private static void WriteSourceMap(BinaryWriter writer, SourceMap sourceMap)
    {
        int count = sourceMap.Count;

        // Build dedup table of unique file paths
        var fileIndex = new Dictionary<string, ushort>();
        var fileList = new List<string>();

        for (int i = 0; i < count; i++)
        {
            SourceMapEntry entry = sourceMap[i];
            string file = entry.Span.File;
            if (!fileIndex.ContainsKey(file))
            {
                fileIndex[file] = (ushort)fileList.Count;
                fileList.Add(file);
            }
        }

        // SourceMap entries: u32 count + [(u32 offset, u16 fileIdx, u16 startLine, u16 startCol, u16 endLine, u16 endCol)]
        writer.Write((uint)count);
        for (int i = 0; i < count; i++)
        {
            SourceMapEntry entry = sourceMap[i];
            writer.Write((uint)entry.BytecodeOffset);
            writer.Write(fileIndex[entry.Span.File]);
            writer.Write((ushort)entry.Span.StartLine);
            writer.Write((ushort)entry.Span.StartColumn);
            writer.Write((ushort)entry.Span.EndLine);
            writer.Write((ushort)entry.Span.EndColumn);
        }

        // SourceFiles dedup table: u16 count + [length-prefixed strings]
        writer.Write((ushort)fileList.Count);
        foreach (string filePath in fileList)
        {
            WriteLengthPrefixedString(writer, filePath);
        }
    }

    private static void WriteLocalNames(BinaryWriter writer, string[]? localNames)
    {
        if (localNames is null)
        {
            writer.Write((ushort)0);
            return;
        }
        writer.Write((ushort)localNames.Length);
        foreach (string name in localNames)
        {
            WriteNullableString(writer, name);
        }
    }

    private static void WriteLocalIsConst(BinaryWriter writer, bool[]? localIsConst)
    {
        if (localIsConst is null)
        {
            writer.Write((ushort)0);
            return;
        }
        writer.Write((ushort)localIsConst.Length);
        foreach (bool isConst in localIsConst)
        {
            writer.Write((byte)(isConst ? 1 : 0));
        }
    }

    private static void WriteUpvalueNames(BinaryWriter writer, string[]? upvalueNames)
    {
        if (upvalueNames is null)
        {
            writer.Write((byte)0);
            return;
        }
        writer.Write((byte)upvalueNames.Length);
        foreach (string name in upvalueNames)
        {
            WriteNullableString(writer, name);
        }
    }

    // --- Embedded Source ---

    private static void WriteEmbeddedSource(BinaryWriter writer, string? sourceText)
    {
        if (sourceText is null)
        {
            writer.Write(0u);
            return;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(sourceText);
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
    }

    // --- String helpers ---

    /// <summary>
    /// Write a non-nullable length-prefixed UTF-8 string (u16 length + bytes).
    /// </summary>
    private static void WriteLengthPrefixedString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    /// <summary>
    /// Write a nullable length-prefixed UTF-8 string. Null is encoded as u16 0xFFFF with no payload bytes.
    /// </summary>
    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        if (value is null)
        {
            writer.Write((ushort)0xFFFF);
            return;
        }
        WriteLengthPrefixedString(writer, value);
    }

    private static void WriteStringArray(BinaryWriter writer, string[] values)
    {
        writer.Write((ushort)values.Length);
        foreach (string s in values)
            WriteLengthPrefixedString(writer, s);
    }

    private static void WriteNullableStringArray(BinaryWriter writer, IReadOnlyList<string?> values)
    {
        writer.Write((ushort)values.Count);
        foreach (string? s in values)
            WriteNullableString(writer, s);
    }

    private static void WriteStdlibManifest(BinaryWriter writer, StdlibManifest manifest)
    {
        // Namespace count + names
        writer.Write((ushort)manifest.RequiredNamespaces.Count);
        foreach (string ns in manifest.RequiredNamespaces)
            WriteLengthPrefixedString(writer, ns);

        // Global count + names
        writer.Write((ushort)manifest.RequiredGlobals.Count);
        foreach (string g in manifest.RequiredGlobals)
            WriteLengthPrefixedString(writer, g);

        // Capabilities (u32)
        writer.Write((uint)manifest.MinimumCapabilities);
    }
}
