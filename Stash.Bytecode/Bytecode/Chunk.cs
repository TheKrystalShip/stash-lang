using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Represents a compiled unit of bytecode — either a top-level script or a function body.
/// Contains the instruction stream, constant pool, source map, and metadata.
/// </summary>
public class Chunk
{
    /// <summary>The bytecode instruction stream.</summary>
    public byte[] Code { get; }

    /// <summary>
    /// Constant pool — stores numbers, strings, and nested Chunks referenced by OP_CONST
    /// and other instructions that take a constant pool index.
    /// </summary>
    public StashValue[] Constants { get; }

    /// <summary>Bytecode offset → source location mappings for debugging and error reporting.</summary>
    public SourceMap SourceMap { get; }

    /// <summary>Number of parameters this function accepts.</summary>
    public int Arity { get; }

    /// <summary>Minimum number of arguments (accounts for default parameter values).</summary>
    public int MinArity { get; }

    /// <summary>Number of local variable slots needed on the value stack.</summary>
    public int LocalCount { get; }

    /// <summary>Upvalue capture descriptors for closures.</summary>
    public UpvalueDescriptor[] Upvalues { get; }

    /// <summary>Function name, or null for the top-level script chunk.</summary>
    public string? Name { get; }

    /// <summary>Whether this is an async function.</summary>
    public bool IsAsync { get; }

    /// <summary>Whether the last parameter is a rest parameter (variadic).</summary>
    public bool HasRestParam { get; }

    /// <summary>
    /// Local variable names for debugger inspection. Index = slot number.
    /// Null when debugging info is not needed (e.g., release builds).
    /// </summary>
    public string[]? LocalNames { get; }

    /// <summary>
    /// Per-local constness flags for debugger mutation guards. Index = slot number.
    /// Null when debugging info is not needed.
    /// </summary>
    public bool[]? LocalIsConst { get; }

    /// <summary>
    /// Upvalue variable names for debugger closure scope display. Index = upvalue index.
    /// Null when debugging info is not needed.
    /// </summary>
    public string[]? UpvalueNames { get; }

    /// <summary>
    /// Maps global slot indices to variable names. Shared across all chunks from
    /// the same compilation unit. Used by the VM for error messages and module fallback.
    /// Null for chunks compiled without slot-based globals (e.g. legacy/test paths).
    /// </summary>
    public string[]? GlobalNameTable { get; }

    /// <summary>
    /// Total number of global variable slots needed for this compilation unit.
    /// The VM pre-allocates a <c>StashValue[]</c> of this size.
    /// </summary>
    public int GlobalSlotCount { get; }

    public Chunk(
        byte[] code,
        StashValue[] constants,
        SourceMap sourceMap,
        int arity,
        int minArity,
        int localCount,
        UpvalueDescriptor[] upvalues,
        string? name,
        bool isAsync,
        bool hasRestParam,
        string[]? localNames = null,
        bool[]? localIsConst = null,
        string[]? upvalueNames = null,
        string[]? globalNameTable = null,
        int globalSlotCount = 0)
    {
        Code = code;
        Constants = constants;
        SourceMap = sourceMap;
        Arity = arity;
        MinArity = minArity;
        LocalCount = localCount;
        Upvalues = upvalues;
        Name = name;
        IsAsync = isAsync;
        HasRestParam = hasRestParam;
        LocalNames = localNames;
        LocalIsConst = localIsConst;
        UpvalueNames = upvalueNames;
        GlobalNameTable = globalNameTable;
        GlobalSlotCount = globalSlotCount;
    }
}
