namespace Stash.Runtime.Protocols;

/// <summary>
/// Supports iteration via for-in loops.
/// </summary>
public interface IVMIterable
{
    /// <summary>
    /// Create an iterator for this value.
    /// When indexed is true, the loop uses two variables (value, key).
    /// </summary>
    IVMIterator VMGetIterator(bool indexed);
}
