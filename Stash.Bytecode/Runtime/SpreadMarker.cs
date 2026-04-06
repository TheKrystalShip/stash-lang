namespace Stash.Bytecode;

/// <summary>
/// Wraps an iterable value pushed by <see cref="OpCode.Spread"/> so that
/// <see cref="OpCode.Array"/> and <see cref="OpCode.Dict"/> can expand it
/// without changing the compile-time element count.
/// </summary>
internal sealed class SpreadMarker
{
    public readonly object Items;
    public SpreadMarker(object items) => Items = items;
}
