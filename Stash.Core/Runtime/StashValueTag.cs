namespace Stash.Runtime;

/// <summary>
/// Discriminator tag for the <see cref="StashValue"/> tagged union.
/// Only primitive value types get dedicated tags — all heap-allocated
/// reference types share <see cref="Obj"/>.
/// </summary>
public enum StashValueTag : byte
{
    Null = 0,
    Bool = 1,
    Int = 2,
    Float = 3,
    Obj = 4,
    Byte = 5,  // unsigned 8-bit integer (0-255)
}
