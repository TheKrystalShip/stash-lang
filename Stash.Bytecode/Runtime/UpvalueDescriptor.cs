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

    /// <summary>True if the captured variable was declared <c>const</c> — closure rebind should throw.</summary>
    public readonly bool IsConst;

    /// <summary>True if the captured variable was declared <c>readonly</c> — closure rebind should deep-freeze the new value.</summary>
    public readonly bool IsReadonly;

    public UpvalueDescriptor(byte index, bool isLocal, bool isConst = false, bool isReadonly = false)
    {
        Index = index;
        IsLocal = isLocal;
        IsConst = isConst;
        IsReadonly = isReadonly;
    }
}
