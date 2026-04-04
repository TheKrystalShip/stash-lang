namespace Stash.Bytecode;

using Stash.Runtime.Types;

/// <summary>
/// A struct method bound to a specific instance. The VM handles this directly
/// in CallValue by injecting the instance as the first argument (self).
/// </summary>
internal sealed class VMBoundMethod
{
    public StashInstance Instance { get; }
    public VMFunction Function { get; }

    public VMBoundMethod(StashInstance instance, VMFunction function)
    {
        Instance = instance;
        Function = function;
    }

    public override string ToString() =>
        $"<bound method {Function.Chunk.Name} on {Instance.TypeName}>";
}

/// <summary>
/// An extension method bound to an arbitrary receiver value. The VM handles this
/// in CallValue by injecting the receiver as the first argument (self).
/// </summary>
internal sealed class VMExtensionBoundMethod
{
    public object? Receiver { get; }
    public VMFunction Function { get; }

    public VMExtensionBoundMethod(object? receiver, VMFunction function)
    {
        Receiver = receiver;
        Function = function;
    }

    public override string ToString() =>
        $"<extension method {Function.Chunk.Name}>";
}
