using Stash.Bytecode.Optimization;
using Stash.Runtime;
using Stash.Runtime.Stdlib;

namespace Stash.Bytecode;

/// <summary>
/// Immutable compiled function prototype for the register-based VM.
/// Each instruction is a 32-bit word in the Code array.
/// </summary>
public sealed class Chunk
{
    /// <summary>Instruction stream (32-bit words).</summary>
    public uint[] Code { get; }

    /// <summary>Constant pool.</summary>
    public StashValue[] Constants { get; }

    /// <summary>Maps instruction indices to source locations.</summary>
    public SourceMap SourceMap { get; }

    /// <summary>Number of declared parameters.</summary>
    public int Arity { get; }

    /// <summary>Minimum number of arguments (accounts for defaults).</summary>
    public int MinArity { get; }

    /// <summary>Maximum number of registers used by this function.</summary>
    public int MaxRegs { get; }

    /// <summary>Upvalue descriptors for closure creation.</summary>
    public UpvalueDescriptor[] Upvalues { get; }

    /// <summary>Function name (null for top-level script).</summary>
    public string? Name { get; }

    /// <summary>Whether this function is async.</summary>
    public bool IsAsync { get; }

    /// <summary>Whether this function has a rest parameter.</summary>
    public bool HasRestParam { get; }

    /// <summary>Optimization hint: true if any local may be captured by a closure.</summary>
    public bool MayHaveCapturedLocals { get; internal set; }

    /// <summary>Debug: register index → variable name.</summary>
    public string[]? LocalNames { get; }

    /// <summary>Debug: const flags per register slot.</summary>
    public bool[]? LocalIsConst { get; }

    /// <summary>Debug: upvalue names for closure scope display.</summary>
    public string[]? UpvalueNames { get; }

    /// <summary>Global slot index → variable name mapping.</summary>
    public string[]? GlobalNameTable { get; }

    /// <summary>Total number of global slots.</summary>
    public int GlobalSlotCount { get; }

    /// <summary>Inline cache slots for GetField operations.</summary>
    internal ICSlot[]? ICSlots { get; set; }

    /// <summary>Metadata-based const global initializations: (Slot, ConstIndex) pairs processed before execution.</summary>
    public (ushort Slot, ushort ConstIndex)[]? ConstGlobalInits { get; }

    /// <summary>Optional stdlib manifest declaring required globals for load-time validation.</summary>
    public StdlibManifest? StdlibManifest { get; internal set; }

    /// <summary>
    /// Per-compilation pass-pipeline statistics.  Populated by <see cref="ChunkBuilder.Build"/>
    /// when the optimization pipeline runs.  Not serialized to .stashc — debug use only.
    /// </summary>
    internal PassPipelineStats? PipelineStats { get; set; }

    internal Chunk(
        uint[] code,
        StashValue[] constants,
        SourceMap sourceMap,
        int arity,
        int minArity,
        int maxRegs,
        UpvalueDescriptor[] upvalues,
        string? name,
        bool isAsync,
        bool hasRestParam,
        bool mayHaveCapturedLocals,
        string[]? localNames,
        string[]? localIsConstNames,
        string[]? upvalueNames,
        string[]? globalNameTable,
        int globalSlotCount,
        ICSlot[]? icSlots,
        (ushort Slot, ushort ConstIndex)[]? constGlobalInits = null)
    {
        Code = code;
        Constants = constants;
        SourceMap = sourceMap;
        Arity = arity;
        MinArity = minArity;
        MaxRegs = maxRegs;
        Upvalues = upvalues;
        Name = name;
        IsAsync = isAsync;
        HasRestParam = hasRestParam;
        MayHaveCapturedLocals = mayHaveCapturedLocals;
        LocalNames = localNames;
        LocalIsConst = localIsConstNames != null ? new bool[localIsConstNames.Length] : null;
        UpvalueNames = upvalueNames;
        GlobalNameTable = globalNameTable;
        GlobalSlotCount = globalSlotCount;
        ICSlots = icSlots;
        ConstGlobalInits = constGlobalInits;
    }

    // Overload with bool[] for LocalIsConst directly
    internal Chunk(
        uint[] code,
        StashValue[] constants,
        SourceMap sourceMap,
        int arity,
        int minArity,
        int maxRegs,
        UpvalueDescriptor[] upvalues,
        string? name,
        bool isAsync,
        bool hasRestParam,
        bool mayHaveCapturedLocals,
        string[]? localNames,
        bool[]? localIsConst,
        string[]? upvalueNames,
        string[]? globalNameTable,
        int globalSlotCount,
        ICSlot[]? icSlots,
        (ushort Slot, ushort ConstIndex)[]? constGlobalInits = null)
    {
        Code = code;
        Constants = constants;
        SourceMap = sourceMap;
        Arity = arity;
        MinArity = minArity;
        MaxRegs = maxRegs;
        Upvalues = upvalues;
        Name = name;
        IsAsync = isAsync;
        HasRestParam = hasRestParam;
        MayHaveCapturedLocals = mayHaveCapturedLocals;
        LocalNames = localNames;
        LocalIsConst = localIsConst;
        UpvalueNames = upvalueNames;
        GlobalNameTable = globalNameTable;
        GlobalSlotCount = globalSlotCount;
        ICSlots = icSlots;
        ConstGlobalInits = constGlobalInits;
    }
}
