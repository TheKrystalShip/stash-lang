namespace Stash.Bytecode;

/// <summary>
/// Describes how to capture an upvalue when creating a closure.
/// </summary>
public readonly struct UpvalueDescriptor
{
    /// <summary>Index into either the enclosing function's locals or upvalues.</summary>
    public readonly byte Index;

    /// <summary>True if capturing a local from the immediately enclosing function, false if re-capturing an upvalue.</summary>
    public readonly bool IsLocal;

    public UpvalueDescriptor(byte index, bool isLocal)
    {
        Index = index;
        IsLocal = isLocal;
    }
}
